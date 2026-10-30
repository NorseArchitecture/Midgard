using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Client.Authorization;

namespace Norse.Infrastructure.Web.Client.Tests;

public sealed class ClientLaneRequirementHandlerTests
{
	const string AudienceClaimType = "aud";

	const string PolicyName = "Lane.Under.Test";

	// The browser only ever holds the cookie identity, whatever AuthenticationType its state provider gives it.
	[Theory]
	[InlineData("Identity.Application")]
	[InlineData("ServerAuthentication")]
	[InlineData(NorseLanes.Identity)]
	async Task Client_principal_is_the_identity_lane(string authenticationType)
	{
		var user = Principal(authenticationType);

		(await AuthorizeAsync(user, policy => policy.RequireLane(NorseLanes.Identity))).ShouldBeTrue();
		(await AuthorizeAsync(user, policy => policy.RequireLane(NorseLanes.User, NorseLanes.Identity))).ShouldBeTrue();
		(await AuthorizeAsync(user, policy => policy.RequireLane(NorseLanes.Machine))).ShouldBeFalse();
		(await AuthorizeAsync(user, policy => policy.RequireLane(NorseLanes.User))).ShouldBeFalse();
	}

	[Fact]
	async Task Client_lane_requirement_fails_for_an_unauthenticated_principal()
	{
		(await AuthorizeAsync(new(), policy => policy.RequireLane(NorseLanes.Identity))).ShouldBeFalse();
		(await AuthorizeAsync(new(new ClaimsIdentity()), policy => policy.RequireLane(NorseLanes.Identity)))
			.ShouldBeFalse();
	}

	[Fact]
	async Task Client_any_lane_succeeds_for_an_empty_principal()
	{
		(await AuthorizeAsync(new(), policy => policy.AllowAnyLane())).ShouldBeTrue();
	}

	[Fact]
	async Task Client_probe_policy_succeeds_for_an_empty_principal()
	{
		(await AuthorizeAsync(new(), NorsePlatformPolicies.Probe)).ShouldBeTrue();
	}

	// A user-lane audience pin never applies to the cookie identity the browser holds, so a policy shaped like
	// AccountRead keeps evaluating on the client as it does today.
	[Fact]
	async Task Client_lane_scoped_audience_ignores_other_lanes()
	{
		(await AuthorizeAsync(Principal("Identity.Application"),
			policy => policy.RequireAudience(NorseAudiences.User, NorseLanes.User))).ShouldBeTrue();
		(await AuthorizeAsync(Principal("Identity.Application"),
			policy => policy.RequireAudience(NorseAudiences.User, NorseLanes.Identity))).ShouldBeFalse();
	}

	[Fact]
	async Task Client_unscoped_audience_matches_any_aud_claim()
	{
		(await AuthorizeAsync(Principal("Identity.Application",
				new Claim(AudienceClaimType, NorseAudiences.User), new Claim(AudienceClaimType, NorseAudiences.Facade)),
			policy => policy.RequireAudience(NorseAudiences.Facade))).ShouldBeTrue();
		(await AuthorizeAsync(Principal("Identity.Application"),
			policy => policy.RequireAudience(NorseAudiences.Facade))).ShouldBeFalse();
	}

	static ClaimsPrincipal Principal(string authenticationType, params Claim[] claims) =>
		new(new ClaimsIdentity(claims, authenticationType));

	// The handlers come from AddNorseClientAuthorization(), exactly as a WASM host gets them.
	static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, Action<AuthorizationPolicyBuilder> configure)
	{
		ServiceCollection services = new();
		services
			.AddLogging()
			.AddNorseClientAuthorization()
			.Configure<AuthorizationOptions>(options => options.AddPolicy(PolicyName, configure));
		await using var provider = services.BuildServiceProvider();
		var result = await provider
			.GetRequiredService<IAuthorizationService>()
			.AuthorizeAsync(user, PolicyName);
		return result.Succeeded;
	}
}
