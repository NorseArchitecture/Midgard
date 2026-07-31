using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;
using Norse.Persistence.EntityFramework.SqlServer;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Well-and-wire spec §9 acceptance 5/6/10 — every test here captures the generated SQL via
// context.Database command logging (LogTo into a test-owned List<string>) and asserts on it; the
// captured SQL strings ARE the telemetry deliverable (spec §10), dumped via ITestOutputHelper so
// the run log is the record. Both provider variants share the same three canaries; kept in one
// file (per the Task 6 brief's own file plan) rather than one file per provider, since the whole
// point is a side-by-side parity record. Each test builds its own WellContext directly — never via
// PostgresContainerFixture/SqlServerContainerFixture's own ContextFactory, whose options are fixed
// at container-start time with no LogTo wired in.

/// <summary>Postgres half of the translation-canary parity suite.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostgresTranslationCanaryTests(PostgresContainerFixture fixture, ITestOutputHelper output)
{
	(Repository<WellContext, WidgetEntity, WidgetView> Repository, List<string> Log) CreateLoggedRepository() =>
		TranslationCanarySupport.CreateLoggedRepository(fixture.ConnectionString, NorsePostgresEfProvider.Instance);

	[Fact]
	async Task Promoted_collection_any_emits_exists_not_client_eval()
	{
		var (repository, log) = CreateLoggedRepository();
		var outcome = await repository.ListAsync(v => v.Tags.Any(t => t.Label == "featured"), TestContext.Current.CancellationToken);
		TranslationCanarySupport.Dump(output, "Postgres: Promoted_collection_any_emits_exists_not_client_eval", log);

		outcome.Match(list => list.Count, _ => -1).ShouldBe(1);
		log.ShouldContain(l => l.Contains("EXISTS", StringComparison.Ordinal));
	}

	[Fact]
	async Task Unpromoted_json_collection_any_translates_server_side()
	{
		var (repository, log) = CreateLoggedRepository();
		// Labels is view-extra (WellContext.cs remarks) — no entity counterpart, so the rewriter
		// routes this Any() through the JSON path (e.View.Labels), not a relational navigation. The
		// EF-version-sensitive canary: if this doesn't translate, EF throws InvalidOperationException
		// here and the test fails loudly rather than silently falling back — see this file's header.
		var outcome = await repository.ListAsync(v => v.Labels.Any(l => l == "featured"), TestContext.Current.CancellationToken);
		TranslationCanarySupport.Dump(output, "Postgres: Unpromoted_json_collection_any_translates_server_side", log);

		outcome.Match(list => list.Count, _ => -1).ShouldBe(1);
		log.ShouldContain(l =>
			l.Contains("jsonb_array_elements", StringComparison.Ordinal) ||
			l.Contains("->", StringComparison.Ordinal));
	}

	[Fact]
	async Task Single_take_two_sql_matches_native_single_or_default()
	{
		var (repository, repositoryLog) = CreateLoggedRepository();
		await repository.SingleAsync(v => v.Name == "beta", TestContext.Current.CancellationToken);

		var (context, nativeLog) = TranslationCanarySupport.CreateLoggedContext(fixture.ConnectionString, NorsePostgresEfProvider.Instance);
		await using var contextDisposable = context.ConfigureAwait(false);
		var map = WellMap.For<WidgetEntity, WidgetView>();
		var rewritten = PredicateRewriter.Rewrite<WidgetEntity, WidgetView>(v => v.Name == "beta", map);
		await context.Set<WidgetEntity>().AsNoTracking().Where(rewritten).Select(ViewSelector.For<WidgetEntity, WidgetView>(map))
			.SingleOrDefaultAsync(TestContext.Current.CancellationToken);

		TranslationCanarySupport.Dump(output, "Postgres: Single_take_two_sql_matches_native_single_or_default (repository)", repositoryLog);
		TranslationCanarySupport.Dump(output, "Postgres: Single_take_two_sql_matches_native_single_or_default (native SingleOrDefaultAsync)", nativeLog);

		// Discovered SQL-shape nuance (not a translation gap — both queries run and both are a
		// 2-row limiting operation, same query plan family): EF Core always parameterizes an
		// explicit user-code Take(N) — Repository.SingleAsync's `Take(2)` — as `LIMIT @p`, but keeps
		// its own internally-injected SingleOrDefaultAsync row-limiting clause a literal `LIMIT 2`.
		// The two are compared by bound value, not raw text, for exactly that reason.
		repositoryLog.ShouldContain(l => l.Contains("LIMIT @p", StringComparison.Ordinal));
		repositoryLog.ShouldContain(l => l.Contains("@p='2'", StringComparison.Ordinal));
		nativeLog.ShouldContain(l => l.Contains("LIMIT 2", StringComparison.Ordinal));
	}
}

/// <summary>SQL Server half of the translation-canary parity suite.</summary>
[Collection(nameof(SqlServerCollection))]
public sealed class SqlServerTranslationCanaryTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
{
	public static bool IsDockerAvailable => DockerAvailability.IsAvailable;

	(Repository<WellContext, WidgetEntity, WidgetView> Repository, List<string> Log) CreateLoggedRepository() =>
		TranslationCanarySupport.CreateLoggedRepository(fixture.ConnectionString, NorseSqlServerEfProvider.Instance);

	[Fact(SkipUnless = nameof(IsDockerAvailable), Skip = "Requires a running Docker daemon.")]
	async Task Promoted_collection_any_emits_exists_not_client_eval()
	{
		TranslationCanarySupport.SkipUnlessAvailable(fixture);
		var (repository, log) = CreateLoggedRepository();
		var outcome = await repository.ListAsync(v => v.Tags.Any(t => t.Label == "featured"), TestContext.Current.CancellationToken);
		TranslationCanarySupport.Dump(output, "SqlServer: Promoted_collection_any_emits_exists_not_client_eval", log);

		outcome.Match(list => list.Count, _ => -1).ShouldBe(1);
		log.ShouldContain(l => l.Contains("EXISTS", StringComparison.Ordinal));
	}

	[Fact(SkipUnless = nameof(IsDockerAvailable), Skip = "Requires a running Docker daemon.")]
	async Task Unpromoted_json_collection_any_translates_server_side()
	{
		TranslationCanarySupport.SkipUnlessAvailable(fixture);
		var (repository, log) = CreateLoggedRepository();
		var outcome = await repository.ListAsync(v => v.Labels.Any(l => l == "featured"), TestContext.Current.CancellationToken);
		TranslationCanarySupport.Dump(output, "SqlServer: Unpromoted_json_collection_any_translates_server_side", log);

		outcome.Match(list => list.Count, _ => -1).ShouldBe(1);
		// Confirmed against real SQL Server 2025 CI output on 2026-07-31 (x64 GitHub Actions
		// runner — this test never ran on this repo's own arm64 dev host, which cannot start a SQL
		// Server container at all; see SqlServerContainerFixture's remarks): the actual translation
		// is JSON_CONTAINS(JSON_QUERY([w].[View], '$.Labels'), N'featured') = 1, SQL Server 2025's
		// native-JSON-type array-membership function — a better fit for this platform's forced
		// compat-170 floor than the older OPENJSON+cross-apply pattern this assertion originally
		// guessed at. Both are real, valid server-side translations (never client-eval, never a
		// throw), so both are accepted — same "accept multiple valid EF shapes" pattern already used
		// for the TOP(@__p_0)/TOP(@p) parameter-naming check below.
		log.ShouldContain(l =>
			l.Contains("JSON_CONTAINS", StringComparison.OrdinalIgnoreCase) ||
			l.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(SkipUnless = nameof(IsDockerAvailable), Skip = "Requires a running Docker daemon.")]
	async Task Single_take_two_sql_matches_native_single_or_default()
	{
		TranslationCanarySupport.SkipUnlessAvailable(fixture);
		var (repository, repositoryLog) = CreateLoggedRepository();
		await repository.SingleAsync(v => v.Name == "beta", TestContext.Current.CancellationToken);

		var (context, nativeLog) = TranslationCanarySupport.CreateLoggedContext(fixture.ConnectionString, NorseSqlServerEfProvider.Instance);
		await using var contextDisposable = context.ConfigureAwait(false);
		var map = WellMap.For<WidgetEntity, WidgetView>();
		var rewritten = PredicateRewriter.Rewrite<WidgetEntity, WidgetView>(v => v.Name == "beta", map);
		await context.Set<WidgetEntity>().AsNoTracking().Where(rewritten).Select(ViewSelector.For<WidgetEntity, WidgetView>(map))
			.SingleOrDefaultAsync(TestContext.Current.CancellationToken);

		TranslationCanarySupport.Dump(output, "SqlServer: Single_take_two_sql_matches_native_single_or_default (repository)", repositoryLog);
		TranslationCanarySupport.Dump(output, "SqlServer: Single_take_two_sql_matches_native_single_or_default (native SingleOrDefaultAsync)", nativeLog);

		// Same discovered nuance as the Postgres half (see its remarks): EF Core parameterizes an
		// explicit user-code Take(2) but keeps SingleOrDefaultAsync's own injected row-limiting
		// clause a literal — compared by bound value, not raw text.
		repositoryLog.ShouldContain(l => l.Contains("TOP(@__p_0)", StringComparison.Ordinal) || l.Contains("TOP(@p)", StringComparison.Ordinal));
		repositoryLog.ShouldContain(l => l.Contains("='2'", StringComparison.Ordinal));
		nativeLog.ShouldContain(l => l.Contains("TOP(2)", StringComparison.Ordinal));
	}
}

/// <summary>
/// Shared plumbing for both provider halves of the canary suite, and reused by
/// <c>SeekVerificationTests.cs</c> — building a per-test, LogTo-instrumented
/// <see cref="WellContext"/> the fixtures' own fixed-option factories cannot provide.
/// </summary>
static class TranslationCanarySupport
{
	public static (WellContext Context, List<string> Log) CreateLoggedContext(string connectionString, INorseEfProvider provider)
	{
		List<string> log = [];
		DbContextOptionsBuilder<WellContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null);
		// EnableSensitiveDataLogging: test-only, throwaway synthetic data (WellContext.cs's remarks)
		// — needed so a captured command log's parameter list shows the actual bound value ("@p='2'")
		// rather than a redacted "?", since Single_take_two_sql_matches_native_single_or_default
		// compares the repository's parameterized Take(2) against native SingleOrDefaultAsync's
		// inlined literal by value, not by raw text (see that test's remarks).
		optionsBuilder.EnableSensitiveDataLogging();
		optionsBuilder.LogTo(log.Add, LogLevel.Information);
		return (new WellContext(optionsBuilder.Options), log);
	}

	public static (Repository<WellContext, WidgetEntity, WidgetView> Repository, List<string> Log) CreateLoggedRepository(string connectionString, INorseEfProvider provider)
	{
		List<string> log = [];
		DbContextOptionsBuilder<WellContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null);
		optionsBuilder.EnableSensitiveDataLogging();
		optionsBuilder.LogTo(log.Add, LogLevel.Information);
		SingleUseContextFactory factory = new(optionsBuilder.Options);
		return (new(factory, WellMap.For<WidgetEntity, WidgetView>()), log);
	}

	public static void Dump(ITestOutputHelper output, string label, IReadOnlyList<string> log)
	{
		output.WriteLine($"--- {label} ---");
		foreach (var entry in log)
			output.WriteLine(entry);
	}

	/// <summary>
	/// Runtime (not attribute-time) skip gate for SQL Server tests — see
	/// <see cref="SqlServerContainerFixture.Available"/>'s remarks: a collection fixture's
	/// <c>InitializeAsync</c> throwing fails every test in the collection before any test body ever
	/// runs, so the container-actually-came-up check can only be applied here, inside each test.
	/// </summary>
	public static void SkipUnlessAvailable(SqlServerContainerFixture fixture)
	{
		if (!fixture.Available)
			Assert.Skip($"SQL Server container did not come up in this environment: {fixture.UnavailableReason ?? "Docker unreachable"}");
	}

	sealed class SingleUseContextFactory(DbContextOptions<WellContext> options) : IDbContextFactory<WellContext>
	{
		public WellContext CreateDbContext() =>
			new(options);
	}
}
