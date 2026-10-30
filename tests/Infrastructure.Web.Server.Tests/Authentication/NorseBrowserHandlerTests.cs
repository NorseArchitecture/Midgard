using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseBrowserHandlerTests
{
	[Fact]
	async Task A_valid_identity_cookie_wins_and_mints_nothing()
	{
		var harness = AuthenticationHarness.ForBrowser().WithValidIdentityCookie("user@example.test");

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.Name).ShouldBe("user@example.test");
		harness.SetCookies.ShouldNotContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	// Spec §7.3: the identity cookie's identity reaches policy evaluation stamped Identity, its claims and its name and
	// role claim types unchanged.
	[Fact]
	async Task Identity_cookie_is_stamped_identity()
	{
		var harness = AuthenticationHarness.ForBrowser().WithValidIdentityCookie("user@example.test");

		var result = await harness.AuthenticateAsync();

		var identity = result.Principal!.Identities.ShouldHaveSingleItem();
		identity.AuthenticationType.ShouldBe(NorseLanes.Identity);
		identity.NameClaimType.ShouldBe(ClaimTypes.Name);
		identity.RoleClaimType.ShouldBe(ClaimTypes.Role);
		identity.Name.ShouldBe("user@example.test");
		result.Ticket!.AuthenticationScheme.ShouldBe(IdentityConstants.ApplicationScheme);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	async Task Anonymous_cookie_is_stamped_identity_and_keeps_its_role(bool hasAnonymousCookie)
	{
		var harness = AuthenticationHarness.ForBrowser();
		if (hasAnonymousCookie)
			harness.WithAnonymousCookie(Guid.NewGuid());

		var result = await harness.AuthenticateAsync();

		result.Principal!.Identities.ShouldHaveSingleItem().AuthenticationType.ShouldBe(NorseLanes.Identity);
		result.Principal!.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
	}

	// The rebuilt principal holds the ticket's primary identity alone, stamped: an identity the ticket itself typed as
	// a lane never survives into it, so it admits the Identity lane and nothing beyond.
	[Fact]
	async Task A_ticket_identity_typed_as_a_lane_admits_nothing_beyond_identity()
	{
		var harness = AuthenticationHarness.ForBrowser().WithIdentityCookie(AuthenticationHarness.TicketWithALaneTypedIdentity());

		var principal = (await harness.AuthenticateAsync()).Principal!;

		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.Machine)).ShouldBeFalse();
		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.User)).ShouldBeFalse();
		(await AuthenticationHarness.AdmitsAsync(principal, NorseLanes.Identity)).ShouldBeTrue();
		principal.Identities.ShouldHaveSingleItem().Name.ShouldBe("user@example.test");
	}

	[Fact]
	async Task An_expired_identity_cookie_is_deleted_and_a_fresh_anonymous_is_minted()
	{
		var harness = AuthenticationHarness.ForBrowser().WithExpiredIdentityCookie();

		var result = await harness.AuthenticateAsync();

		result.Principal!.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
		harness.DeletedCookies.ShouldContain(harness.IdentityCookieName);
		harness.SetCookies.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task The_identity_cookie_delete_matches_the_attributes_it_was_written_with()
	{
		var harness = AuthenticationHarness.ForBrowser().WithExpiredIdentityCookie();

		await harness.AuthenticateAsync();

		var delete = harness.SetCookies.Single(h => h.StartsWith(harness.IdentityCookieName, StringComparison.Ordinal));
		delete.ShouldContain("path=/", Case.Insensitive);
		delete.ShouldContain("secure", Case.Insensitive);
		delete.ShouldContain("samesite=lax", Case.Insensitive);
	}

	[Fact]
	async Task Identity_outranks_anonymous_when_both_cookies_are_present()
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithValidIdentityCookie("user@example.test")
			.WithAnonymousCookie(Guid.NewGuid());

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.Name).ShouldBe("user@example.test");
		result.Principal!.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeFalse();
	}

	[Fact]
	async Task A_forged_anonymous_cookie_cannot_add_claims_to_an_authenticated_principal()
	{
		var harness = AuthenticationHarness.ForBrowser().WithValidIdentityCookie("user@example.test");
		harness.Request.Headers.Append("Cookie", "Norse.Anonymous=forged");

		var result = await harness.AuthenticateAsync();

		result.Principal!.Claims.ShouldNotContain(c =>
			c.Type == ClaimTypes.Role && c.Value == NorseAnonymousOptions.AnonymousRole);
	}

	[Fact]
	async Task A_valid_anonymous_cookie_alone_is_returned_without_reminting()
	{
		var id = Guid.NewGuid();
		var harness = AuthenticationHarness.ForBrowser().WithAnonymousCookie(id);

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(id.ToString("D"));
	}

	[Fact]
	async Task A_valid_anonymous_cookie_is_refreshed_rather_than_reminted()
	{
		var id = Guid.NewGuid();
		var harness = AuthenticationHarness.ForBrowser().WithAnonymousCookie(id);

		await harness.AuthenticateAsync();

		harness.SetCookies.ShouldContain(header => header.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task Challenge_forwards_to_the_identity_cookies_login_presentation()
	{
		var harness = AuthenticationHarness.ForBrowser();

		await harness.ChallengeAsync();

		harness.Response.StatusCode.ShouldBe(302);
		harness.Response.Headers.Location.ToString().ShouldContain("/Account/Login");
	}

	[Theory]
	[InlineData("GET", true, BrowserNavigationAccept, null)]
	[InlineData("GET", false, BrowserNavigationAccept, null)]
	[InlineData("HEAD", true, BrowserNavigationAccept, null)]
	[InlineData("GET", true, "text/html; blazor-enhanced-nav=on", "no-cors")]
	async Task An_anonymous_page_navigation_is_challenged_to_login_with_a_return_url(string method, bool hasAnonymousCookie,
		string accept, string? secFetchMode)
	{
		var harness = AuthenticationHarness.ForBrowser().WithRequest(method, "/Account/Manage", accept);
		if (hasAnonymousCookie)
			harness.WithAnonymousCookie(Guid.NewGuid());
		if (secFetchMode is not null)
			harness.Request.Headers["Sec-Fetch-Mode"] = secFetchMode;

		await harness.ForbidAfterAuthenticatingAsync();

		harness.Response.StatusCode.ShouldBe(302);
		Uri location = new(harness.Response.Headers.Location.ToString());
		location.AbsolutePath.ShouldBe("/Account/Login");
		QueryHelpers.ParseQuery(location.Query)["ReturnUrl"].ToString().ShouldBe("/Account/Manage");
	}

	[Theory]
	[InlineData("POST", BrowserNavigationAccept)]
	[InlineData("PUT", BrowserNavigationAccept)]
	[InlineData("DELETE", BrowserNavigationAccept)]
	[InlineData("GET", "application/json")]
	[InlineData("GET", "*/*")]
	[InlineData("GET", "application/grpc-web-text")]
	async Task Forbid_of_an_anonymous_non_navigation_is_a_bare_403_with_no_redirect_and_no_body(string method, string accept)
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithRequest(method, "/Account/Manage", accept)
			.WithAnonymousCookie(Guid.NewGuid());

		await harness.ForbidAfterAuthenticatingAsync();

		ShouldBeABare403(harness);
	}

	[Theory]
	[InlineData("grpc")]
	[InlineData("facade")]
	[InlineData("probe")]
	[InlineData("api")]
	async Task Forbid_of_an_anonymous_principal_on_a_machine_shaped_endpoint_is_a_bare_403_even_for_an_html_get(string shape)
	{
		var endpoint = shape switch
		{
			"grpc" => EndpointFactory.Grpc(),
			"facade" => EndpointFactory.Facade(),
			"probe" => EndpointFactory.Probe(),
			_ => new Endpoint(null, new EndpointMetadataCollection(new ApiControllerAttribute()), displayName: "api")
		};
		var harness = AuthenticationHarness.ForBrowser()
			.WithRequest("GET", "/Account/Manage", BrowserNavigationAccept)
			.WithEndpoint(endpoint)
			.WithAnonymousCookie(Guid.NewGuid());

		await harness.ForbidAfterAuthenticatingAsync();

		ShouldBeABare403(harness);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	async Task Forbid_of_an_anonymous_xhr_is_a_bare_403_even_when_it_accepts_html(bool asHeader)
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithRequest("GET", "/Account/Manage", BrowserNavigationAccept)
			.WithAnonymousCookie(Guid.NewGuid());
		if (asHeader)
			harness.Request.Headers.XRequestedWith = "XMLHttpRequest";
		else
			harness.Request.QueryString = new("?X-Requested-With=XMLHttpRequest");

		await harness.ForbidAfterAuthenticatingAsync();

		ShouldBeABare403(harness);
	}

	[Fact]
	async Task Forbid_of_an_identity_principal_on_a_page_navigation_redirects_to_access_denied()
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithRequest("GET", "/Account/Manage", BrowserNavigationAccept)
			.WithValidIdentityCookie("user@example.test");
		harness.Request.QueryString = new("?tab=passkeys");

		await harness.ForbidAfterAuthenticatingAsync();

		harness.Response.StatusCode.ShouldBe(302);
		Uri location = new(harness.Response.Headers.Location.ToString());
		location.AbsolutePath.ShouldBe("/Account/AccessDenied");
		QueryHelpers.ParseQuery(location.Query)["ReturnUrl"].ToString().ShouldBe("/Account/Manage?tab=passkeys");
	}

	// A guard: every identity-principal forbid was already a bare 403; this pins that the access-denied redirect stays
	// confined to page navigations.
	[Theory]
	[InlineData("GET", "application/json", false, false)]
	[InlineData("GET", BrowserNavigationAccept, true, false)]
	[InlineData("GET", BrowserNavigationAccept, false, true)]
	[InlineData("POST", BrowserNavigationAccept, false, false)]
	async Task Forbid_of_an_identity_principal_off_a_page_navigation_is_a_bare_403(string method, string accept,
		bool xhrHeader, bool xhrQuery)
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithRequest(method, "/Account/Manage", accept)
			.WithValidIdentityCookie("user@example.test");
		if (xhrHeader)
			harness.Request.Headers.XRequestedWith = "XMLHttpRequest";
		if (xhrQuery)
			harness.Request.QueryString = new("?X-Requested-With=XMLHttpRequest");

		await harness.ForbidAfterAuthenticatingAsync();

		ShouldBeABare403(harness);
	}

	[Fact]
	async Task Deletion_of_an_insecure_request_cookie_does_not_force_the_secure_flag()
	{
		var harness = AuthenticationHarness.ForBrowser(https: false)
			.WithExpiredIdentityCookie(securePolicy: CookieSecurePolicy.SameAsRequest);

		await harness.AuthenticateAsync();

		var delete = harness.SetCookies.Single(h => h.StartsWith(harness.IdentityCookieName, StringComparison.Ordinal));
		delete.ShouldNotContain("secure", Case.Insensitive);
	}

	/// <summary>What a browser's top-level navigation sends: an explicit <c>text/html</c> among the rest.</summary>
	const string BrowserNavigationAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

	static void ShouldBeABare403(AuthenticationHarness harness)
	{
		harness.Response.StatusCode.ShouldBe(403);
		harness.Response.Headers.Location.ToString().ShouldBeEmpty();
		harness.Response.ContentLength.ShouldBe(0);
	}
}
