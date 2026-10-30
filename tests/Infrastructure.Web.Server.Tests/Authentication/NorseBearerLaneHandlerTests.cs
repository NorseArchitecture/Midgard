using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseBearerLaneHandlerTests
{
	const string AudienceClaimType = "aud";

	[Fact]
	async Task Machine_lane_stamps_machine()
	{
		var result = await AuthenticateAsync(NorseSchemes.Machine, BearerTokens.Machine());

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.Machine);
		identity.FindFirst(ClaimTypes.NameIdentifier)!.Value.ShouldBe(BearerTokens.MachineSubject);
		result.Ticket!.AuthenticationScheme.ShouldBe(NorseSchemes.Machine);
	}

	[Fact]
	async Task User_lane_stamps_user()
	{
		var result = await AuthenticateAsync(NorseSchemes.User,
			Bearer(new Claim("sub", "user-1"), new Claim(ClaimTypes.NameIdentifier, "user-1"),
				new Claim(AudienceClaimType, NorseAudiences.User)));

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.User);
		identity.Claims.Select(static claim => (claim.Type, claim.Value))
			.ShouldBe([("sub", "user-1"), (ClaimTypes.NameIdentifier, "user-1"), (AudienceClaimType, NorseAudiences.User)]);
		result.Ticket!.AuthenticationScheme.ShouldBe(NorseSchemes.User);
	}

	// The authenticated identity is the bearer principal's primary Identity: a second identity on the bearer
	// result, already typed as a lane and carrying a claim named like one, contributes nothing to the lane principal.
	[Fact]
	async Task Bearer_lane_principal_has_one_identity()
	{
		ClaimsPrincipal bearer = new([
			new ClaimsIdentity([new Claim("sub", "user-1"), new Claim(AudienceClaimType, NorseAudiences.User)],
				"Stub.Bearer", "name", "role"),
			new ClaimsIdentity([new Claim(NorseLanes.Machine, NorseLanes.Machine)], NorseLanes.Machine)
		]);

		var result = await AuthenticateAsync(NorseSchemes.User, bearer);

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.User);
		identity.NameClaimType.ShouldBe("name");
		identity.RoleClaimType.ShouldBe("role");
		identity.Claims.Select(static claim => (claim.Type, claim.Value))
			.ShouldBe([("sub", "user-1"), (AudienceClaimType, NorseAudiences.User)]);
	}

	// A multi-valued aud arrives as one claim per value; every one is carried onto the lane identity unchanged. An
	// audience outside the platform's two is not a token kind, so it neither admits nor refuses (BUVY RULING, lanes).
	[Fact]
	async Task Bearer_lane_preserves_audience_claims()
	{
		var result = await AuthenticateAsync(NorseSchemes.Machine, BearerTokens.Machine(NorseAudiences.Facade, "urn:resource"));

		result.Principal!.FindAll(AudienceClaimType).Select(static claim => claim.Value)
			.ShouldBe([NorseAudiences.Facade, "urn:resource"]);
	}

	// Ruled fact 1: a bearer lane means the token's kind. Each lane admits its own audience, single or multi-valued.
	[Theory]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine, new[] { NorseAudiences.Facade })]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine, new[] { "urn:resource", NorseAudiences.Facade })]
	[InlineData(NorseSchemes.User, NorseLanes.User, new[] { NorseAudiences.User })]
	[InlineData(NorseSchemes.User, NorseLanes.User, new[] { NorseAudiences.User, "urn:resource" })]
	async Task The_lanes_audience_authenticates_and_stamps_its_lane(string scheme, string lane, string[] audiences)
	{
		var result = await AuthenticateAsync(scheme, BearerTokens.User(audiences));

		result.Succeeded.ShouldBeTrue();
		result.Principal!.Identities.ShouldHaveSingleItem().AuthenticationType.ShouldBe(lane);
	}

	// Ruled fact 2: the wrong audience, no audience, both platform audiences, or an audience differing only by
	// case or whitespace fails authentication on either lane -- a failure, never NoResult, and nothing is stamped.
	[Theory]
	[InlineData(NorseSchemes.Machine, new[] { NorseAudiences.User })]
	[InlineData(NorseSchemes.User, new[] { NorseAudiences.Facade })]
	[InlineData(NorseSchemes.Machine, new string[0])]
	[InlineData(NorseSchemes.User, new string[0])]
	[InlineData(NorseSchemes.Machine, new[] { NorseAudiences.Facade, NorseAudiences.User })]
	[InlineData(NorseSchemes.User, new[] { NorseAudiences.Facade, NorseAudiences.User })]
	[InlineData(NorseSchemes.User, new[] { NorseAudiences.User, NorseAudiences.Facade })]
	[InlineData(NorseSchemes.Machine, new[] { "urn:resource" })]
	[InlineData(NorseSchemes.Machine, new[] { "norse.facade" })]
	[InlineData(NorseSchemes.User, new[] { "NORSE.USER" })]
	[InlineData(NorseSchemes.User, new[] { " Norse.User" })]
	async Task A_token_of_another_kind_fails_authentication(string scheme, string[] audiences)
	{
		var result = await AuthenticateAsync(scheme, audiences.Length is 0 ? BearerTokens.WithoutAudience() : BearerTokens.User(audiences));

		result.Succeeded.ShouldBeFalse();
		result.None.ShouldBeFalse();
		result.Failure.ShouldNotBeNull().Message.ShouldContain(scheme);
	}

	[Fact]
	async Task No_bearer_result_is_no_lane_result()
	{
		var result = await AuthenticateAsync(NorseSchemes.User, bearer: null);

		result.None.ShouldBeTrue();
	}

	[Fact]
	async Task An_unauthenticated_bearer_identity_fails()
	{
		var result = await AuthenticateAsync(NorseSchemes.User, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")])));

		result.Succeeded.ShouldBeFalse();
		result.None.ShouldBeFalse();
		result.Failure.ShouldNotBeNull();
	}

	[Fact]
	async Task A_failed_bearer_result_passes_through_as_a_failure()
	{
		var result = await AuthenticateAsync(NorseSchemes.Machine, bearer: null, bearerFailure: "invalid_token");

		result.Succeeded.ShouldBeFalse();
		result.Failure.ShouldNotBeNull().Message.ShouldBe("invalid_token");
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	void A_blank_lane_is_rejected(string lane) =>
		Should.Throw<InvalidOperationException>(() =>
				new NorseBearerLaneOptions { Lane = lane, Audience = NorseAudiences.User }.Validate(NorseSchemes.User))
			.Message.ShouldContain(NorseSchemes.User);

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	void A_blank_audience_is_rejected(string audience) =>
		Should.Throw<InvalidOperationException>(() =>
				new NorseBearerLaneOptions { Lane = NorseLanes.User, Audience = audience }.Validate(NorseSchemes.User))
			.Message.ShouldContain(NorseSchemes.User);

	[Theory]
	[InlineData(NorseSchemes.Machine)]
	[InlineData(NorseSchemes.User)]
	async Task Challenge_and_forbid_delegate_to_norse_bearer(string lane)
	{
		await using var provider = Services(bearer: null);

		// One scope per request: the handler provider caches a handler per scheme for its scope's lifetime.
		await using var challengeScope = provider.CreateAsyncScope();
		var challenged = Context(challengeScope.ServiceProvider);
		await challenged.ChallengeAsync(lane);
		challenged.Response.Headers[StubBearerHandler.Marker].ToString().ShouldBe("challenge");

		await using var forbidScope = provider.CreateAsyncScope();
		var forbidden = Context(forbidScope.ServiceProvider);
		await forbidden.ForbidAsync(lane);
		forbidden.Response.Headers[StubBearerHandler.Marker].ToString().ShouldBe("forbid");
	}

	[Fact]
	async Task A_host_without_norse_bearer_fails_at_startup()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddNorseAuthentication();
		await using var app = builder.Build();

		var failure = await Should.ThrowAsync<OptionsValidationException>(() =>
			app.StartAsync(TestContext.Current.CancellationToken));

		failure.Message.ShouldContain(NorseSchemes.Bearer);
	}

	static ClaimsPrincipal Bearer(params Claim[] claims) => new(new ClaimsIdentity(claims, "Stub.Bearer"));

	static async Task<AuthenticateResult> AuthenticateAsync(string scheme, ClaimsPrincipal? bearer, string? bearerFailure = null)
	{
		await using var provider = Services(bearer, bearerFailure);
		await using var scope = provider.CreateAsyncScope();
		return await Context(scope.ServiceProvider).AuthenticateAsync(scheme);
	}

	static ServiceProvider Services(ClaimsPrincipal? bearer, string? bearerFailure = null)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseAuthentication()
			.AddScheme<StubBearerOptions, StubBearerHandler>(NorseSchemes.Bearer, options =>
			{
				options.Principal = bearer;
				options.Failure = bearerFailure;
			});
		return services.BuildServiceProvider();
	}

	static DefaultHttpContext Context(IServiceProvider provider) => new() { RequestServices = provider };
}

sealed class StubBearerOptions : AuthenticationSchemeOptions
{
	public ClaimsPrincipal? Principal { get; set; }

	public string? Failure { get; set; }
}

/// <summary>
///     A test-registered <see cref="NorseSchemes.Bearer" />: Midgard declares the scheme name and never implements
///     it, so these facts stand in for the bearer-validating realm. Authenticate returns the configured failure or
///     principal;
///     challenge and forbid leave a marker header so delegation is observable.
/// </summary>
sealed class StubBearerHandler(IOptionsMonitor<StubBearerOptions> options, ILoggerFactory logger, UrlEncoder encoder)
	: AuthenticationHandler<StubBearerOptions>(options, logger, encoder)
{
	public const string Marker = "X-Stub-Bearer";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(Options switch
		{
			{ Failure: { } failure } => AuthenticateResult.Fail(failure),
			{ Principal: { } principal } => AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)),
			_ => AuthenticateResult.NoResult()
		});

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.Headers[Marker] = "challenge";
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.Headers[Marker] = "forbid";
		return Task.CompletedTask;
	}
}
