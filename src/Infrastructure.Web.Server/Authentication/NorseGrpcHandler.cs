using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The gRPC lane's composite (authn-uniformity amendment, ../Glitnir/docs/Platform/plans/2026-08-21-principal-at-the-door.md):
///     identity cookie, then a previously-established anonymous cookie — the same two-step
///     <see cref="NorseBrowserHandler" /> already runs, so a gRPC-Web caller sharing the browser's cookie
///     jar (Mímir's <c>CountryLookup</c> is the first consumer) authenticates exactly as the page that
///     hosts it does. The one asymmetry is deliberate: this lane never mints. A gRPC/gRPC-Web caller
///     presenting neither cookie gets <see cref="AuthenticateResult.NoResult" /> and falls through to
///     <see cref="NorseSchemes.Machine" />'s bare 401/403 — there is no login page to redirect a client
///     that cannot follow one to, and a credential-less machine caller must not walk away holding a free
///     identity (<c>LaneWireupTests.A_credentialless_grpc_call_mints_nothing_and_writes_no_cookie</c>).
/// </summary>
sealed class NorseGrpcHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
	IOptionsMonitor<NorseAnonymousOptions> anonymousOptions,
	IDataProtectionProvider protection)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var identityScheme = IdentityConstants.ApplicationScheme;
		var identityCookieName = cookieOptions.Get(identityScheme).Cookie.Name ?? identityScheme;

		if (Request.Cookies.ContainsKey(identityCookieName))
		{
			var identity = await Context.AuthenticateAsync(identityScheme).ConfigureAwait(false);
			if (identity.Succeeded)
				return identity;

			// Same delete-and-fall-through as NorseBrowserHandler's identical branch: present but invalid
			// (expired, revoked, key-rotated) is not "absent", so the stale cookie is removed with the
			// options it was written with before this request tries the anonymous cookie instead.
			Response.Cookies.Delete(identityCookieName, cookieOptions.Get(identityScheme).Cookie.Build(Context));
		}

		// Read-only, never blind: NorseAnonymousHandler mints unconditionally once invoked (design §2.3),
		// and treats a payload it cannot decode as absence -- indistinguishable, by design, from a cookie
		// that was never there (the browser lane's own tolerance for a tampered/key-rotated cookie). Gating
		// on mere presence would let a garbage Norse.Anonymous value walk a credential-less caller straight
		// through that mint path (codex review, PR #78), so this lane validates a genuine decode itself
		// first -- the same protector, the same payload -- and only then calls in, at which point the
		// second decode inside NorseAnonymousHandler is guaranteed to succeed identically rather than fall
		// through to its own mint branch.
		if (Request.Cookies.TryGetValue(anonymousOptions.Get(NorseSchemes.Anonymous).CookieName, out var anonymousPayload) &&
			NorseAnonymousCookieReader.TryUnprotect(
				protection.CreateProtector(NorseAnonymousOptions.ProtectionPurpose), anonymousPayload, out _))
			return await Context.AuthenticateAsync(NorseSchemes.Anonymous).ConfigureAwait(false);

		return AuthenticateResult.NoResult();
	}

	// Both operations go bare through the machine lane's own handler, same as this scheme's previous
	// AddPolicyScheme forwarding did -- a gRPC client cannot follow the identity cookie's login redirect
	// and must not be sent one.
	protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
		Context.ChallengeAsync(NorseSchemes.Machine, properties);

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
		Context.ForbidAsync(NorseSchemes.Machine, properties);
}
