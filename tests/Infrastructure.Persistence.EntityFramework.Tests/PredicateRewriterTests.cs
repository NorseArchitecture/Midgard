using System.Linq.Expressions;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

public sealed class PredicateRewriterTests
{
	static readonly WellMap _map = WellMap.For<PolicyEntity, PolicyView>();

	[Fact]
	void Promoted_scalar_access_retargets_to_the_entity_column()
	{
		Expression<Func<PolicyView, bool>> predicate = v => v.CustomerId == "C1";
		var rewritten = PredicateRewriter.Rewrite<PolicyEntity, PolicyView>(predicate, _map);
		rewritten.ToString().ShouldBe("e => (e.CustomerId == \"C1\")");
	}

	[Fact]
	void Unpromoted_member_access_routes_through_the_view_json_path()
	{
		Expression<Func<PolicyView, bool>> predicate = v => v.Notes == "hot";
		var rewritten = PredicateRewriter.Rewrite<PolicyEntity, PolicyView>(predicate, _map);
		rewritten.ToString().ShouldBe("e => (e.View.Notes == \"hot\")");
	}

	[Fact]
	void Promoted_collection_any_retargets_to_the_relational_navigation()
	{
		Expression<Func<PolicyView, bool>> predicate = v => v.ClassCodes.Any(c => c.Code == "8810");
		var rewritten = PredicateRewriter.Rewrite<PolicyEntity, PolicyView>(predicate, _map);
		rewritten.ToString().ShouldBe("e => e.ClassCodes.Any(c => (c.Code == \"8810\"))");
	}

	[Fact]
	void Mixed_predicates_rewrite_each_leg_independently()
	{
		Expression<Func<PolicyView, bool>> predicate =
			v => v.CustomerId == "C1" && v.Notes == "hot" && v.ClassCodes.Any(c => c.Code == "8810");
		var rewritten = PredicateRewriter.Rewrite<PolicyEntity, PolicyView>(predicate, _map);
		rewritten.ToString().ShouldBe(
			"e => (((e.CustomerId == \"C1\") AndAlso (e.View.Notes == \"hot\")) AndAlso e.ClassCodes.Any(c => (c.Code == \"8810\")))");
	}
}
