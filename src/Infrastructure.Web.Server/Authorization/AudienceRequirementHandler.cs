using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     Succeeds <see cref="AudienceRequirement" /> when an authenticated identity in the requirement's lane carries
///     an <c>aud</c> claim equal to the required audience; one identity may carry several, and any match counts.
/// </summary>
/// <remarks>
///     A lane-scoped requirement leaves every other lane untouched: a principal with no authenticated identity in
///     that lane, an empty one included, passes it, and the policy's lane requirement decides whether it is
///     admitted at all. A requirement with no lane applies to every authenticated identity.
/// </remarks>
sealed class AudienceRequirementHandler : AuthorizationHandler<AudienceRequirement>
{
	// The registered JWT audience claim name (RFC 7519 §4.1.3), carried unchanged onto the bearer identity.
	const string AudienceClaimType = "aud";

	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AudienceRequirement requirement)
	{
		List<ClaimsIdentity> inScope =
		[
			.. context.User.Identities
				.Where(identity => identity.IsAuthenticated
					&& (requirement.Lane is null
						|| string.Equals(identity.AuthenticationType, requirement.Lane, StringComparison.Ordinal)))
		];
		if ((requirement.Lane is not null && inScope.Count is 0)
			|| inScope.Any(identity => identity.HasClaim(AudienceClaimType, requirement.Audience)))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
