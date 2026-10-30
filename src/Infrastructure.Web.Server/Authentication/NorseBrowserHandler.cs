using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The browser lane's composite. Exactly one authentication <i>result</i> contributes — the handler may
///     internally invoke the identity-cookie or anonymous handler, but two results never merge into one
///     principal. Fallback lives here rather than in the lane selector because a policy scheme cannot
///     supply it: <c>ForwardDefaultSelector</c> resolves one scheme name and a failed
///     <c>AuthenticateAsync</c> stays failed. The selector is therefore endpoint-shaped and result-blind,
///     and everything credential-dependent happens inside this type. Whichever result contributes is stamped
///     <c>NorseLanes.Identity</c> on the way out (<see cref="IdentityLaneStamping" />, spec §7.3); the anonymous role
///     still tells the anonymous principal apart.
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
				return identity.StampedIdentityLane();

			// Present but not valid -- expired, revoked, or key-rotated. Delete it with the same options it
			// was written with: a browser silently ignores a delete whose Path/Domain/Secure/SameSite do not
			// match, so rejecting the cookie and removing it are two different acts and only one of them is
			// what we mean. CookieBuilder.Build(Context) is what produces those options in the first place,
			// so calling it here is what makes "same options" true rather than approximately true -- a
			// hand-rolled copy would map SecurePolicy.SameAsRequest to Secure = true and emit a delete a
			// plain-HTTP browser discards.
			Response.Cookies.Delete(identityCookieName, cookieOptions.Get(identityScheme).Cookie.Build(Context));
		}

		return (await Context.AuthenticateAsync(NorseSchemes.Anonymous).ConfigureAwait(false)).StampedIdentityLane();
	}

	// Challenge and forbid are separate operations from authenticate, and the base handler answers both with
	// a bare status. That is right for forbid and wrong for challenge: the browser lane's challenge is the
	// identity cookie's login presentation, and nothing forwards to it unless this override does.
	protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
		Context.ChallengeAsync(IdentityConstants.ApplicationScheme, properties);

	// A bare 403 by default, a door for a human on a page navigation (Ruling 44, amending design §2.4 on 2026-10-27).
	// A forbidden caller is already identified -- anonymous principals included -- so the 403 stands for every gRPC,
	// API, XHR, or non-GET/HEAD forbid: a machine asking "can I?" gets "no", never an HTML page. A human on a page
	// navigation gets a page instead of a blank 403. An anonymous principal carries a GUID but no identity, and its
	// only way forward is to log in, so it gets the same identity-cookie challenge as HandleChallengeAsync and lands
	// on the login path with a returnUrl back to where it was going. An identity principal is forbidden through the
	// identity cookie, which redirects to its access-denied path with the same returnUrl (2026-10-28, Ruling T3-3;
	// AccessDenied is the accepted terminus until the flow is revisited).
	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		if (IsPageNavigation(Context))
			return Context.User.IsInRole(NorseRoles.Anonymous)
				? Context.ChallengeAsync(IdentityConstants.ApplicationScheme, properties)
				: Context.ForbidAsync(IdentityConstants.ApplicationScheme, properties);

		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}

	// A top-level page load a human's browser makes. Endpoint shape is the lane selector's verdict, reused rather
	// than re-derived: anything it routes off the browser lane (gRPC methods, REST facades, probes) is a machine
	// endpoint whatever the request looks like. An [ApiController] endpoint is an API by declaration. Beyond
	// shape: only GET/HEAD navigate; a browser navigation names text/html explicitly in Accept (a bare */* is a
	// tool, and JSON/gRPC-Web clients name their own types); and X-Requested-With, as header or query string, is
	// the same XHR marker the cookie handler's own redirect events honor (CookieAuthenticationEvents.IsAjaxRequest).
	static bool IsPageNavigation(HttpContext context)
	{
		var request = context.Request;
		var endpoint = context.GetEndpoint();

		if (!string.Equals(NorseLaneSelector.Select(endpoint), NorseSchemes.Browser, StringComparison.Ordinal)
			|| endpoint?.Metadata.GetMetadata<IApiBehaviorMetadata>() is not null)
			return false;

		if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
			return false;

		if (string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(request.Query[HeaderNames.XRequestedWith], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
			return false;

		return request.GetTypedHeaders().Accept
			.Any(static accept => accept.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
	}
}
