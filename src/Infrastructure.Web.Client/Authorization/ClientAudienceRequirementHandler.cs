using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Client.Authorization;

/// <summary>
///     The client's <see cref="AudienceRequirement" /> handler, over the same identity-lane view of the browser's
///     principal as <see cref="ClientLaneRequirementHandler" />: a requirement scoped to any lane but
///     <see cref="NorseLanes.Identity" /> leaves the principal untouched, and any other requirement needs an
///     authenticated identity carrying an <c>aud</c> claim equal to the required audience.
/// </summary>
sealed class ClientAudienceRequirementHandler : AuthorizationHandler<AudienceRequirement>
{
	// The registered JWT audience claim name (RFC 7519 §4.1.3), the same claim the server's handler reads.
	const string AudienceClaimType = "aud";

	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AudienceRequirement requirement)
	{
		if ((requirement.Lane is not null && !string.Equals(requirement.Lane, NorseLanes.Identity, StringComparison.Ordinal))
			|| context.User.Identities.Any(identity =>
				identity.IsAuthenticated && identity.HasClaim(AudienceClaimType, requirement.Audience)))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
