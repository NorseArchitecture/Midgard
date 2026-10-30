using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class PolicyCacheTests
{
	[Fact]
	void Partition_is_by_name_not_order()
	{
		PolicyCache<OperationFirst>.OperationPolicies.ShouldBe(["Test.Operation"]);
		PolicyCache<FreshnessFirst>.OperationPolicies.ShouldBe(PolicyCache<OperationFirst>.OperationPolicies);
		PolicyCache<OperationFirst>.RequiresRecentAuthentication.ShouldBeTrue();
		PolicyCache<FreshnessFirst>.RequiresRecentAuthentication.ShouldBeTrue();
		PolicyCache<OperationFirst>.ReauthenticationLanding.ShouldBe("/Account/Manage/Passkeys");
		PolicyCache<FreshnessFirst>.ReauthenticationLanding.ShouldBe("/Account/Manage/Passkeys");
	}

	[Fact]
	void A_plain_request_names_its_one_operation_policy_and_no_freshness()
	{
		PolicyCache<Plain>.OperationPolicies.ShouldBe(["Test.Operation"]);
		PolicyCache<Plain>.RequiresRecentAuthentication.ShouldBeFalse();
		PolicyCache<Plain>.ReauthenticationLanding.ShouldBeNull();
	}

	[Fact]
	void Operation_policies_are_ordinal_and_distinct()
	{
		// Reflection promises no attribute order, so the list is sorted by name: the same declaration set always
		// yields the same list, whichever order the compiler emitted it in.
		PolicyCache<TwoOperations>.OperationPolicies.ShouldBe(["Test.First", "Test.Second"]);
	}

	[Fact]
	void Recent_authentication_without_a_landing_throws()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<NoLanding>.OperationPolicies)
			.InnerException.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("ReauthenticationLanding");
	}

	[Fact]
	void Recent_authentication_alone_throws()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<FreshnessAlone>.OperationPolicies)
			.InnerException.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("operation policy");
	}

	[Fact]
	void An_authorize_attribute_naming_no_policy_throws()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<BlankPolicy>.OperationPolicies)
			.InnerException.ShouldBeOfType<InvalidOperationException>();
	}

	[Authorize(Policy = "Test.Operation")]
	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record OperationFirst : ICommandRequest<bool>;

	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[Authorize(Policy = "Test.Operation")]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record FreshnessFirst : ICommandRequest<bool>;

	[Authorize(Policy = "Test.Operation")]
	public sealed record Plain : IQueryRequest<bool>;

	[Authorize(Policy = "Test.Second")]
	[Authorize(Policy = "Test.First")]
	[Authorize(Policy = "Test.Second")]
	public sealed record TwoOperations : IQueryRequest<bool>;

	[Authorize(Policy = "Test.Operation")]
	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	public sealed record NoLanding : ICommandRequest<bool>;

	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record FreshnessAlone : ICommandRequest<bool>;

	[Authorize(Policy = "Test.Operation")]
	[Authorize]
	public sealed record BlankPolicy : IQueryRequest<bool>;
}
