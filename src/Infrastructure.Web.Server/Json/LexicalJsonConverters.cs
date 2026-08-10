using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
///     Plain-scalar STJ converters pinning the §7 lexical table's byte-exact wire forms for the types
///     where STJ's built-in defaults disagree with <see cref="XmlLexical" /> — trimmed fractional zeros or
///     a different round-trip form for <see cref="DateTime" />/<see cref="DateTimeOffset" />/
///     <see cref="TimeOnly" />, and the BCL colon grammar instead of ISO 8601 duration for
///     <see cref="TimeSpan" />. Every <c>Write</c> below calls <see cref="XmlLexical.Format(DateTime)" /> (or the sibling
///     overload for its own type) directly, so
///     JSON and XML are byte-identical by construction — never by two hand-kept-in-sync format strings.
///     Every <c>Read</c> funnels through <see cref="Parser" />, the same lexical space the XML channel
///     accepts. These converters exist for plain scalars outside <see cref="Result{T}" /> — e.g. response-
///     body members — since <see cref="ResultJsonConverter{T}" /> already reaches this same pair
///     (<see cref="Parser" />/<see cref="XmlLexical" />) for its own success-value content via
///     <see cref="JsonSerializer.Serialize{TValue}(Utf8JsonWriter, TValue, JsonSerializerOptions?)" />'s
///     recursive call into whichever converter is registered for <c>T</c>.
/// </summary>
sealed class DateTimeLexicalJsonConverter : JsonConverter<DateTime>
{
	public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		LexicalScalars.Read<DateTime>(ref reader);

	public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
		writer.WriteStringValue(XmlLexical.Format(value));
}

/// <summary>See <see cref="DateTimeLexicalJsonConverter" />'s remarks — same pinning, for <see cref="DateTimeOffset" />.</summary>
sealed class DateTimeOffsetLexicalJsonConverter : JsonConverter<DateTimeOffset>
{
	public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		LexicalScalars.Read<DateTimeOffset>(ref reader);

	public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
		writer.WriteStringValue(XmlLexical.Format(value));
}

/// <summary>See <see cref="DateTimeLexicalJsonConverter" />'s remarks — same pinning, for <see cref="TimeOnly" />.</summary>
sealed class TimeOnlyLexicalJsonConverter : JsonConverter<TimeOnly>
{
	public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		LexicalScalars.Read<TimeOnly>(ref reader);

	public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
		writer.WriteStringValue(XmlLexical.Format(value));
}

/// <summary>
///     See <see cref="DateTimeLexicalJsonConverter" />'s remarks — same pinning, for <see cref="TimeSpan" /> (ISO
///     8601 duration, not the BCL colon form).
/// </summary>
sealed class TimeSpanLexicalJsonConverter : JsonConverter<TimeSpan>
{
	public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		LexicalScalars.Read<TimeSpan>(ref reader);

	public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
		writer.WriteStringValue(XmlLexical.Format(value));
}

/// <summary>Shared read-side funnel for the lexical converters above — one string-token-to-parser path, not four.</summary>
static class LexicalScalars
{
	internal static T Read<T>(ref Utf8JsonReader reader) where T : notnull, ISpanParsable<T>
	{
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException($"expected a JSON string reading {typeof(T).Name}, found {reader.TokenType}");
		return Parser.ParseRequired<T>(reader.GetString() ?? string.Empty, CultureInfo.InvariantCulture) switch
		{
			Success<T>(var value) => value,
			Failure failure => throw new JsonException(FailureDetail.Render(failure))
		};
	}
}
