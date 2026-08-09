using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Backend.Serialization;

/// <summary>Composition-root wiring for the serialization seam.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Registers the JSON-backed <see cref="ISerializerProvider" /> as a singleton.</summary>
	public static IServiceCollection AddNorseSerialization(this IServiceCollection services) =>
		services.AddSingleton<ISerializerProvider, SerializerProvider>();
}
