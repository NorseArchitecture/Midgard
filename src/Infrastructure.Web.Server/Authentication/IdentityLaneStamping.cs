using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The cookie paths' interception point (spec §7.3): <see cref="NorseBrowserHandler" /> and
///     <see cref="NorseGrpcHandler" /> pass every cookie result through here, the identity cookie and the anonymous
///     cookie alike, so the identity they hand to policy evaluation is one Midgard built and stamped
///     <see cref="NorseLanes.Identity" />.
/// </summary>
/// <remarks>
///     The stamped principal holds exactly one identity: the ticket's primary <see cref="ClaimsPrincipal.Identity" />,
///     rebuilt with every claim copied unchanged and its name and role claim types kept. Any other identity on the
///     ticket is dropped, so an identity the ticket itself typed as a lane never survives unstamped, and two identities
///     never mix in one principal — the same shape <see cref="NorseBearerLaneHandler" /> builds for the bearer lanes.
///     The ticket's properties and authentication scheme are kept, so everything but the identity reads as before. A
///     result that did not succeed passes through untouched; one that succeeded without an authenticated primary
///     identity fails rather than passing through unstamped.
/// </remarks>
static class IdentityLaneStamping
{
	/// <param name="result">A cookie handler's result.</param>
	extension(AuthenticateResult result)
	{
		/// <summary>The result with its identity rebuilt on the <see cref="NorseLanes.Identity" /> lane.</summary>
		public AuthenticateResult StampedIdentityLane()
		{
			if (!result.Succeeded)
				return result;

			if (result.Principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
				return AuthenticateResult.Fail($"The {result.Ticket.AuthenticationScheme} result carries no authenticated identity.");

			ClaimsIdentity stamped = new(identity.Claims, NorseLanes.Identity, identity.NameClaimType, identity.RoleClaimType);
			return AuthenticateResult.Success(
				new AuthenticationTicket(new ClaimsPrincipal(stamped), result.Properties, result.Ticket.AuthenticationScheme));
		}
	}
}
