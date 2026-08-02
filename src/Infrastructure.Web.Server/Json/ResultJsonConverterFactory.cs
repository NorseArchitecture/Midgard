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
/// tests, without going through <see cref="MvcBuilderExtensions.AddNorseJson"/>.
/// </summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
	/// <inheritdoc/>
	public override bool CanConvert(Type typeToConvert)
	{
		if (!typeToConvert.IsGenericType)
			return false;
		var definition = typeToConvert.GetGenericTypeDefinition();
		if (definition == typeof(Result<>))
			return true;
		return definition == typeof(Nullable<>) && IsResult(typeToConvert.GetGenericArguments()[0]);
	}

	/// <inheritdoc/>
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "The scalar taxonomy Result<T> closes over (ISpanParsable<T>) is a doctrinally finite ~13-type set (spec §7); AOT source-generation for it is a future increment.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (typeToConvert.GetGenericTypeDefinition() == typeof(Result<>))
		{
			var valueType = typeToConvert.GetGenericArguments()[0];
			ThrowIfEnum(valueType);
			return (JsonConverter)Activator.CreateInstance(typeof(ResultJsonConverter<>).MakeGenericType(valueType))!;
		}

		var resultType = typeToConvert.GetGenericArguments()[0];
		var nullableValueType = resultType.GetGenericArguments()[0];
		ThrowIfEnum(nullableValueType);
		return (JsonConverter)Activator.CreateInstance(typeof(NullableResultJsonConverter<>).MakeGenericType(nullableValueType))!;
	}

	// Result<TEnum> has no JSON wire law yet: the converters are constrained to ISpanParsable<T>,
	// which no enum satisfies, and the enum name tables (§7's case-styled member names) live in the
	// generated XML shapes with no JSON-channel equivalent designed. Refuse with the named gap rather
	// than letting MakeGenericType surface it as a bare generic-constraint ArgumentException.
	static void ThrowIfEnum(Type valueType)
	{
		if (valueType.IsEnum)
			throw new NotSupportedException(
				$"Result<{valueType.Name}> has no JSON wire law — enums parse through the generated XML shapes' name tables, and the JSON channel has no equivalent mechanism yet");
	}

	static bool IsResult(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);
}
