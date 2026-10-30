using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	const string Account = "Test.Account";
	const string Other = "Test.Other";
	const string Member = "Test.Member";
	const string Elevated = "Test.Elevated";

	static DateTimeOffset Now => new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

	static AuthorizationBehavior<PolicedRequest, bool> Behavior(ClaimsPrincipal user, bool authorized)
	{
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "Test.Policy")
			.Returns(authorized ?
				AuthorizationResult.Success() :
				AuthorizationResult.Failed());
		return new(authorizationService, new FixedPrincipal(user), new HttpContextAccessor());
	}

	[Fact]
	async Task Not_authenticated_returns_Unauthorized()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"),
				TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task Authenticated_but_policy_fails_returns_Forbidden()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"),
				TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_calls_next()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: true)
			.Handle(new PolicedRequest(), () => ValueTask.FromResult(Outcome<bool>.Ok(true)),
				TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	void A_request_type_with_no_policy_is_a_hard_failure_at_first_touch()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<UnpolicedRequest>.OperationPolicies)
			.InnerException.ShouldBeOfType<InvalidOperationException>();
	}

	[Fact]
	async Task A_fresh_caller_reaches_the_handler()
	{
		var outcome = await HandleAsync(new AccountThenFresh(), Caller(Now));

		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task A_stale_caller_gets_forbidden_with_the_code_and_next_url()
	{
		var problem = ShouldFail(await HandleAsync(new AccountThenFresh(), Caller(Stale)));

		problem.Category.ShouldBe(ErrorCategory.Forbidden);
		problem.Code.ShouldBe("reauthentication-required");
		problem.NextUrl.ShouldBe("/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys");
	}

	[Fact]
	async Task Next_url_carries_the_path_base()
	{
		var problem = ShouldFail(await HandleAsync(new AccountThenFresh(), Caller(Stale), pathBase: "/app"));

		problem.Category.ShouldBe(ErrorCategory.Forbidden);
		problem.Code.ShouldBe(NorseErrorCodes.ReauthenticationRequired);
		problem.NextUrl.ShouldBe("/app/Account/Login?returnUrl=%2Fapp%2FAccount%2FManage%2FPasskeys");
	}

	[Fact]
	async Task Any_other_failure_stays_bare()
	{
		// A fresh caller who fails the operation policy.
		ShouldBeBareForbidden(await HandleAsync(new AccountThenFresh(), Caller(Now, member: false)));
		// A plain request whose only policy fails.
		ShouldBeBareForbidden(await HandleAsync(new AccountOnly(), Caller(Stale, member: false)));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	async Task An_operation_failure_on_a_stale_caller_stays_bare_in_either_attribute_order(bool operationFirst)
	{
		var anonymous = Principal(null, NorseRoles.Anonymous);

		ShouldBeBareForbidden(operationFirst ?
			await HandleAsync(new AccountThenFresh(), anonymous) :
			await HandleAsync(new FreshThenAccount(), anonymous));
	}

	[Fact]
	async Task Multiple_failing_operation_policies_stay_bare()
	{
		ShouldBeBareForbidden(await HandleAsync(new TwoOperationsThenFresh(), Caller(Stale, member: false)));
	}

	[Fact]
	async Task Freshness_is_not_evaluated_when_an_operation_policy_fails()
	{
		CountingFreshness counter = new();

		ShouldBeBareForbidden(await HandleAsync(new AccountThenFresh(), Caller(Stale, member: false), counter: counter));
		counter.Calls.ShouldBe(0);
		// And it is evaluated once the operation policies pass, so the zero above is the ordering, not a dead counter.
		await HandleAsync(new AccountThenFresh(), Caller(Stale), counter: counter);
		counter.Calls.ShouldBe(1);
	}

	[Fact]
	async Task A_freshness_failure_on_an_unauthenticated_caller_is_unauthorized_and_bare()
	{
		// Only reachable when an operation policy admits an unauthenticated principal; the hop is never offered to
		// a caller with no identity to re-prove.
		var problem = ShouldFail(await HandleAsync(new OpenThenFresh(), new ClaimsPrincipal(new ClaimsIdentity())));

		problem.Category.ShouldBe(ErrorCategory.Unauthorized);
		problem.Code.ShouldBeNull();
		problem.NextUrl.ShouldBeNull();
	}

	static DateTimeOffset Stale => Now - TimeSpan.FromMinutes(11);

	static ClaimsPrincipal Caller(DateTimeOffset authTime, bool member = true) =>
		Principal(authTime, member ? Member : Elevated);

	static ClaimsPrincipal Principal(DateTimeOffset? authTime, string role)
	{
		List<Claim> claims = [new(ClaimTypes.Role, role)];
		if (authTime is { } time)
			claims.Add(new(NorseClaimTypes.AuthTime, time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
		return new(new ClaimsIdentity(claims, NorseLanes.Identity));
	}

	static Problem ShouldFail(Outcome<bool> outcome)
	{
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		return failed.Problem;
	}

	static void ShouldBeBareForbidden(Outcome<bool> outcome)
	{
		var problem = ShouldFail(outcome);
		problem.Category.ShouldBe(ErrorCategory.Forbidden);
		problem.Code.ShouldBeNull();
		problem.NextUrl.ShouldBeNull();
	}

	// The freshness handler and its options come from AddNorseAuthentication(), exactly as a host gets them.
	static async Task<Outcome<bool>> HandleAsync<TRequest>(
		TRequest request, ClaimsPrincipal user, string pathBase = "", CountingFreshness? counter = null)
	{
		ServiceCollection services = new();
		services
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
			.AddLogging()
			.AddAuthorizationCore(options =>
			{
				options.AddPolicy(Account, policy => policy.RequireRole(Member));
				options.AddPolicy(Other, policy => policy.RequireRole("Test.Nobody"));
				options.AddPolicy("Test.Open", policy => policy.RequireAssertion(_ => true));
				options.AddPolicy(NorsePolicies.RecentAuthentication, NorsePlatformPolicies.RecentAuthentication);
			})
			.AddNorseAuthentication();
		if (counter is not null)
			services.AddSingleton<IAuthorizationHandler>(counter);
		await using var provider = services.BuildServiceProvider();

		DefaultHttpContext httpContext = new();
		httpContext.Request.PathBase = pathBase;
		AuthorizationBehavior<TRequest, bool> behavior = new(
			provider.GetRequiredService<IAuthorizationService>(),
			new FixedPrincipal(user),
			new HttpContextAccessor { HttpContext = httpContext });
		return await behavior.Handle(request, () => ValueTask.FromResult(Outcome<bool>.Ok(true)),
			TestContext.Current.CancellationToken);
	}

	[Authorize(Policy = "Test.Policy")]
	public sealed record PolicedRequest : IQueryRequest<bool>;

	public sealed record UnpolicedRequest : IQueryRequest<bool>;

	[Authorize(Policy = Account)]
	public sealed record AccountOnly : IQueryRequest<bool>;

	[Authorize(Policy = Account)]
	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record AccountThenFresh : ICommandRequest<bool>;

	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[Authorize(Policy = Account)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record FreshThenAccount : ICommandRequest<bool>;

	[Authorize(Policy = Account)]
	[Authorize(Policy = Other)]
	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record TwoOperationsThenFresh : ICommandRequest<bool>;

	[Authorize(Policy = "Test.Open")]
	[Authorize(Policy = NorsePolicies.RecentAuthentication)]
	[ReauthenticationLanding("/Account/Manage/Passkeys")]
	public sealed record OpenThenFresh : ICommandRequest<bool>;

	sealed class FixedPrincipal(ClaimsPrincipal principal) : IPrincipalAccessor
	{
		public ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(principal);
	}

	sealed class CountingFreshness : AuthorizationHandler<RecentAuthenticationRequirement>
	{
		public int Calls { get; private set; }

		protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
			RecentAuthenticationRequirement requirement)
		{
			Calls++;
			return Task.CompletedTask;
		}
	}
}
