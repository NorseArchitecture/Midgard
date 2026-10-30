using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Norse.Infrastructure.Web.Client.Authorization;

/// <summary>Composition-root wiring for client-side (WASM) authorization.</summary>
public static class ClientAuthorizationExtensions
{
	/// <param name="services">The service collection to configure.</param>
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers <c>AddAuthorizationCore()</c> and the client lane and audience requirement handlers, which
		///     treat the browser's principal as the identity lane, so the client mirror of the platform's policies
		///     evaluates as the server's does for the cookie identity. The companion of the generated
		///     <c>AddNorsePolicies()</c> and <c>AddNorseGrpcClients()</c>; a WASM host calls it in place of
		///     <c>AddAuthorizationCore()</c>.
		/// </summary>
		/// <returns>The service collection, for chaining.</returns>
		public IServiceCollection AddNorseClientAuthorization()
		{
			services.TryAddEnumerable([
				ServiceDescriptor.Singleton<IAuthorizationHandler, ClientLaneRequirementHandler>(),
				ServiceDescriptor.Singleton<IAuthorizationHandler, ClientAnyLaneRequirementHandler>(),
				ServiceDescriptor.Singleton<IAuthorizationHandler, ClientAudienceRequirementHandler>()
			]);
			return services.AddAuthorizationCore();
		}
	}
}
