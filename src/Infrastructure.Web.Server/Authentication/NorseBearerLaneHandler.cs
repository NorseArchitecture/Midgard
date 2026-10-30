using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     A bearer lane (spec §7.1, §7.3): one type, registered twice by
///     <see cref="AuthenticationBuilderExtensions.AddNorseAuthentication" /> — <see cref="NorseSchemes.Machine" /> and
///     <see cref="NorseSchemes.User" /> — each parameterized by its lane. It authenticates
///     <see cref="NorseSchemes.Bearer" /> by name and rebuilds the authenticated identity with the lane as its
///     <see cref="ClaimsIdentity.AuthenticationType" />, so the bearer result passes through Midgard's interception
///     point and nothing the token carries can set the lane. Challenge and forbid are the bearer source's own.
/// </summary>
/// <remarks>
///     A bearer lane means the token's kind, not only the route's: before stamping, the
///     validated identity must carry the lane's <see cref="NorseBearerLaneOptions.Audience" /> — <c>Norse.Machine</c>
///     <see cref="NorseAudiences.Facade" />, <c>Norse.User</c> <see cref="NorseAudiences.User" /> — among its
///     <c>aud</c> claims, compared ordinal, and none of the platform's other audiences. A token carrying both platform
///     audiences is ambiguous and fails in either lane. A missing, wrong or ambiguous audience is
///     <see cref="AuthenticateResult.Fail(string)" />: never <see cref="AuthenticateResult.NoResult" />, never a fallback
///     to a cookie or another lane. An <c>aud</c> outside the platform's two names no token kind and neither admits nor
///     refuses. The policies' own audience pins stay, as defense in depth.
///     <para>
///     The authenticated identity is the bearer principal's primary <see cref="ClaimsPrincipal.Identity" />. The lane
///     principal holds exactly that one identity, rebuilt with every claim copied unchanged (its <c>aud</c> claims
///     included) and its name and role claim types kept; any other identity on the bearer result is dropped, so two
///     identities never mix in one lane principal. A bearer result that succeeded without an authenticated primary
///     identity fails rather than passing through unstamped.
///     </para>
/// </remarks>
sealed class NorseBearerLaneHandler(
	IOptionsMonitor<NorseBearerLaneOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<NorseBearerLaneOptions>(options, logger, encoder)
{
	// The registered JWT audience claim name (RFC 7519 §4.1.3); a multi-valued aud arrives as one claim per value.
	const string AudienceClaimType = "aud";

	// The audiences that name a token kind: a lane admits its own and refuses every other.
	static readonly string[] _platformAudiences = [NorseAudiences.Facade, NorseAudiences.User];

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var bearer = await Context.AuthenticateAsync(NorseSchemes.Bearer).ConfigureAwait(false);
		if (!bearer.Succeeded)
			return bearer;

		if (bearer.Principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
			return AuthenticateResult.Fail($"The {NorseSchemes.Bearer} result carries no authenticated identity.");

		HashSet<string> audiences = [.. identity.FindAll(AudienceClaimType).Select(static claim => claim.Value)];
		if (!audiences.Contains(Options.Audience))
			return AuthenticateResult.Fail($"The {Scheme.Name} lane requires a token with the {Options.Audience} audience.");
		if (_platformAudiences.Any(audience => !string.Equals(audience, Options.Audience, StringComparison.Ordinal)
				&& audiences.Contains(audience)))
			return AuthenticateResult.Fail($"The {Scheme.Name} lane refuses a token carrying another platform audience.");

		ClaimsIdentity stamped = new(identity.Claims, Options.Lane, identity.NameClaimType, identity.RoleClaimType);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(stamped), bearer.Properties, Scheme.Name));
	}

	protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
		Context.ChallengeAsync(NorseSchemes.Bearer, properties);

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
		Context.ForbidAsync(NorseSchemes.Bearer, properties);
}
