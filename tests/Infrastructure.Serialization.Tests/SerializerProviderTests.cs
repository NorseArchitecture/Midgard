using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Serialization.Tests;

public sealed class SerializerProviderTests
{
	[Fact]
	void Provider_caches_one_serializer_per_strategy()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		var provider = services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
		provider[NamingStrategy.CamelCase].ShouldBeSameAs(provider[NamingStrategy.CamelCase]);
		provider[NamingStrategy.SnakeCase].ShouldNotBeSameAs(provider[NamingStrategy.CamelCase]);
	}

	[Fact]
	void Unspecified_is_the_smuggled_sentinel_and_throws()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		var provider = services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
		Should.Throw<ArgumentOutOfRangeException>(() => provider[NamingStrategy.Unspecified]);
	}
}
