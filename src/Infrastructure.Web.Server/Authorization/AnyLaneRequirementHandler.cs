using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     Succeeds <see cref="AnyLaneRequirement" /> for every principal, an empty one included: the requirement only
///     opts a policy out of the default lane closure, and the policy's other requirements still decide.
/// </summary>
sealed class AnyLaneRequirementHandler : AuthorizationHandler<AnyLaneRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnyLaneRequirement requirement)
	{
		context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
