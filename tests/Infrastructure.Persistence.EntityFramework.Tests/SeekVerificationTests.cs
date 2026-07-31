using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework.PostgreSQL;
using Norse.Persistence.EntityFramework.SqlServer;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Well-and-wire spec §9 acceptance 6 — the anchored composite query (the promoted trio:
// CustomerId + EffectiveDate, both indexed via WellContext.cs's composite index, plus Notes as the
// residual leg) verifies the rewriter's output GIVEN the index, never that promotion alone bought
// a seek (design doc §9.6's own framing) — a real seek on a synthetic million-row table is a
// separate, later telemetry exercise, not this fixture's 3-row job.

/// <summary>Postgres half — EXPLAIN-verified index usage.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostgresSeekVerificationTests(PostgresContainerFixture fixture, ITestOutputHelper output)
{
	[Fact]
	async Task Anchored_composite_query_produces_an_index_scan_not_a_sequential_scan()
	{
		var (context, log) = TranslationCanarySupport.CreateLoggedContext(fixture.ConnectionString, NorsePostgresEfProvider.Instance);
		await using var contextDisposable = context.ConfigureAwait(false);

		var map = WellMap.For<WidgetEntity, WidgetView>();
		var alphaEffectiveDate = new DateOnly(2026, 1, 1);
		// EF.Constant, not a bare captured local: ToQueryString() inlines genuine C# literals
		// ("C1", "hot" below) as SQL literals but leaves a captured local extracted as a real
		// parameter (`@alphaEffectiveDate`, unresolved in the printed text) — EXPLAIN needs a fully
		// literal, directly executable statement, and EF.Constant is EF's own sanctioned way to mark
		// a value for literal inlining rather than parameterization.
		Expression<Func<WidgetView, bool>> anchored = v => v.CustomerId == "C1" && v.EffectiveDate == EF.Constant(alphaEffectiveDate) && v.Notes == "hot";
		var rewritten = PredicateRewriter.Rewrite<WidgetEntity, WidgetView>(anchored, map);
		var query = context.Set<WidgetEntity>().AsNoTracking().Where(rewritten).Select(ViewSelector.For<WidgetEntity, WidgetView>(map));

		var sql = SeekVerificationSupport.StripParameterCommentPreamble(query.ToQueryString());
		var tableName = context.Model.FindEntityType(typeof(WidgetEntity))!.GetTableName();

		// enable_seqscan=off is a standard, honest Postgres testing technique for proving a
		// predicate CAN use an available index — it does not fabricate data or the query shape, it
		// only removes the seq-scan escape hatch the planner would otherwise sensibly take on a
		// 3-row fixture table (real-volume evidence is the design doc's separate million-row
		// telemetry exercise, not this correctness test's job — see this file's header).
		await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
		List<string> plan;
		try
		{
			await context.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", TestContext.Current.CancellationToken);
			// `sql` is EF's own ToQueryString() output over a parameterized LINQ query the rewriter
			// built from a strongly-typed predicate — never caller/user input, so the analyzer's
			// SQL-injection concern does not apply; EXPLAIN accepts no parameters of its own, so
			// SqlQuery's parameterized-interpolation form is not an option here.
#pragma warning disable EF1003
			plan = await context.Database.SqlQueryRaw<string>("EXPLAIN " + sql).ToListAsync(TestContext.Current.CancellationToken);
#pragma warning restore EF1003
		}
		finally
		{
			await context.Database.CloseConnectionAsync();
		}

		TranslationCanarySupport.Dump(output, "Postgres: Anchored_composite_query_produces_an_index_scan_not_a_sequential_scan (captured SQL)", log);
		TranslationCanarySupport.Dump(output, "Postgres: Anchored_composite_query_produces_an_index_scan_not_a_sequential_scan (EXPLAIN)", plan);

		plan.ShouldContain(l => l.Contains("Index Scan", StringComparison.Ordinal) || l.Contains("Index Cond", StringComparison.Ordinal));
		plan.ShouldNotContain(l => l.Contains($"Seq Scan on {tableName}", StringComparison.Ordinal));
	}
}

/// <summary>SQL Server half — sargable-shape assertion over the captured SQL (spec §9.6: plan XML is telemetry, not asserted).</summary>
[Collection(nameof(SqlServerCollection))]
public sealed class SqlServerSeekVerificationTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
{
	public static bool IsDockerAvailable => DockerAvailability.IsAvailable;

	[Fact(SkipUnless = nameof(IsDockerAvailable), Skip = "Requires a running Docker daemon.")]
	async Task Anchored_composite_query_is_sargable_shaped()
	{
		TranslationCanarySupport.SkipUnlessAvailable(fixture);
		var (context, log) = TranslationCanarySupport.CreateLoggedContext(fixture.ConnectionString, NorseSqlServerEfProvider.Instance);
		await using var contextDisposable = context.ConfigureAwait(false);

		var map = WellMap.For<WidgetEntity, WidgetView>();
		var alphaEffectiveDate = new DateOnly(2026, 1, 1);
		Expression<Func<WidgetView, bool>> anchored = v => v.CustomerId == "C1" && v.EffectiveDate == alphaEffectiveDate && v.Notes == "hot";
		var rewritten = PredicateRewriter.Rewrite<WidgetEntity, WidgetView>(anchored, map);
		var results = await context.Set<WidgetEntity>().AsNoTracking().Where(rewritten).Select(ViewSelector.For<WidgetEntity, WidgetView>(map))
			.ToListAsync(TestContext.Current.CancellationToken);

		TranslationCanarySupport.Dump(output, "SqlServer: Anchored_composite_query_is_sargable_shaped (captured SQL)", log);

		results.Count.ShouldBe(1);
		// Sargable-shaped: the indexed columns (CustomerId, EffectiveDate) appear as bare equality
		// comparisons, never wrapped in a function (CAST/CONVERT/UPPER/etc.) — wrapping either column
		// would make the composite index unusable regardless of its existence. Matched without
		// assuming a specific table alias (e.g. "[w]" vs "[w0]") — this file's SQL Server half never
		// ran against a real engine on this task's arm64 dev host (see SqlServerContainerFixture's
		// remarks), so the assertion avoids guessing EF's exact alias-naming output.
		var commandText = log.First(l => l.Contains("SELECT", StringComparison.Ordinal) && l.Contains("FROM", StringComparison.Ordinal));
		commandText.ShouldContain(".[CustomerId] = ");
		commandText.ShouldContain(".[EffectiveDate] = ");
		commandText.ShouldNotContain("CAST(");
		commandText.ShouldNotContain("CONVERT(");
	}
}

static class SeekVerificationSupport
{
	/// <summary>
	/// <c>IQueryable.ToQueryString()</c> prefixes the runnable SQL with commented-out
	/// parameter-value lines (<c>-- @__p_0='...'</c>) for readability; a raw <c>EXPLAIN</c> needs the
	/// bare statement immediately after the keyword, so the preamble is stripped before concatenation.
	/// </summary>
	public static string StripParameterCommentPreamble(string sql)
	{
		var lines = sql.Split('\n');
		var firstStatementLine = Array.FindIndex(lines, l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(l));
		return firstStatementLine < 0 ? sql : string.Join('\n', lines[firstStatementLine..]);
	}
}
