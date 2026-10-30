using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Authorization;

namespace Norse.Infrastructure.Web.Server.Tests.Authorization;

public sealed class LaneRequirementHandlerTests
{
	const string AudienceClaimType = "aud";

	[Fact]
	async Task Succeeds_when_authentication_type_is_listed()
	{
		(await EvaluateAsync(new LaneRequirementHandler(), new LaneRequirement([NorseLanes.Identity, NorseLanes.User]),
			Stamped(NorseLanes.User))).ShouldBeTrue();
	}

	[Fact]
	async Task Fails_for_an_unlisted_lane()
	{
		LaneRequirement machineOnly = new([NorseLanes.Machine]);

		(await EvaluateAsync(new LaneRequirementHandler(), machineOnly, Stamped(NorseLanes.Identity))).ShouldBeFalse();
		// An identity the platform never stamped names no lane at all, however authenticated it is.
		(await EvaluateAsync(new LaneRequirementHandler(), machineOnly, Stamped("Identity.Application")))
			.ShouldBeFalse();
	}

	// .NET decides "authenticated" as a non-empty AuthenticationType, so the only unauthenticated identities are
	// one with no type and one with an empty type. Listing the empty string as a lane is the one way such an
	// identity could ever look like a match; the handler's IsAuthenticated guard refuses it even then.
	[Fact]
	async Task Lane_requirement_fails_for_an_unauthenticated_identity()
	{
		LaneRequirement requirement = new([string.Empty, NorseLanes.Identity, NorseLanes.User, NorseLanes.Machine]);

		(await EvaluateAsync(new LaneRequirementHandler(), requirement, new())).ShouldBeFalse();
		(await EvaluateAsync(new LaneRequirementHandler(), requirement, new(new ClaimsIdentity()))).ShouldBeFalse();
		(await EvaluateAsync(new LaneRequirementHandler(), requirement, Stamped(string.Empty))).ShouldBeFalse();
	}

	[Theory]
	[InlineData(NorseLanes.Identity)]
	[InlineData(NorseLanes.User)]
	[InlineData(NorseLanes.Machine)]
	async Task Any_lane_succeeds_for_any_stamped_identity(string lane)
	{
		(await EvaluateAsync(new AnyLaneRequirementHandler(), new AnyLaneRequirement(), Stamped(lane))).ShouldBeTrue();
	}

	[Fact]
	async Task Any_lane_succeeds_for_an_empty_principal()
	{
		(await EvaluateAsync(new AnyLaneRequirementHandler(), new AnyLaneRequirement(), new())).ShouldBeTrue();
	}

	[Fact]
	async Task Probe_policy_succeeds_for_an_empty_principal()
	{
		var result = await AuthorizeAsync(new(), NorsePolicies.Probe, NorsePlatformPolicies.Probe);

		result.Succeeded.ShouldBeTrue();
	}

	// Machine no longer calls RequireAuthenticatedUser: its authenticated guarantee rests on the lane handler alone.
	[Fact]
	async Task Machine_policy_fails_for_an_empty_principal()
	{
		var result = await AuthorizeAsync(new(), NorsePolicies.Machine, NorsePlatformPolicies.Machine);

		result.Succeeded.ShouldBeFalse();
	}

	[Fact]
	async Task Machine_policy_admits_a_machine_principal_carrying_the_facade_audience()
	{
		var result = await AuthorizeAsync(Stamped(NorseLanes.Machine, new Claim(AudienceClaimType, NorseAudiences.Facade)),
			NorsePolicies.Machine, NorsePlatformPolicies.Machine);

		result.Succeeded.ShouldBeTrue();
	}

	[Fact]
	async Task Audience_matches_any_aud_claim()
	{
		AudienceRequirement facade = new(NorseAudiences.Facade, null);

		(await EvaluateAsync(new AudienceRequirementHandler(), facade, Stamped(NorseLanes.Machine,
			new Claim(AudienceClaimType, NorseAudiences.User),
			new Claim(AudienceClaimType, NorseAudiences.Facade)))).ShouldBeTrue();
		(await EvaluateAsync(new AudienceRequirementHandler(), facade, Stamped(NorseLanes.Machine,
			new Claim(AudienceClaimType, NorseAudiences.User)))).ShouldBeFalse();
		(await EvaluateAsync(new AudienceRequirementHandler(), facade, Stamped(NorseLanes.Machine))).ShouldBeFalse();
	}

	[Fact]
	async Task Lane_scoped_audience_ignores_other_lanes()
	{
		AudienceRequirement userLaneOnly = new(NorseAudiences.User, NorseLanes.User);

		// A cookie identity carries no aud claim, and the user-lane pin never applies to it.
		(await EvaluateAsync(new AudienceRequirementHandler(), userLaneOnly, Stamped(NorseLanes.Identity)))
			.ShouldBeTrue();
		(await EvaluateAsync(new AudienceRequirementHandler(), userLaneOnly, Stamped(NorseLanes.User)))
			.ShouldBeFalse();
		(await EvaluateAsync(new AudienceRequirementHandler(), userLaneOnly, Stamped(NorseLanes.User,
			new Claim(AudienceClaimType, NorseAudiences.Facade)))).ShouldBeFalse();
		(await EvaluateAsync(new AudienceRequirementHandler(), userLaneOnly, Stamped(NorseLanes.User,
			new Claim(AudienceClaimType, NorseAudiences.User)))).ShouldBeTrue();
	}

	static ClaimsPrincipal Stamped(string lane, params Claim[] claims) =>
		new(new ClaimsIdentity(claims, lane));

	static async Task<bool> EvaluateAsync(
		IAuthorizationHandler handler, IAuthorizationRequirement requirement, ClaimsPrincipal user)
	{
		AuthorizationHandlerContext context = new([requirement], user, null);
		await handler.HandleAsync(context);
		return context.HasSucceeded;
	}

	// The handlers come from AddNorseAuthentication(), exactly as a host gets them.
	static async Task<AuthorizationResult> AuthorizeAsync(
		ClaimsPrincipal user, string policyName, Action<AuthorizationPolicyBuilder> configure)
	{
		ServiceCollection services = new();
		services
			// Every host carries configuration; AddNorseAuthentication() binds Norse:Authentication from it.
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddLogging()
			.AddAuthorizationCore(options => options.AddPolicy(policyName, configure))
			.AddNorseAuthentication();
		await using var provider = services.BuildServiceProvider();
		return await provider
			.GetRequiredService<IAuthorizationService>()
			.AuthorizeAsync(user, policyName);
	}
}
