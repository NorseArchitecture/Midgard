using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Brief-vs-fixture reconciliation (Task 4): the brief's literal tests were drafted against a
// "dup" CustomerId pair, but PostgresContainerFixture's real seed (Task 3) already reserves
// alpha/gamma sharing CustomerId "C1" specifically for this MultipleMatches case (see the
// fixture's own doc comment). Adapting the predicate literal to "C1" — rather than adding two
// more seed rows for an unused "dup" pair — keeps the 0/1/2-match cardinality coverage intact
// with zero fixture churn.
[Collection(nameof(PostgresCollection))]
public sealed class RepositorySingleTests(PostgresContainerFixture fixture)
{
	Repository<WellContext, WidgetEntity, WidgetView> CreateRepository() =>
		new(fixture.ContextFactory, WellMap.For<WidgetEntity, WidgetView>());

	[Fact]
	async Task Single_returns_the_view_over_exactly_one_match()
	{
		var outcome = await CreateRepository().SingleAsync(v => v.Name == "beta", TestContext.Current.CancellationToken);
		outcome.Match(v => v.Name, _ => "<problem>").ShouldBe("beta");
	}

	[Fact]
	async Task Single_returns_not_found_over_zero_matches()
	{
		var outcome = await CreateRepository().SingleAsync(v => v.Name == "no-such", TestContext.Current.CancellationToken);
		outcome.Match(_ => ErrorCategory.Unspecified, p => p.Category).ShouldBe(ErrorCategory.NotFound);
	}

	[Fact]
	async Task Single_returns_multiple_matches_over_two_matches()
	{
		var outcome = await CreateRepository().SingleAsync(v => v.CustomerId == "C1", TestContext.Current.CancellationToken);
		outcome.Match(_ => ErrorCategory.Unspecified, p => p.Category).ShouldBe(ErrorCategory.MultipleMatches);
	}

	[Fact]
	async Task An_untranslatable_predicate_throws_and_is_never_a_multiple_matches_problem()
	{
		// Failure-channel purity (spec §5.3, acceptance 9): the exact regression a future
		// "simplification" to EF SingleOrDefaultAsync + catch would introduce. Real exceptions
		// stay exceptions and propagate loudly.
		var repository = CreateRepository();
		await Should.ThrowAsync<InvalidOperationException>(() =>
			repository.SingleAsync(v => Untranslatable(v.Name), TestContext.Current.CancellationToken));
	}

	static bool Untranslatable(string name) => name.Length % 2 == 0;

	[Fact]
	async Task No_code_path_yields_a_succeeded_outcome_with_a_null_value()
	{
		// Invariant test (acceptance 8): success arms always carry a real value across all shapes.
		var repository = CreateRepository();
		var single = await repository.SingleAsync(v => v.Name == "beta", TestContext.Current.CancellationToken);
		single.Match(v => v, _ => null!).ShouldNotBeNull();
		var list = await repository.ListAsync(v => true, TestContext.Current.CancellationToken);
		list.Match(l => l, _ => null!).ShouldNotBeNull();
	}
}
