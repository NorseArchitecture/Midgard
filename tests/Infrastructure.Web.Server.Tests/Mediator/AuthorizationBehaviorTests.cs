using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	[Authorize(Policy = "Test.Policy")]
	public sealed record PolicedRequest : IQueryRequest<bool>;

	public sealed record UnpolicedRequest : IQueryRequest<bool>;

	sealed class FixedPrincipal(ClaimsPrincipal principal) : IPrincipalAccessor
	{
		public ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(principal);
	}

	static AuthorizationBehavior<PolicedRequest, bool> Behavior(ClaimsPrincipal user, bool authorized)
	{
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "Test.Policy")
			.Returns(authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());
		return new(authorizationService, new FixedPrincipal(user));
	}

	[Fact]
	async Task Not_authenticated_returns_Unauthorized()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task Authenticated_but_policy_fails_returns_Forbidden()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_calls_next()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: true)
			.Handle(new PolicedRequest(), () => ValueTask.FromResult(Outcome<bool>.Ok(true)), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	void A_request_type_with_no_policy_is_a_hard_failure_at_first_touch()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<UnpolicedRequest>.Policy)
			.InnerException.ShouldBeOfType<InvalidOperationException>();
	}
}
