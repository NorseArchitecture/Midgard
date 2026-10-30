using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseGrpcHandlerTests
{
	const string Authenticate = "authenticate";

	// Spec §7.2: a bearer header decides the request alone. The cookie is never authenticated, so two identities
	// never mix in one request -- asserted on the dispatch itself, not only on the result.
	[Fact]
	async Task A_bearer_header_selects_the_user_lane_and_never_reads_the_cookie()
	{
		var harness = AuthenticationHarness.ForGrpc(BearerTokens.User())
			.WithValidIdentityCookie("cookie@example.test")
			.WithAnonymousCookie(Guid.NewGuid())
			.WithAuthorization("Bearer token");

		var result = await harness.AuthenticateAsync();

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.User);
		identity.FindFirst(ClaimTypes.NameIdentifier)!.Value.ShouldBe(BearerTokens.UserSubject);
		result.Ticket!.AuthenticationScheme.ShouldBe(NorseSchemes.User);
		harness.Dispatched.ShouldBe([(Authenticate, NorseSchemes.User), (Authenticate, NorseSchemes.Bearer)]);
	}

	[Theory]
	[InlineData("bearer token")]
	[InlineData("BEARER token")]
	[InlineData("Bearer")]
	async Task The_bearer_scheme_is_matched_ignoring_case(string authorization)
	{
		var harness = AuthenticationHarness.ForGrpc(BearerTokens.User())
			.WithValidIdentityCookie("cookie@example.test")
			.WithAuthorization(authorization);

		var result = await harness.AuthenticateAsync();

		result.Principal!.Identities.ShouldHaveSingleItem().AuthenticationType.ShouldBe(NorseLanes.User);
		harness.Dispatched.ShouldNotContain((Authenticate, IdentityConstants.ApplicationScheme));
	}

	// A bearer the source rejects is the request's answer: the valid cookie beside it is not a fallback.
	[Fact]
	async Task A_rejected_bearer_does_not_fall_back_to_the_cookie()
	{
		var harness = AuthenticationHarness.ForGrpc(bearer: null)
			.WithValidIdentityCookie("cookie@example.test")
			.WithAuthorization("Bearer expired");

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeFalse();
		harness.Dispatched.ShouldNotContain((Authenticate, IdentityConstants.ApplicationScheme));
		harness.Dispatched.ShouldNotContain((Authenticate, NorseSchemes.Anonymous));
	}

	// Ruled fact 4: a bearer of the wrong kind (a machine token, or one carrying both platform audiences) is refused
	// by the user lane the header selected, and the valid identity and anonymous cookies beside it are never read.
	[Theory]
	[InlineData(NorseAudiences.Facade)]
	[InlineData(NorseAudiences.Facade + " " + NorseAudiences.User)]
	[InlineData("")]
	async Task A_wrong_kind_bearer_fails_and_does_not_fall_back_to_the_cookie(string audienceList)
	{
		var audiences = audienceList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var harness = AuthenticationHarness.ForGrpc(audiences.Length is 0 ? BearerTokens.WithoutAudience() : BearerTokens.Machine(audiences))
			.WithValidIdentityCookie("cookie@example.test")
			.WithAnonymousCookie(Guid.NewGuid())
			.WithAuthorization("Bearer token");

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeFalse();
		result.None.ShouldBeFalse();
		result.Failure.ShouldNotBeNull();
		harness.Dispatched.ShouldBe([(Authenticate, NorseSchemes.User), (Authenticate, NorseSchemes.Bearer)]);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("Basic dXNlcjpwYXNz")]
	[InlineData("Bearerish token")]
	async Task No_bearer_header_runs_the_cookie_path_as_today(string? authorization)
	{
		var harness = AuthenticationHarness.ForGrpc(BearerTokens.User())
			.WithValidIdentityCookie("cookie@example.test");
		if (authorization is not null)
			harness.WithAuthorization(authorization);

		var result = await harness.AuthenticateAsync();

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.Identity);
		identity.Name.ShouldBe("cookie@example.test");
		harness.Dispatched.ShouldBe([(Authenticate, IdentityConstants.ApplicationScheme)]);
	}

	[Fact]
	async Task Anonymous_cookie_is_stamped_identity_and_keeps_its_role()
	{
		var harness = AuthenticationHarness.ForGrpc().WithAnonymousCookie(Guid.NewGuid());

		var result = await harness.AuthenticateAsync();

		result.Principal!.Identities.ShouldHaveSingleItem().AuthenticationType.ShouldBe(NorseLanes.Identity);
		result.Principal!.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
	}

	[Fact]
	async Task No_credential_is_no_result()
	{
		var harness = AuthenticationHarness.ForGrpc();

		var result = await harness.AuthenticateAsync();

		result.None.ShouldBeTrue();
		harness.SetCookies.ShouldBeEmpty();
	}

	[Fact]
	async Task A_ticket_identity_typed_as_a_lane_admits_nothing_beyond_identity()
	{
		var harness = AuthenticationHarness.ForGrpc().WithIdentityCookie(AuthenticationHarness.TicketWithALaneTypedIdentity());

		var principal = (await harness.AuthenticateAsync()).Principal!;

		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.Machine)).ShouldBeFalse();
		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.User)).ShouldBeFalse();
		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.Identity)).ShouldBeTrue();
	}

	// The lane is the authentication type of the identity Midgard builds: a token whose identity is typed as a lane,
	// and whose claims name one, still lands on the lane that authenticated it.
	[Fact]
	async Task A_claim_cannot_set_the_lane()
	{
		ClaimsPrincipal token = new(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "user-1"),
				new Claim(BearerTokens.AudienceClaimType, NorseAudiences.User),
				new Claim(NorseLanes.Machine, NorseLanes.Machine),
				new Claim(ClaimTypes.AuthenticationMethod, NorseLanes.Machine)
			],
			NorseLanes.Machine));
		var harness = AuthenticationHarness.ForGrpc(token).WithAuthorization("Bearer token");

		var principal = (await harness.AuthenticateAsync()).Principal!;

		principal.Identities.ShouldHaveSingleItem().AuthenticationType.ShouldBe(NorseLanes.User);
		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.Machine)).ShouldBeFalse();
	}

	// Challenge and forbid authenticate nothing first: the authenticate-before-challenge workaround was the bearer
	// source's concern and lives with whichever realm registers Norse.Bearer (spec §7.1).
	[Theory]
	[InlineData(null, NorseSchemes.Machine)]
	[InlineData("Bearer token", NorseSchemes.User)]
	async Task Challenge_reaches_the_bearer_source_through_its_lane(string? authorization, string lane)
	{
		var harness = AuthenticationHarness.ForGrpc();
		if (authorization is not null)
			harness.WithAuthorization(authorization);

		await harness.ChallengeAsync();

		harness.Dispatched.ShouldBe([("challenge", lane), ("challenge", NorseSchemes.Bearer)]);
		harness.Response.Headers[StubBearerHandler.Marker].ToString().ShouldBe("challenge");
	}

	[Theory]
	[InlineData(null, NorseSchemes.Machine)]
	[InlineData("Bearer token", NorseSchemes.User)]
	async Task Forbid_reaches_the_bearer_source_through_its_lane(string? authorization, string lane)
	{
		var harness = AuthenticationHarness.ForGrpc();
		if (authorization is not null)
			harness.WithAuthorization(authorization);

		await harness.ForbidAsync();

		harness.Dispatched.ShouldBe([("forbid", lane), ("forbid", NorseSchemes.Bearer)]);
		harness.Response.Headers[StubBearerHandler.Marker].ToString().ShouldBe("forbid");
	}
}
