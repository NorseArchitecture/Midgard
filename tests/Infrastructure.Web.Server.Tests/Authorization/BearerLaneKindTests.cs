using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Tests.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authorization;

/// <summary>
///     Ruled fact 3 (BUVY RULING, lanes): a bearer lane means the token's kind, so a policy naming only a lane, with no
///     audience pin, still refuses a token of the other kind routed to that lane. Evaluated as the authorization
///     middleware does, through <see cref="IPolicyEvaluator" />: the lane scheme authenticates, then the policy decides.
/// </summary>
public sealed class BearerLaneKindTests
{
	[Theory]
	[InlineData(NorseSchemes.User, NorseLanes.User, NorseAudiences.Facade)]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine, NorseAudiences.User)]
	async Task A_lane_only_policy_challenges_a_token_of_the_other_kind(string scheme, string lane, string audience)
	{
		var result = await EvaluateAsync(scheme, lane, BearerTokens.User(audience));

		result.Succeeded.ShouldBeFalse();
		result.Challenged.ShouldBeTrue();
	}

	[Theory]
	[InlineData(NorseSchemes.User, NorseLanes.User)]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine)]
	async Task A_lane_only_policy_challenges_a_token_carrying_both_platform_audiences(string scheme, string lane)
	{
		var result = await EvaluateAsync(scheme, lane, BearerTokens.User(NorseAudiences.Facade, NorseAudiences.User));

		result.Challenged.ShouldBeTrue();
	}

	[Theory]
	[InlineData(NorseSchemes.User, NorseLanes.User, NorseAudiences.User)]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine, NorseAudiences.Facade)]
	async Task A_lane_only_policy_admits_its_own_kind(string scheme, string lane, string audience)
	{
		var result = await EvaluateAsync(scheme, lane, BearerTokens.User(audience));

		result.Succeeded.ShouldBeTrue();
	}

	static async Task<PolicyAuthorizationResult> EvaluateAsync(string scheme, string lane, ClaimsPrincipal bearer)
	{
		ServiceCollection services = new();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()).AddLogging().AddAuthorization();
		services.AddNorseAuthentication()
			.AddScheme<StubBearerOptions, StubBearerHandler>(NorseSchemes.Bearer, options => options.Principal = bearer);
		await using var provider = services.BuildServiceProvider();
		await using var scope = provider.CreateAsyncScope();

		var policy = new AuthorizationPolicyBuilder(scheme).RequireLane(lane).Build();
		DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
		var evaluator = scope.ServiceProvider.GetRequiredService<IPolicyEvaluator>();
		var authenticated = await evaluator.AuthenticateAsync(policy, context);
		return await evaluator.AuthorizeAsync(policy, authenticated, context, resource: null);
	}
}
