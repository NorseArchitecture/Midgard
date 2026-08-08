using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives;
using Norse.Primitives.Pii;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
///     STJ converter for the PII rows of the <see cref="Result{T}" /> taxonomy — the JSON leg's
///     mirror of <c>PiiResultSerializer&lt;T&gt;</c>. Every incoming token funnels through
///     <typeparamref name="T" />'s own <c>Parse</c> — the PII taxonomy's one parsing door — so
///     failure wording never diverges by channel: a JSON <c>null</c> parses the empty span
///     (producing the domain's required-missing failure), a string token parses verbatim, number and
///     bool tokens are invariant-stringified first, and object/array tokens are skipped whole and
///     captured as a typed <see cref="Failure" /> — this converter never throws on content, only on
///     a malformed token stream. Write unwraps a success to the scalar's canonical
///     <see cref="IPiiScalar{TSelf}.WireValue" /> — the deliberate plaintext egress, never the
///     masked rendering — and a failed or default stamp is illegal to write, same one law as every
///     other leg.
/// </summary>
/// <typeparam name="T">The PII scalar's type — one row of the forge's PII taxonomy.</typeparam>
public sealed class PiiResultJsonConverter<T> : JsonConverter<Result<T>> where T : struct, IPiiScalar<T>
{
	/// <inheritdoc/>
	public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ? T.Parse([]) : ReadPresent(ref reader);

	/// <summary>
	///     Funnels a present (non-null) token through the scalar's parse door. Shared with
	///     <see cref="NullablePiiResultJsonConverter{T}" />, which handles the <c>null</c> branch
	///     itself (absent-optional rather than required-missing) before delegating here.
	/// </summary>
	internal static Result<T> ReadPresent(ref Utf8JsonReader reader) =>
		reader.TokenType switch
		{
			JsonTokenType.String => T.Parse(reader.GetString() ?? string.Empty),
			JsonTokenType.Number => T.Parse(reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)),
			JsonTokenType.True or JsonTokenType.False => T.Parse(reader.GetBoolean() ? "true" : "false"),
			JsonTokenType.StartObject or JsonTokenType.StartArray => SkipAndFail(ref reader),
			_ => throw new JsonException($"unexpected token {reader.TokenType} reading Result<{typeof(T).Name}>"),
		};

	static Result<T> SkipAndFail(ref Utf8JsonReader reader)
	{
		var kind = reader.TokenType == JsonTokenType.StartObject ? "{object}" : "[array]";
		reader.Skip();
		return new Failure(ParseFailure.Malformed, kind, typeof(T).Name);
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is a <see cref="Failure"/> or defaulted.</exception>
	public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) =>
		WritePresent(writer, value);

	/// <summary>
	///     Unwraps a present success to the scalar's canonical wire string — deliberate
	///     <see cref="IPiiScalar{TSelf}.WireValue" /> egress. Shared with
	///     <see cref="NullablePiiResultJsonConverter{T}" /> for its present branch.
	/// </summary>
	internal static void WritePresent(Utf8JsonWriter writer, Result<T> value)
	{
		if (!value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException("a failed or default Result<T> is illegal to write");
		writer.WriteStringValue(success.Value.WireValue);
	}
}

/// <summary>
///     STJ converter for <c>Result&lt;T&gt;?</c> over the PII rows. A JSON <c>null</c> maps to the
///     CLR <see langword="null" /> (optional-and-absent); any other token delegates to
///     <see cref="PiiResultJsonConverter{T}.ReadPresent" /> so funnel behavior is identical to the
///     non-nullable converter for every present token. Write mirrors the platform's nullable law:
///     absent writes <c>null</c>, present delegates to the unwrap-or-throw path.
/// </summary>
/// <typeparam name="T">The PII scalar's type — one row of the forge's PII taxonomy.</typeparam>
public sealed class NullablePiiResultJsonConverter<T> : JsonConverter<Result<T>?> where T : struct, IPiiScalar<T>
{
	/// <inheritdoc/>
	public override Result<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType == JsonTokenType.Null ? null : PiiResultJsonConverter<T>.ReadPresent(ref reader);

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is present but a <see cref="Failure"/> or defaulted.</exception>
	public override void Write(Utf8JsonWriter writer, Result<T>? value, JsonSerializerOptions options)
	{
		if (value is null)
			writer.WriteNullValue();
		else
			PiiResultJsonConverter<T>.WritePresent(writer, value.Value);
	}
}
