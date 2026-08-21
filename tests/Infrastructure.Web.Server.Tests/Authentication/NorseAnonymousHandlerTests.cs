using System.Security.Claims;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseAnonymousHandlerTests
{
	[Fact]
	async Task Mints_a_guid_principal_when_no_anonymous_cookie_is_present()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeTrue();
		Guid.TryParse(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier), out _).ShouldBeTrue();
	}

	[Fact]
	async Task Writes_the_anonymous_cookie_when_it_mints()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		await harness.AuthenticateAsync();

		harness.SetCookies.ShouldContain(header => header.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task Returns_the_existing_principal_without_reminting()
	{
		var harness = AuthenticationHarness.ForAnonymous();
		var first = await harness.AuthenticateAsync();
		var id = first.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
		harness.ReplayCookies();

		var second = await harness.AuthenticateAsync();

		second.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(id);
		harness.SetCookies.ShouldBeEmpty();
	}

	[Fact]
	async Task A_tampered_cookie_mints_fresh_rather_than_failing()
	{
		var harness = AuthenticationHarness.ForAnonymous();
		harness.Request.Headers.Cookie = "Norse.Anonymous=not-a-protected-payload";

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeTrue();
		harness.SetCookies.ShouldContain(header => header.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task The_principal_carries_the_anonymous_role_and_is_authenticated()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		var result = await harness.AuthenticateAsync();

		result.Principal!.Identity!.IsAuthenticated.ShouldBeTrue();
		result.Principal.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
	}

	[Fact]
	async Task A_validly_protected_empty_guid_is_treated_as_absent_and_reminted()
	{
		var harness = AuthenticationHarness.ForAnonymous().WithProtectedAnonymousPayload(Guid.Empty);

		var result = await harness.AuthenticateAsync();

		// Never hands the pipeline a principal the mediator seam is guaranteed to reject: Seed refuses
		// Guid.Empty, so authenticating one here would only defer the failure to a worse place.
		Guid.Parse(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!).ShouldNotBe(Guid.Empty);
		harness.SetCookies.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}
}
