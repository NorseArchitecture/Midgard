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
		// [Flags] is detected once here, at converter construction — STJ calls CreateConverter (and
		// caches the result) once per closed type, never per Read/Write call.
		var converterType = typeToConvert.IsDefined(typeof(FlagsAttribute), inherit: false) ?
			typeof(FlagsEnumJsonConverter<>) :
			typeof(PlainEnumJsonConverter<>);
		return (JsonConverter)Activator.CreateInstance(converterType.MakeGenericType(typeToConvert),
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
///     STJ converter for a plain (non-<see cref="Result{T}" />) <c>[Flags]</c> enum value — a JSON array of
///     governed case-styled names over the same <see cref="EnumNameTable" />/<see cref="EnumLexical" />
///     mechanism <see cref="PlainEnumJsonConverter{TEnum}" /> uses for a non-flags enum: decomposed at
///     write, OR-accumulated at read. Composite/leftover bits are illegal to write; the empty array is
///     the zero value on read, legal with or without a named zero member.
/// </summary>
/// <typeparam name="TEnum">The <c>[Flags]</c> enum type this converter serializes.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class FlagsEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<TEnum> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException($"expected a JSON array reading {table.TypeName}, found {reader.TokenType}");

		List<string> tokens = [];
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			if (reader.TokenType != JsonTokenType.String)
				throw new JsonException(
					$"expected a JSON string array element reading {table.TypeName}, found {reader.TokenType}");
			tokens.Add(reader.GetString() ?? string.Empty);
		}

		return EnumLexical.ParseFlags<TEnum>(table, tokens, styleIndex) switch
		{
			Success<TEnum>(var value) => value,
			Failure failure => throw new JsonException(FailureDetail.Render(failure))
		};
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		foreach (var name in EnumLexical.FormatFlags(table, value, styleIndex))
			writer.WriteStringValue(name);
		writer.WriteEndArray();
	}
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
			// [Flags] is detected once here, at converter construction — STJ calls CreateConverter (and
			// caches the result) once per closed type, never per Read/Write call.
			var converterType = enumType.IsDefined(typeof(FlagsAttribute), inherit: false) ?
				typeof(ResultFlagsEnumJsonConverter<>) :
				typeof(ResultEnumJsonConverter<>);
			return (JsonConverter)Activator.CreateInstance(converterType.MakeGenericType(enumType),
				table, (int)xmlOptions.CaseStyle)!;
		}

		var resultType = typeToConvert.GetGenericArguments()[0];
		var nullableEnumType = resultType.GetGenericArguments()[0];
		var nullableTable = Resolve(nullableEnumType);
		// [Flags] is detected once here, at converter construction — STJ calls CreateConverter (and
		// caches the result) once per closed type, never per Read/Write call.
		var nullableConverterType = nullableEnumType.IsDefined(typeof(FlagsAttribute), inherit: false) ?
			typeof(NullableResultFlagsEnumJsonConverter<>) :
			typeof(NullableResultEnumJsonConverter<>);
		return (JsonConverter)Activator.CreateInstance(
			nullableConverterType.MakeGenericType(nullableEnumType), nullableTable,
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

	// internal, not private: ResultFlagsEnumJsonConverter<TEnum> (below) reuses the identical
	// invariant-number-text rendering for its own array-element "not a string" failure detail.
	internal static string ReadNumberInvariant(ref Utf8JsonReader reader) =>
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

/// <summary>
///     STJ converter for <c>Result&lt;TEnum&gt;</c> where <typeparamref name="TEnum" /> is <c>[Flags]</c> —
///     the array/flags twin of <see cref="ResultEnumJsonConverter{TEnum}" />. Every token funnels to a
///     captured <see cref="Failure" /> rather than throwing on content, the same posture the scalar
///     converter holds: a non-array top-level token, a non-string array element, an unknown token, and a
///     duplicate token are all captured, never thrown; only a malformed token stream throws.
/// </summary>
/// <typeparam name="TEnum">The <c>[Flags]</c> enum type this converter's <see cref="Result{T}" /> wraps.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class ResultFlagsEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<Result<TEnum>> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override Result<TEnum> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ?
			new Failure(ParseFailure.Empty, string.Empty, table.TypeName) :
			ReadPresent(ref reader, table, styleIndex);

	/// <summary>
	///     Funnels a present (non-null) token into a captured result. Shared with
	///     <see cref="NullableResultFlagsEnumJsonConverter{TEnum}" />, which handles the <c>null</c> branch
	///     itself (absent-optional rather than required-missing) before delegating here.
	/// </summary>
	internal static Result<TEnum> ReadPresent(ref Utf8JsonReader reader, EnumNameTable table, int styleIndex) =>
		reader.TokenType switch
		{
			JsonTokenType.StartArray => ReadArray(ref reader, table, styleIndex),
			JsonTokenType.String => new Failure(ParseFailure.Malformed, reader.GetString() ?? string.Empty,
				table.TypeName),
			JsonTokenType.Number => new Failure(ParseFailure.Malformed,
				ResultEnumJsonConverter<TEnum>.ReadNumberInvariant(ref reader), table.TypeName),
			JsonTokenType.True or JsonTokenType.False => new Failure(ParseFailure.Malformed, reader.GetBoolean() ?
				"true" :
				"false", table.TypeName),
			JsonTokenType.StartObject => SkipAndFail(ref reader, table),
			_ => throw new JsonException($"unexpected token {reader.TokenType} reading Result<{table.TypeName}>[]")
		};

	static Result<TEnum> ReadArray(ref Utf8JsonReader reader, EnumNameTable table, int styleIndex)
	{
		List<string> tokens = [];
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			if (reader.TokenType == JsonTokenType.String)
			{
				tokens.Add(reader.GetString() ?? string.Empty);
				continue;
			}

			var kind = DescribeElement(ref reader);
			SkipToEndOfArray(ref reader);
			return new Failure(ParseFailure.Malformed, kind, table.TypeName);
		}

		return EnumLexical.ParseFlags<TEnum>(table, tokens, styleIndex);
	}

	static string DescribeElement(ref Utf8JsonReader reader) => reader.TokenType switch
	{
		JsonTokenType.Number => ResultEnumJsonConverter<TEnum>.ReadNumberInvariant(ref reader),
		JsonTokenType.True or JsonTokenType.False => reader.GetBoolean() ? "true" : "false",
		JsonTokenType.Null => "null",
		JsonTokenType.StartObject => "{object}",
		JsonTokenType.StartArray => "[array]",
		_ => reader.TokenType.ToString()
	};

	// Advances past every remaining array element (scalar or nested container alike) so the reader is
	// left positioned at the array's own EndArray — the sibling of SkipAndFail's single reader.Skip()
	// call below, generalized to "possibly several elements still unread".
	static void SkipToEndOfArray(ref Utf8JsonReader reader)
	{
		reader.Skip();
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			reader.Skip();
	}

	static Result<TEnum> SkipAndFail(ref Utf8JsonReader reader, EnumNameTable table)
	{
		reader.Skip();
		return new Failure(ParseFailure.Malformed, "{object}", table.TypeName);
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Result<TEnum> value, JsonSerializerOptions options) =>
		WritePresent(writer, value, table, styleIndex);

	/// <summary>
	///     Unwraps a present <see cref="Result{T}" /> for writing. Shared with
	///     <see cref="NullableResultFlagsEnumJsonConverter{TEnum}" />, which handles the <c>null</c> branch
	///     itself (absent-optional, never reaching this method) before delegating here for a present value.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value" /> is a <see cref="Failure" /> or defaulted.</exception>
	internal static void WritePresent(Utf8JsonWriter writer, Result<TEnum> value, EnumNameTable table, int styleIndex)
	{
		if (!value.TryGetValue(out Success<TEnum> success))
			throw new InvalidOperationException("a failed or default Result<T> is illegal to write");
		writer.WriteStartArray();
		foreach (var name in EnumLexical.FormatFlags(table, success.Value, styleIndex))
			writer.WriteStringValue(name);
		writer.WriteEndArray();
	}
}

/// <summary>
///     STJ converter for <c>Result&lt;TEnum&gt;?</c> where <typeparamref name="TEnum" /> is <c>[Flags]</c>.
///     A JSON <c>null</c> maps to the CLR <see langword="null" /> (optional-and-absent); any other token
///     delegates to <see cref="ResultFlagsEnumJsonConverter{TEnum}.ReadPresent" /> so the funnel behavior
///     is identical to the non-nullable converter for every present token.
/// </summary>
/// <typeparam name="TEnum">The <c>[Flags]</c> enum type this converter's <see cref="Result{T}" /> wraps.</typeparam>
/// <param name="table">The generated name table for <typeparamref name="TEnum" />.</param>
/// <param name="styleIndex">
///     The active <see cref="XmlCaseStyle" />, as its ordinal column index into
///     <paramref name="table" />.
/// </param>
public sealed class NullableResultFlagsEnumJsonConverter<TEnum>(EnumNameTable table, int styleIndex)
	: JsonConverter<Result<TEnum>?> where TEnum : unmanaged, Enum
{
	/// <inheritdoc />
	public override Result<TEnum>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ?
			null :
			ResultFlagsEnumJsonConverter<TEnum>.ReadPresent(ref reader, table, styleIndex);

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Result<TEnum>? value, JsonSerializerOptions options)
	{
		if (!value.HasValue)
		{
			writer.WriteNullValue();
			return;
		}

		ResultFlagsEnumJsonConverter<TEnum>.WritePresent(writer, value.GetValueOrDefault(), table, styleIndex);
	}
}
