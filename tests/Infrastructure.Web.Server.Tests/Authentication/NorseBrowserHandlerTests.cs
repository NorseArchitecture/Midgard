using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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

	[Fact]
	async Task Forbid_is_a_bare_403_with_no_redirect_and_no_body()
	{
		var harness = AuthenticationHarness.ForBrowser().WithAnonymousCookie(Guid.NewGuid());

		await harness.ForbidAsync();

		harness.Response.StatusCode.ShouldBe(403);
		harness.Response.Headers.Location.ToString().ShouldBeEmpty();
		harness.Response.ContentLength.ShouldBe(0);
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
}
