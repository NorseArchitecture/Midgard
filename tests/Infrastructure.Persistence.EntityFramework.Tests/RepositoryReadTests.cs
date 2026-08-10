using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class RepositoryReadTests(PostgresContainerFixture fixture)
{
	// Helper builds the repository exactly as AddWell will: factory + map.
	Repository<WellContext, WidgetEntity, WidgetView> CreateRepository() =>
		new(fixture.ContextFactory, WellMap.For<WidgetEntity, WidgetView>());

	[Fact]
	async Task Get_returns_the_view_for_a_known_id()
	{
		var outcome = await CreateRepository().GetAsync(fixture.KnownWidgetId, TestContext.Current.CancellationToken);
		outcome.Match(v => v.Name, _ => "<problem>").ShouldBe("alpha");
	}

	[Fact]
	async Task Get_returns_not_found_for_an_unknown_id()
	{
		var outcome = await CreateRepository().GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
		outcome.Match(_ => ErrorCategory.Unspecified, p => p.Category).ShouldBe(ErrorCategory.NotFound);
	}

	[Fact]
	async Task Get_with_projection_projects_in_sql()
	{
		var outcome = await CreateRepository()
			.GetAsync(fixture.KnownWidgetId, v => v.Name, TestContext.Current.CancellationToken);
		outcome.Match(name => name, _ => "<problem>").ShouldBe("alpha");
	}

	[Fact]
	async Task A_value_type_projection_over_an_unknown_id_is_not_found_never_a_fabricated_default()
	{
		// Pins the Take(1)+count materialization of projection overloads: a FirstOrDefaultAsync
		// "simplification" would return default(Guid) here and fabricate a succeeded Outcome
		// from absence.
		var outcome = await CreateRepository()
			.GetAsync(Guid.NewGuid(), v => v.Id, TestContext.Current.CancellationToken);
		outcome.Match(_ => ErrorCategory.Unspecified, p => p.Category).ShouldBe(ErrorCategory.NotFound);
	}

	[Fact]
	async Task First_returns_not_found_over_an_empty_match()
	{
		var outcome = await CreateRepository()
			.FirstAsync(v => v.Name == "no-such", TestContext.Current.CancellationToken);
		outcome.Match(_ => ErrorCategory.Unspecified, p => p.Category).ShouldBe(ErrorCategory.NotFound);
	}

	[Fact]
	async Task List_returns_an_empty_list_as_a_value_never_a_problem()
	{
		var outcome = await CreateRepository()
			.ListAsync(v => v.Name == "no-such", TestContext.Current.CancellationToken);
		outcome.Match(list => list.Count, _ => -1).ShouldBe(0);
	}

	[Fact]
	async Task Residual_json_predicate_translates_server_side()
	{
		// Notes is view-extra: rewriter routes through the JSON path; EF must translate, not client-eval.
		var outcome = await CreateRepository().ListAsync(v => v.Notes == "hot", TestContext.Current.CancellationToken);
		outcome.Match(list => list.Count, _ => -1).ShouldBe(1);
	}
}
