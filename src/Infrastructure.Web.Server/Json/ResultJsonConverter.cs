using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
/// STJ converter for <see cref="Result{T}"/> — the JSON leg of the platform's one parsing funnel
/// (spec §9.1). Every incoming token, whatever its native JSON shape, resolves through
/// <see cref="Parser.ParseRequired{T}"/> so failure wording never diverges by channel: string tokens
/// funnel directly; number/bool tokens are invariant-stringified first (JSON's own number lexical
/// form is already culture-invariant, so no reformatting is needed); a JSON <c>null</c> funnels
/// <see cref="string.Empty"/> through the same required-parse door, producing the domain's one
/// "required value missing" wording rather than a serializer-specific message; object/array tokens
/// are skipped whole and captured as a typed <see cref="Failure"/> — this converter never throws on
/// content, only on a malformed token stream.
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
			JsonTokenType.String => Parser.ParseRequired<T>(reader.GetString() ?? string.Empty, CultureInfo.InvariantCulture),
			JsonTokenType.Number => Parser.ParseRequired<T>(ReadNumberInvariant(ref reader), CultureInfo.InvariantCulture),
			JsonTokenType.True or JsonTokenType.False => Parser.ParseRequired<T>(reader.GetBoolean() ? "true" : "false", CultureInfo.InvariantCulture),
			JsonTokenType.StartObject or JsonTokenType.StartArray => SkipAndFail(ref reader),
			_ => throw new JsonException($"unexpected token {reader.TokenType} reading Result<{typeof(T).Name}>")
		};

	static string ReadNumberInvariant(ref Utf8JsonReader reader) =>
		// JSON's number grammar is already culture-invariant (no thousands separators, '.' always the
		// decimal point) — the raw token text is the invariant text, no reformatting required.
		Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan);

	static Result<T> SkipAndFail(ref Utf8JsonReader reader)
	{
		var kind = reader.TokenType == JsonTokenType.StartObject ? "{object}" : "[array]";
		reader.Skip();
		return new Failure(ParseFailure.Malformed, kind, typeof(T).Name);
	}

	/// <summary>
	/// Writes the unwrapped success value using the same lexical forms as the XML channel (via
	/// <see cref="JsonSerializer.Serialize{TValue}(Utf8JsonWriter, TValue, JsonSerializerOptions?)"/>'s
	/// recursive call, which reaches the registered lexical converters for DateTime/DateTimeOffset/
	/// TimeOnly/TimeSpan). <b>This path has no production consumer.</b> Per spec §1.3, text channels
	/// are for strangers — internal clients are gRPC end-to-end, and this platform's own code never
	/// legitimately writes a <see cref="Result{T}"/> as an outbound JSON request. It exists solely as
	/// test infrastructure: the round-trip test suite needs to author wire-shaped JSON request bodies,
	/// and this is how it does it. A failed or default <see cref="Result{T}"/> is illegal to write —
	/// "you do not ship failures."
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is a failed or default <see cref="Result{T}"/>.</exception>
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Success<T>.Value is T itself, from the closed ISpanParsable<T> scalar taxonomy — no unknown types reach this recursive Serialize call.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Same closed scalar taxonomy as above; AOT source-generation for this finite type set is a future increment.")]
	public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
	{
		if (!value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException("a failed Result<T> is illegal to write");
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
	/// Writes <see langword="null"/> as JSON <c>null</c>; otherwise writes the unwrapped success value.
	/// Test-infrastructure-only, for the same reason as <see cref="ResultJsonConverter{T}.Write"/> —
	/// see its remarks for the full honest accounting (spec §1.3).
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is a failed or default <see cref="Result{T}"/>.</exception>
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Success<T>.Value is T itself, from the closed ISpanParsable<T> scalar taxonomy — no unknown types reach this recursive Serialize call.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Same closed scalar taxonomy as above; AOT source-generation for this finite type set is a future increment.")]
	public override void Write(Utf8JsonWriter writer, Result<T>? value, JsonSerializerOptions options)
	{
		if (!value.HasValue)
		{
			writer.WriteNullValue();
			return;
		}
		if (!value.Value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException("a failed Result<T> is illegal to write");
		JsonSerializer.Serialize(writer, success.Value, options);
	}
}
