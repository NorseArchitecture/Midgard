using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     Succeeds <see cref="LaneRequirement" /> when an authenticated identity on the principal carries one of the
///     admitted lanes as its <see cref="ClaimsIdentity.AuthenticationType" />, the lane
///     Midgard's authentication handlers stamp. Nothing a token or cookie carries can set it.
/// </summary>
/// <remarks>
///     "Authenticated" is <see cref="ClaimsIdentity.IsAuthenticated" />: a non-empty
///     authentication type. It is checked explicitly so an unauthenticated identity never matches, even against a
///     requirement constructed with a blank lane, and an empty principal always fails: policies such as
///     <see cref="NorsePolicies.Machine" /> rely on that instead of <c>RequireAuthenticatedUser</c>.
/// </remarks>
sealed class LaneRequirementHandler : AuthorizationHandler<LaneRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LaneRequirement requirement)
	{
		if (context.User.Identities.Any(identity =>
			identity.IsAuthenticated && requirement.Lanes.Contains(identity.AuthenticationType, StringComparer.Ordinal)))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
