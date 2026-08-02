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
	/// Always throws. <see cref="Result{T}"/> is a deserialization-only type — it exists to carry an
	/// inbound value's parse outcome across the boundary between untrusted wire text and validated
	/// domain data, and nothing downstream of that boundary has legitimate business turning a
	/// <see cref="Result{T}"/> back into wire bytes. This holds for every state: a <see cref="Success{T}"/>
	/// caller already holds the clean value and should serialize <typeparamref name="T"/> directly, never
	/// round-trip it back through the type that exists to validate it in the first place; a
	/// <see cref="Failure"/> or defaulted value was never fit to ship regardless.
	/// </summary>
	/// <exception cref="InvalidOperationException">Always.</exception>
	public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) =>
		throw new InvalidOperationException("Result<T> is a deserialization-only type and must never be written");
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
	/// present value — <see cref="Success{T}"/> or <see cref="Failure"/> alike — always throws, for the
	/// same reason as <see cref="ResultJsonConverter{T}.Write"/>; see its remarks for the full accounting.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is present (<see cref="Nullable{T}.HasValue"/> is <see langword="true"/>).</exception>
	public override void Write(Utf8JsonWriter writer, Result<T>? value, JsonSerializerOptions options)
	{
		if (!value.HasValue)
		{
			writer.WriteNullValue();
			return;
		}
		throw new InvalidOperationException("Result<T> is a deserialization-only type and must never be written");
	}
}
