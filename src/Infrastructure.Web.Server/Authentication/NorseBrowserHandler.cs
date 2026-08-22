using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The browser lane's composite. Exactly one authentication <i>result</i> contributes — the handler may
///     internally invoke the identity-cookie or anonymous handler, but two results never merge into one
///     principal. Fallback lives here rather than in the lane selector because a policy scheme cannot
///     supply it: <c>ForwardDefaultSelector</c> resolves one scheme name and a failed
///     <c>AuthenticateAsync</c> stays failed. The selector is therefore endpoint-shaped and result-blind,
///     and everything credential-dependent happens inside this type.
/// </summary>
/// <remarks>
///     The design's constructor also names <c>IOptionsMonitor&lt;NorseAnonymousOptions&gt;</c> and
///     <c>TimeProvider</c>, but neither is read anywhere below: the mint/read decision for the anonymous
///     principal is delegated whole to <see cref="NorseSchemes.Anonymous" />'s own handler
///     (<see cref="NorseAnonymousHandler" />), which already owns that clock and those options. Declaring
///     unread primary-constructor parameters is CS9113 under this repo's warnings-as-errors build, so they
///     are omitted here rather than kept as dead parameters.
/// </remarks>
sealed class NorseBrowserHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
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

			// Present but not valid -- expired, revoked, or key-rotated. Delete it with the same options it
			// was written with: a browser silently ignores a delete whose Path/Domain/Secure/SameSite do not
			// match, so rejecting the cookie and removing it are two different acts and only one of them is
			// what we mean. CookieBuilder.Build(Context) is what produces those options in the first place,
			// so calling it here is what makes "same options" true rather than approximately true -- a
			// hand-rolled copy would map SecurePolicy.SameAsRequest to Secure = true and emit a delete a
			// plain-HTTP browser discards.
			Response.Cookies.Delete(identityCookieName, cookieOptions.Get(identityScheme).Cookie.Build(Context));
		}

		return await Context.AuthenticateAsync(NorseSchemes.Anonymous).ConfigureAwait(false);
	}

	// Challenge and forbid are separate operations from authenticate, and the base handler answers both with
	// a bare status. That is right for forbid and wrong for challenge: the browser lane's challenge is the
	// identity cookie's login presentation, and nothing forwards to it unless this override does.
	protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
		Context.ChallengeAsync(IdentityConstants.ApplicationScheme, properties);

	// Never a redirect. A forbidden caller is already identified -- anonymous principals included, which is
	// the whole point of design §2.4 -- so sending them to a login page would answer "who are you?" to
	// someone who has already told us. Bare 403, no body.
	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}
}
