using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>Composition-root wiring for <see cref="IDeferredSignIn"/>.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Registers <see cref="IDeferredSignIn"/> and the <see cref="IMemoryCache"/> it depends on.</summary>
	public static IServiceCollection AddDeferredSignIn(this IServiceCollection services)
	{
		services.AddMemoryCache();
		services.AddSingleton<IDeferredSignIn, MemoryCacheDeferredSignIn>();
		return services;
	}
}
