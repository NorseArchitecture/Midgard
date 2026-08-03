using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
/// STJ converter for <see cref="Result{T}"/> — the JSON leg of the platform's one parsing funnel
/// (spec §9.1). Every incoming token, whatever its native JSON shape, resolves through
/// <see cref="Parser.ParseRequired{T}"/> so failure wording never diverges by channel: string tokens
/// funnel directly — <b>except when <c>T</c> is <see cref="string"/> itself</b>, where per §7's
/// lexical table (<c>string</c>'s wire form is "verbatim") there is nothing to parse, so the read
/// text wraps straight into <see cref="Success{T}"/>, bypassing <see cref="Parser.ParseRequired{T}"/>
/// entirely; number/bool tokens are invariant-stringified first (JSON's own number lexical form is
/// already culture-invariant, so no reformatting is needed); a JSON <c>null</c> funnels
/// <see cref="string.Empty"/> through the same required-parse door — including for <c>T = string</c>
/// — producing the domain's one "required value missing" wording rather than a serializer-specific
/// message; object/array tokens are skipped whole and captured as a typed <see cref="Failure"/> —
/// this converter never throws on content, only on a malformed token stream.
/// </summary>
/// <typeparam name="T">The validated value's type. Constrained to what <see cref="Parser"/> can route.</typeparam>
public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>> where T : notnull, ISpanParsable<T>
{
	/// <inheritdoc/>
	public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ?
			Parser.ParseRequired<T>(string.Empty, CultureInfo.InvariantCulture) :
			ReadPresent(ref reader);

	/// <summary>
	/// Funnels a present (non-null) token through the parser. Shared with
	/// <see cref="NullableResultJsonConverter{T}"/>, which handles the <c>null</c> branch itself
	/// (absent-optional rather than required-missing) before delegating here.
	/// </summary>
	internal static Result<T> ReadPresent(ref Utf8JsonReader reader) =>
		reader.TokenType switch
		{
			JsonTokenType.String => typeof(T) == typeof(string) ? ReadStringDirect(ref reader) : Parser.ParseRequired<T>(reader.GetString() ?? string.Empty, CultureInfo.InvariantCulture),
			JsonTokenType.Number => Parser.ParseRequired<T>(ReadNumberInvariant(ref reader), CultureInfo.InvariantCulture),
			JsonTokenType.True or JsonTokenType.False => Parser.ParseRequired<T>(reader.GetBoolean() ? "true" : "false", CultureInfo.InvariantCulture),
			JsonTokenType.StartObject or JsonTokenType.StartArray => SkipAndFail(ref reader),
			_ => throw new JsonException($"unexpected token {reader.TokenType} reading Result<{typeof(T).Name}>")
		};

	static string ReadNumberInvariant(ref Utf8JsonReader reader) =>
		// JSON's number grammar is already culture-invariant (no thousands separators, '.' always the
		// decimal point) — the raw token text is the invariant text, no reformatting required.
		Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan);

	// In this JIT-eliminated branch T is statically string (guarded by the typeof check at the call
	// site, the same BCL generic-specialization pattern Parser.cs itself uses); the reinterpret is an
	// identity the type system cannot express. Presence is carried entirely by which token STJ saw —
	// a present string token, empty or not, is content, never "required missing" — so this bypasses
	// Parser.ParseRequired<string> outright rather than funneling "" through the same door the null
	// branch uses for the synthesized required-missing failure. Every other type in the taxonomy still
	// routes through the parser.
	static Result<T> ReadStringDirect(ref Utf8JsonReader reader)
	{
		Result<string> routed = new Success<string>(reader.GetString() ?? string.Empty);
		return Unsafe.As<Result<string>, Result<T>>(ref routed);
	}

	static Result<T> SkipAndFail(ref Utf8JsonReader reader)
	{
		var kind = reader.TokenType == JsonTokenType.StartObject ? "{object}" : "[array]";
		reader.Skip();
		return new Failure(ParseFailure.Malformed, kind, typeof(T).Name);
	}

	/// <summary>
	/// Unwraps a <see cref="Success{T}"/> to <typeparamref name="T"/>'s own wire form; a
	/// <see cref="Failure"/> or defaulted value throws. The path is legal everywhere and exercised by
	/// this converter's own round-trip suites — it is not gated to any one channel — but production
	/// gRPC clients never reach it: gRPC carries the binary wire law directly, so this JSON leg's actual
	/// consumers are strangers to that contract (spec §1.3) probing the text channel, not the platform's
	/// own clients.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is a <see cref="Failure"/> or defaulted.</exception>
	public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) =>
		WritePresent(writer, value, options);

	/// <summary>
	/// Unwraps a present <see cref="Result{T}"/> for writing. Shared with
	/// <see cref="NullableResultJsonConverter{T}"/>, which handles the <c>null</c> branch itself
	/// (absent-optional, never reaching this method) before delegating here for a present value.
	/// </summary>
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Same finite scalar taxonomy as ResultJsonConverterFactory.CreateConverter (spec §7, ~13 types, all ISpanParsable<T>); no unbounded reflection surface to trim.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Same posture as ResultJsonConverterFactory.CreateConverter: the closed scalar taxonomy is doctrinally finite; AOT source-generation for it is a future increment.")]
	internal static void WritePresent(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
	{
		if (!value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException("a failed or default Result<T> is illegal to write");
		JsonSerializer.Serialize(writer, success.Value, options);
	}
}

/// <summary>
/// STJ converter for <c>Result&lt;T&gt;?</c>. A JSON <c>null</c> maps to the CLR <see langword="null"/>
/// (optional-and-absent); any other token delegates to <see cref="ResultJsonConverter{T}.ReadPresent"/>
/// so the funnel behavior is identical to the non-nullable converter for every present token.
/// </summary>
/// <typeparam name="T">The validated value's type. Constrained to what <see cref="Parser"/> can route.</typeparam>
public sealed class NullableResultJsonConverter<T> : JsonConverter<Result<T>?> where T : notnull, ISpanParsable<T>
{
	/// <inheritdoc/>
	public override Result<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ? null : ResultJsonConverter<T>.ReadPresent(ref reader);

	/// <summary>
	/// Writes <see langword="null"/> for the optional-and-absent case — that is orthogonal to writing a
	/// <see cref="Result{T}"/> value, the same reason protobuf-net's own <see cref="Nullable{T}"/>
	/// handling never even reaches the gRPC leg's serializer for an absent field, and the same reason
	/// the generated XML writer omits an absent optional attribute rather than throwing for it. A
	/// present value delegates to <see cref="ResultJsonConverter{T}.WritePresent"/>'s unwrap-or-throw
	/// law: a present <see cref="Success{T}"/> unwraps to <typeparamref name="T"/>'s own wire form,
	/// legal everywhere and exercised by this converter's own round-trip suites — production gRPC
	/// clients simply never reach it, gRPC carrying the binary wire law directly; a present
	/// <see cref="Failure"/> or defaulted value throws.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is present and holds a <see cref="Failure"/> or defaulted <see cref="Result{T}"/>.</exception>
	public override void Write(Utf8JsonWriter writer, Result<T>? value, JsonSerializerOptions options)
	{
		if (!value.HasValue)
		{
			writer.WriteNullValue();
			return;
		}
		ResultJsonConverter<T>.WritePresent(writer, value.GetValueOrDefault(), options);
	}
}
