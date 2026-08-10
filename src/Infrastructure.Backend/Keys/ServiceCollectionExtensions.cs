using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Keys;

namespace Norse.Infrastructure.Backend.Keys;

/// <summary>Composition-root wiring for the dev-grade key seam.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Registers a single <see cref="DevelopmentSubjectKeyStore" /> instance under both
	///     <see cref="ISubjectKeyStore" /> and <see cref="ILookupKeyRing" />, rooted at
	///     <paramref name="rootPath" />. Dev-grade only — never a production path.
	/// </summary>
	public static IServiceCollection AddNorseDevelopmentKeys(this IServiceCollection services, string rootPath)
	{
		DevelopmentSubjectKeyStore store = new(rootPath);
		return services
			.AddSingleton<ISubjectKeyStore>(store)
			.AddSingleton<ILookupKeyRing>(store);
	}
}
