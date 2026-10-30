using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Client.Authorization;

/// <summary>
///     The client's <see cref="AnyLaneRequirement" /> handler: succeeds for every principal, an empty one included,
///     exactly as the server's does.
/// </summary>
sealed class ClientAnyLaneRequirementHandler : AuthorizationHandler<AnyLaneRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnyLaneRequirement requirement)
	{
		context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
