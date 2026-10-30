using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Client.Authorization;

/// <summary>
///     The client's <see cref="LaneRequirement" /> handler: the browser only ever holds the cookie identity, so any
///     authenticated principal is the <see cref="NorseLanes.Identity" /> lane, whatever
///     <see cref="ClaimsIdentity.AuthenticationType" /> its state provider gave it, and no other lane.
/// </summary>
/// <remarks>
///     "Authenticated" is <see cref="ClaimsIdentity.IsAuthenticated" />, as on the server: an empty principal, or
///     one whose identities carry no authentication type, matches no lane.
/// </remarks>
sealed class ClientLaneRequirementHandler : AuthorizationHandler<LaneRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LaneRequirement requirement)
	{
		if (requirement.Lanes.Contains(NorseLanes.Identity, StringComparer.Ordinal)
			&& context.User.Identities.Any(identity => identity.IsAuthenticated))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
