using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
///     The probe policy for a host with no identity stack. An application host gets <see cref="NorsePolicies.Probe" />
///     from the generated <c>AddNorsePolicies()</c> and its handlers from <c>AddNorseAuthentication()</c>, which
///     refuses to start without a bearer scheme. An asset host has neither, so the asset root registers the policy
///     from Asgard's own declaration and satisfies its lane opt-out itself. The policy names no scheme and always
///     succeeds, so an asset host needs no authentication; a <c>WebApplication</c> adds the authorization middleware
///     itself once these services are registered.
/// </summary>
static class AssetHostProbeAuthorization
{
	internal static void Add(IServiceCollection services)
	{
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, AnyLaneRequirementHandler>());
		services.AddAuthorizationBuilder().AddPolicy(NorsePolicies.Probe, NorsePlatformPolicies.Probe);
	}

	/// <summary>Succeeds <see cref="AnyLaneRequirement" />: it only opts the probe out of the default lane closure.</summary>
	sealed class AnyLaneRequirementHandler : AuthorizationHandler<AnyLaneRequirement>
	{
		protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnyLaneRequirement requirement)
		{
			context.Succeed(requirement);
			return Task.CompletedTask;
		}
	}
}
