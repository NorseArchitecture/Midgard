using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
/// Resolves <see cref="ResultJsonConverter{T}"/> / <see cref="NullableResultJsonConverter{T}"/> for
/// any closed <see cref="Result{T}"/> or <c>Result&lt;T&gt;?</c> shape in the platform's scalar
/// taxonomy — the full <c>where T : notnull</c> set, including <see cref="string"/>. Deliberately
/// <see langword="public"/>: usable standalone against a bare <see cref="JsonSerializerOptions"/> in
/// tests, without going through <c>AddNorseJson</c>.
/// </summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
	/// <inheritdoc/>
	// Enum-argument Result<T> is refused here — ResultEnumJsonConverterFactory owns Result<TEnum> /
	// Result<TEnum>? via the generated EnumNameRegistry, so a given Result<T> shape is claimed by
	// exactly one factory and ordering between the two in the converter list never matters.
	public override bool CanConvert(Type typeToConvert)
	{
		if (!typeToConvert.IsGenericType)
			return false;
		var definition = typeToConvert.GetGenericTypeDefinition();
		if (definition == typeof(Result<>))
			return !typeToConvert.GetGenericArguments()[0].IsEnum;
		return definition == typeof(Nullable<>) && IsResult(typeToConvert.GetGenericArguments()[0]);
	}

	/// <inheritdoc/>
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "The scalar taxonomy Result<T> closes over (ISpanParsable<T> + the PII rows) is a doctrinally finite set (spec §7); AOT source-generation for it is a future increment.")]
	[UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Same finite-taxonomy posture as IL3050 above: every closed Result<T> converter shape is reachable only over the doctrinally finite scalar set, all of whose converter types are declared in this assembly.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (typeToConvert.GetGenericTypeDefinition() == typeof(Result<>))
		{
			var valueType = typeToConvert.GetGenericArguments()[0];
			return (JsonConverter)Activator.CreateInstance(
				(IsPiiScalar(valueType) ? typeof(PiiResultJsonConverter<>) : typeof(ResultJsonConverter<>)).MakeGenericType(valueType))!;
		}

		var resultType = typeToConvert.GetGenericArguments()[0];
		var nullableValueType = resultType.GetGenericArguments()[0];
		return (JsonConverter)Activator.CreateInstance(
			(IsPiiScalar(nullableValueType) ? typeof(NullablePiiResultJsonConverter<>) : typeof(NullableResultJsonConverter<>)).MakeGenericType(nullableValueType))!;
	}

	static bool IsResult(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>) && !type.GetGenericArguments()[0].IsEnum;

	// The PII rows carry no ISpanParsable — their Parse returns Result<T> — so they route to the
	// PII converter pair (WireValue out, T.Parse in) instead of the ISpanParsable-constrained pair,
	// which would otherwise fail at closed-generic construction, at runtime.
	[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The PII scalars are concrete structs referenced directly by the contract types that carry them; their interface lists survive trimming with the types themselves.")]
	static bool IsPiiScalar(Type type) =>
		Array.Exists(type.GetInterfaces(), static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Norse.Primitives.Pii.IPiiScalar<>));
}
