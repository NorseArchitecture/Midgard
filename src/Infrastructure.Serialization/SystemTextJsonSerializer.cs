using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Abstractions.Backend.Serialization;

namespace Norse.Infrastructure.Serialization;

/// <summary>
/// The JSON arm of the seam: one instance per <see cref="NamingStrategy"/>, four cached
/// <see cref="JsonSerializerOptions"/> variants (nulls × pretty) — options are never minted per
/// call. Property names follow the strategy; dictionary keys are data and pass through unrewritten.
/// </summary>
sealed class SystemTextJsonSerializer : ISerializer
{
	/// <summary>
	/// A known, accepted AOT/trim gap — not a false positive. <see cref="ISerializer"/> is generic
	/// over any caller-supplied payload type by design, so every call below reflects over an
	/// unbounded <c>T</c>: IL2026/IL3050 are firing correctly, and this suppression records
	/// acceptance of that gap rather than disputing the warning. This is deliberately the opposite
	/// posture of the bounded-surface precedents in <c>Infrastructure.Web.Server/Json</c>
	/// (<c>ResultJsonConverter</c> and friends), which suppress because their reflection closes over
	/// a doctrinally finite, statically-known type set — the warning is wrong there. Here it is
	/// right; binding a <see cref="JsonSerializerContext"/> per <c>T</c> would recreate exactly the
	/// per-consumer System.Text.Json coupling the seam exists to hide behind Asgard's contract, so
	/// resolving this gap needs its own source-generated-<c>JsonTypeInfo</c> design pass, tracked in
	/// scope by the seam spec — not a quiet fix here. The interface declares no
	/// <c>RequiresUnreferencedCode</c>/<c>RequiresDynamicCode</c> annotation, so the requirement is
	/// suppressed at each implementation rather than propagated.
	/// </summary>
	const string AcceptedAotGapUnboundedGenericT = "Known, accepted AOT/trim gap, not a false positive — ISerializer is generic over an unbounded caller-supplied T, so IL2026/IL3050 fire correctly here (contrast the bounded-surface precedents in Infrastructure.Web.Server/Json, which suppress because the warning is wrong for their finite type set). Resolution is a future JsonSerializerContext (source-generated JsonTypeInfo) design pass, tracked in the seam spec's scope; see the class-level remarks.";

	readonly JsonSerializerOptions
		_compact,
		_compactWithNulls,
		_pretty,
		_prettyWithNulls;

	/// <summary>Builds the four cached option variants for <paramref name="strategy"/>.</summary>
	public SystemTextJsonSerializer(NamingStrategy strategy)
	{
		var policy = strategy switch
		{
			NamingStrategy.CamelCase => JsonNamingPolicy.CamelCase,
			NamingStrategy.PascalCase => null,
			NamingStrategy.SnakeCase => JsonNamingPolicy.SnakeCaseLower,
			NamingStrategy.KebabCase => JsonNamingPolicy.KebabCaseLower,
			_ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "A serializer always names its convention.")
		};
		_compact = Build(policy, serializeNulls: false, prettyPrint: false);
		_compactWithNulls = Build(policy, serializeNulls: true, prettyPrint: false);
		_pretty = Build(policy, serializeNulls: false, prettyPrint: true);
		_prettyWithNulls = Build(policy, serializeNulls: true, prettyPrint: true);
	}

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public T? Deserialize<T>(byte[] bytes) =>
		JsonSerializer.Deserialize<T>(bytes, _compact);

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public T? Deserialize<T>(Stream stream) =>
		JsonSerializer.Deserialize<T>(stream, _compact);

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public T? Deserialize<T>(string payload) =>
		JsonSerializer.Deserialize<T>(payload, _compact);

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default) =>
		JsonSerializer.DeserializeAsync<T>(stream, _compact, cancellationToken);

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public void Serialize<T>(Stream stream, T obj, bool serializeNulls = false) =>
		JsonSerializer.Serialize(stream, obj, Options(serializeNulls, prettyPrint: false));

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public string Serialize<T>(T obj, bool serializeNulls = false, bool prettyPrint = false) =>
		JsonSerializer.Serialize(obj, Options(serializeNulls, prettyPrint));

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public Task SerializeAsync<T>(Stream stream, T obj, bool serializeNulls = false, CancellationToken cancellationToken = default) =>
		JsonSerializer.SerializeAsync(stream, obj, Options(serializeNulls, prettyPrint: false), cancellationToken);

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AcceptedAotGapUnboundedGenericT)]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = AcceptedAotGapUnboundedGenericT)]
	public byte[] SerializeToUtf8Bytes<T>(T obj, bool serializeNulls = false) =>
		JsonSerializer.SerializeToUtf8Bytes(obj, Options(serializeNulls, prettyPrint: false));

	JsonSerializerOptions Options(bool serializeNulls, bool prettyPrint) =>
		serializeNulls ?
			prettyPrint ?
				_prettyWithNulls :
				_compactWithNulls :
			prettyPrint ?
				_pretty :
				_compact;

	static JsonSerializerOptions Build(JsonNamingPolicy? policy, bool serializeNulls, bool prettyPrint) =>
		new()
		{
			PropertyNamingPolicy = policy,
			DefaultIgnoreCondition = serializeNulls ?
				JsonIgnoreCondition.Never :
				JsonIgnoreCondition.WhenWritingNull,
			WriteIndented = prettyPrint
		};
}
