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
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "The scalar taxonomy Result<T> closes over (ISpanParsable<T>) is a doctrinally finite ~13-type set (spec §7); AOT source-generation for it is a future increment.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (typeToConvert.GetGenericTypeDefinition() == typeof(Result<>))
		{
			var valueType = typeToConvert.GetGenericArguments()[0];
			return (JsonConverter)Activator.CreateInstance(typeof(ResultJsonConverter<>).MakeGenericType(valueType))!;
		}

		var resultType = typeToConvert.GetGenericArguments()[0];
		var nullableValueType = resultType.GetGenericArguments()[0];
		return (JsonConverter)Activator.CreateInstance(typeof(NullableResultJsonConverter<>).MakeGenericType(nullableValueType))!;
	}

	static bool IsResult(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>) && !type.GetGenericArguments()[0].IsEnum;
}
