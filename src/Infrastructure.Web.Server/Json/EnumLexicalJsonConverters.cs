using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
///     Resolves <see cref="PlainEnumJsonConverter{TEnum}" /> for any enum with a table registered in the
///     generated <see cref="EnumNameRegistry" /> — the JSON leg of the platform's enum wire law (spec
///     §7.4): governed case-styled names, never the CLR ordinal, and never a bare enum outside a facade
///     closure.
/// </summary>
/// <param name="registry">
///     The generated per-enum name-table registry — the host passes the same instance
///     <c>AddNorseJson</c> registers.
/// </param>
/// <param name="xmlOptions">
///     Carries the platform's single <see cref="NorseXmlOptions.CaseStyle" /> — the JSON and XML
///     channels never diverge on which case style is live.
/// </param>
public sealed class EnumLexicalJsonConverterFactory(EnumNameRegistry registry, NorseXmlOptions xmlOptions)
	: JsonConverterFactory
{
	/// <inheritdoc />
	public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification =
			"Same finite, closure-bounded posture as ResultJsonConverterFactory.CreateConverter: MakeGenericType closes over an enum type the generated registry already carries a table for, never an unbounded reflection surface.")]
	[UnconditionalSuppressMessage("Trimming", "IL2055",
		Justification =
			"MakeGenericType(typeToConvert) closes PlainEnumJsonConverter<> over an enum the generated registry already carries a table for — a registry-bounded set, never an unbounded reflection surface.")]
	[UnconditionalSuppressMessage("Trimming", "IL2071",
		Justification =
			"TEnum's struct-implying 'unmanaged, Enum' constraint carries an implicit PublicParameterlessConstructor annotation; every enum type trivially satisfies it (value types always have one), so the annotation mismatch on the unattributed typeToConvert parameter is a false positive here.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050",
		Justification =
			"Same posture as ResultJsonConverterFactory.CreateConverter: composition-root wiring over a doctrinally finite, registry-bounded set of enum types; AOT source-generation is a future increment.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (!registry.TryGet(typeToConvert, out var table))
			throw new NotSupportedException(
				$"no generated name table for enum '{typeToConvert.Name}' — an enum outside every facade closure has no text wire law");
		return (JsonConverter)Activator.CreateInstance(typeof(PlainEnumJsonConverter<>).MakeGenericType(typeToConvert),
			table, (int)xmlOptions.CaseStyle)!;
	}
}

/// <summary>
///     STJ converter for a plain (non-<see cref="Result{T}" />) enum value — governed case-styled names
///     over <see cref="EnumLexical" />, never the CLR ordinal. Read refuses every non-string token,
///     including numbers: names-never-numerics is the wire law, not merely the default case style.
/// </summary>
/// <typeparam name="TEnum">The enum type this converter serializes.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class PlainEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<TEnum> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException($"expected a JSON string reading {table.TypeName}, found {reader.TokenType}");
		return EnumLexical.Parse<TEnum>(table, reader.GetString() ?? string.Empty, styleIndex) switch
		{
			Success<TEnum>(var value) => value,
			Failure failure => throw new JsonException(FailureDetail.Render(failure))
		};
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
		writer.WriteStringValue(EnumLexical.Format(table, value, styleIndex));
}

/// <summary>
///     Resolves <see cref="ResultEnumJsonConverter{TEnum}" /> / <see cref="NullableResultEnumJsonConverter{TEnum}" />
///     for any closed <see cref="Result{T}" /> or <c>Result&lt;T&gt;?</c> shape whose argument is an enum
///     with a table registered in <see cref="EnumNameRegistry" /> — the <see cref="Result{T}" />-wrapped half
///     of the enum wire law, twinned with <see cref="EnumLexicalJsonConverterFactory" /> for the plain half.
///     <see cref="ResultJsonConverterFactory" /> refuses every enum-argument <see cref="Result{T}" />, so
///     ordering between the two factories in the converter list never matters.
/// </summary>
/// <param name="registry">
///     The generated per-enum name-table registry — the host passes the same instance
///     <c>AddNorseJson</c> registers.
/// </param>
/// <param name="xmlOptions">
///     Carries the platform's single <see cref="NorseXmlOptions.CaseStyle" /> — the JSON and XML
///     channels never diverge on which case style is live.
/// </param>
public sealed class ResultEnumJsonConverterFactory(EnumNameRegistry registry, NorseXmlOptions xmlOptions)
	: JsonConverterFactory
{
	/// <inheritdoc />
	public override bool CanConvert(Type typeToConvert)
	{
		if (!typeToConvert.IsGenericType)
			return false;
		var definition = typeToConvert.GetGenericTypeDefinition();
		if (definition == typeof(Result<>))
			return typeToConvert.GetGenericArguments()[0].IsEnum;
		return definition == typeof(Nullable<>) && IsEnumResult(typeToConvert.GetGenericArguments()[0]);
	}

	/// <inheritdoc />
	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification =
			"Same finite, closure-bounded posture as ResultJsonConverterFactory.CreateConverter: MakeGenericType closes over an enum type the generated registry already carries a table for, never an unbounded reflection surface.")]
	[UnconditionalSuppressMessage("Trimming", "IL2055",
		Justification =
			"MakeGenericType(enumType) closes ResultEnumJsonConverter<>/NullableResultEnumJsonConverter<> over an enum the generated registry already carries a table for — a registry-bounded set, never an unbounded reflection surface.")]
	[UnconditionalSuppressMessage("Trimming", "IL2071",
		Justification =
			"TEnum's struct-implying 'unmanaged, Enum' constraint carries an implicit PublicParameterlessConstructor annotation; every enum type trivially satisfies it (value types always have one), so the annotation mismatch on the unattributed enumType local is a false positive here.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050",
		Justification =
			"Same posture as ResultJsonConverterFactory.CreateConverter: composition-root wiring over a doctrinally finite, registry-bounded set of enum types; AOT source-generation is a future increment.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (typeToConvert.GetGenericTypeDefinition() == typeof(Result<>))
		{
			var enumType = typeToConvert.GetGenericArguments()[0];
			var table = Resolve(enumType);
			return (JsonConverter)Activator.CreateInstance(typeof(ResultEnumJsonConverter<>).MakeGenericType(enumType),
				table, (int)xmlOptions.CaseStyle)!;
		}

		var resultType = typeToConvert.GetGenericArguments()[0];
		var nullableEnumType = resultType.GetGenericArguments()[0];
		var nullableTable = Resolve(nullableEnumType);
		return (JsonConverter)Activator.CreateInstance(
			typeof(NullableResultEnumJsonConverter<>).MakeGenericType(nullableEnumType), nullableTable,
			(int)xmlOptions.CaseStyle)!;
	}

	EnumNameTable Resolve(Type enumType) =>
		registry.TryGet(enumType, out var table) ?
			table :
			throw new NotSupportedException(
				$"no generated name table for enum '{enumType.Name}' — an enum outside every facade closure has no text wire law");

	static bool IsEnumResult(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>) &&
		type.GetGenericArguments()[0].IsEnum;
}

/// <summary>
///     STJ converter for <c>Result&lt;TEnum&gt;</c> — the <see cref="Result{T}" />-wrapped half of the enum
///     wire law. Every token funnels to a captured <see cref="Failure" /> rather than throwing on content —
///     the same "never throws on content, only on a malformed token stream" posture
///     <see cref="ResultJsonConverter{T}" /> holds for the scalar taxonomy; a JSON <c>null</c> captures the
///     domain's one "required value missing" wording via <see cref="ParseFailure.Empty" />.
/// </summary>
/// <typeparam name="TEnum">The enum type this converter's <see cref="Result{T}" /> wraps.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class ResultEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<Result<TEnum>> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override Result<TEnum> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ?
			new Failure(ParseFailure.Empty, string.Empty, table.TypeName) :
			ReadPresent(ref reader, table, styleIndex);

	/// <summary>
	///     Funnels a present (non-null) token into a captured result. Shared with
	///     <see cref="NullableResultEnumJsonConverter{TEnum}" />, which handles the <c>null</c> branch itself
	///     (absent-optional rather than required-missing) before delegating here.
	/// </summary>
	internal static Result<TEnum> ReadPresent(ref Utf8JsonReader reader, EnumNameTable table, int styleIndex) =>
		reader.TokenType switch
		{
			JsonTokenType.String => EnumLexical.Parse<TEnum>(table, reader.GetString() ?? string.Empty, styleIndex),
			JsonTokenType.Number => new Failure(ParseFailure.Malformed, ReadNumberInvariant(ref reader),
				table.TypeName),
			JsonTokenType.True or JsonTokenType.False => new Failure(ParseFailure.Malformed, reader.GetBoolean() ?
				"true" :
				"false", table.TypeName),
			JsonTokenType.StartObject or JsonTokenType.StartArray => SkipAndFail(ref reader, table),
			_ => throw new JsonException($"unexpected token {reader.TokenType} reading Result<{table.TypeName}>")
		};

	static string ReadNumberInvariant(ref Utf8JsonReader reader) =>
		// JSON's number grammar is already culture-invariant (no thousands separators, '.' always the
		// decimal point) — the raw token text is the invariant text, no reformatting required.
		Encoding.UTF8.GetString(reader.HasValueSequence ?
			BuffersExtensions.ToArray(reader.ValueSequence) :
			reader.ValueSpan);

	static Result<TEnum> SkipAndFail(ref Utf8JsonReader reader, EnumNameTable table)
	{
		var kind = reader.TokenType == JsonTokenType.StartObject ?
			"{object}" :
			"[array]";
		reader.Skip();
		return new Failure(ParseFailure.Malformed, kind, table.TypeName);
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Result<TEnum> value, JsonSerializerOptions options) =>
		WritePresent(writer, value, table, styleIndex);

	/// <summary>
	///     Unwraps a present <see cref="Result{T}" /> for writing. Shared with
	///     <see cref="NullableResultEnumJsonConverter{TEnum}" />, which handles the <c>null</c> branch itself
	///     (absent-optional, never reaching this method) before delegating here for a present value.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value" /> is a <see cref="Failure" /> or defaulted.</exception>
	internal static void WritePresent(Utf8JsonWriter writer, Result<TEnum> value, EnumNameTable table, int styleIndex)
	{
		if (!value.TryGetValue(out Success<TEnum> success))
			throw new InvalidOperationException("a failed or default Result<T> is illegal to write");
		writer.WriteStringValue(EnumLexical.Format(table, success.Value, styleIndex));
	}
}

/// <summary>
///     STJ converter for <c>Result&lt;TEnum&gt;?</c>. A JSON <c>null</c> maps to the CLR
///     <see langword="null" /> (optional-and-absent); any other token delegates to
///     <see cref="ResultEnumJsonConverter{TEnum}.ReadPresent" /> so the funnel behavior is identical to the
///     non-nullable converter for every present token.
/// </summary>
/// <typeparam name="TEnum">The enum type this converter's <see cref="Result{T}" /> wraps.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class NullableResultEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<Result<TEnum>?> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override Result<TEnum>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ?
			null :
			ResultEnumJsonConverter<TEnum>.ReadPresent(ref reader, table, styleIndex);

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Result<TEnum>? value, JsonSerializerOptions options)
	{
		if (!value.HasValue)
		{
			writer.WriteNullValue();
			return;
		}

		ResultEnumJsonConverter<TEnum>.WritePresent(writer, value.GetValueOrDefault(), table, styleIndex);
	}
}
