using System.Collections.Concurrent;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Backend.Serialization;

/// <summary>Lazy-mints and caches one <see cref="SystemTextJsonSerializer" /> per strategy.</summary>
sealed class SerializerProvider : ISerializerProvider
{
	readonly ConcurrentDictionary<NamingStrategy, ISerializer> _serializers = new();

	/// <inheritdoc />
	public ISerializer this[NamingStrategy key] =>
		_serializers.GetOrAdd(key, static k => new SystemTextJsonSerializer(k));
}
