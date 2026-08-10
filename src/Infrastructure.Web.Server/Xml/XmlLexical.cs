using System.Globalization;
using System.Xml;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The canonical, byte-exact scalar emission functions for Futhark's XML and JSON writers — one
///     pinned wire form per type, per the design spec's §7 lexical table. Deliberately
///     <see langword="public" />, not <see langword="internal sealed" />: generated code in a host
///     compilation (a different repo, later task) calls these functions, so they must be visible outside
///     this assembly.
/// </summary>
public static class XmlLexical
{
	/// <summary>Emits <c>"true"</c> or <c>"false"</c>.</summary>
	public static string Format(bool value) =>
		value ?
			"true" :
			"false";

	/// <summary>Emits invariant plain decimal notation — no exponent, no separators.</summary>
	public static string Format(decimal value) =>
		value.ToString(CultureInfo.InvariantCulture);

	/// <summary>Emits the invariant shortest round-trippable form.</summary>
	/// <exception cref="InvalidOperationException"><paramref name="value" /> is <see cref="double.NaN" /> or infinite.</exception>
	public static string Format(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
			throw new InvalidOperationException("non-finite values are illegal to write");

		return value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Emits the invariant shortest round-trippable form.</summary>
	/// <exception cref="InvalidOperationException"><paramref name="value" /> is <see cref="float.NaN" /> or infinite.</exception>
	public static string Format(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
			throw new InvalidOperationException("non-finite values are illegal to write");

		return value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Emits lowercase hyphenated <c>"D"</c> format.</summary>
	public static string Format(Guid value) =>
		value.ToString("D", CultureInfo.InvariantCulture);

	/// <summary>Emits ISO 8601 round-trip (<c>"O"</c>) form, kind suffix preserved.</summary>
	public static string Format(DateTime value) =>
		value.ToString("O", CultureInfo.InvariantCulture);

	/// <summary>Emits ISO 8601 round-trip (<c>"O"</c>) form.</summary>
	public static string Format(DateTimeOffset value) =>
		value.ToString("O", CultureInfo.InvariantCulture);

	/// <summary>Emits <c>"yyyy-MM-dd"</c>.</summary>
	public static string Format(DateOnly value) =>
		value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	/// <summary>Emits <c>"O"</c> form (<c>HH:mm:ss.fffffff</c>).</summary>
	public static string Format(TimeOnly value) =>
		value.ToString("O", CultureInfo.InvariantCulture);

	/// <summary>Emits ISO 8601 duration form (e.g. <c>P1DT2H3M4S</c>) — culture-proof and XML-native.</summary>
	public static string Format(TimeSpan value) =>
		XmlConvert.ToString(value);

	/// <summary>Emits the single character verbatim.</summary>
	/// <exception cref="InvalidOperationException"><paramref name="value" /> is not a legal XML 1.0 character.</exception>
	public static string Format(char value)
	{
		if (!IsXmlLegalChar(value))
			throw new InvalidOperationException(
				$"'\\u{(int)value:X4}' is not a legal XML character and is illegal to write.");

		return value.ToString();
	}

	static bool IsXmlLegalChar(char value) =>
		value is '\t' or '\n' or '\r'
			or >= ' ' and <= '\uD7FF'
			or >= '\uE000' and <= '\uFFFD';
}
