using System.Text;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
/// A runtime port of the Xml.Generator's <c>NameCasing</c> (Task 5) — same word-splitting algorithm,
/// same five-way join, byte-identical output. Duplicated rather than shared: the generator lives in a
/// netstandard2.0, compile-time-only assembly this runtime library cannot reference (and the
/// generator's own <c>NameCasing.cs</c> carries the mirror-image remark for its side of the same
/// boundary, keying off a process-local enum with ordinal values pinned to match this assembly's
/// public <see cref="XmlCaseStyle"/>). <see cref="XmlMetadataTransformer"/> uses this to case-style
/// the wire names it stamps into the OpenAPI document's <c>xml</c> objects, so the document's
/// declared names match what the generated shapes actually emit on the wire at the host's chosen
/// case style — never a second, independently-drifting casing rule.
/// </summary>
static class RuntimeNameCasing
{
	/// <summary>Projects <paramref name="name"/> through <paramref name="style"/>.</summary>
	public static string Apply(XmlCaseStyle style, string name)
	{
		var words = Split(name);
		return style switch
		{
			XmlCaseStyle.CamelCase => JoinCamel(words),
			XmlCaseStyle.PascalCase => JoinPascal(words),
			XmlCaseStyle.SnakeCase => string.Join("_", words.Select(w => w.ToLowerInvariant())),
			XmlCaseStyle.UpperCase => string.Concat(words).ToUpperInvariant(),
			XmlCaseStyle.LowerCase => string.Concat(words).ToLowerInvariant(),
			_ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unrecognized XmlCaseStyle.")
		};
	}

	static string JoinCamel(List<string> words)
	{
		if (words.Count == 0)
			return string.Empty;

		StringBuilder sb = new();
		sb.Append(LowerFirst(words[0]));
		for (var i = 1; i < words.Count; i++)
			sb.Append(UpperFirst(words[i]));
		return sb.ToString();
	}

	static string JoinPascal(List<string> words) =>
		string.Concat(words.Select(UpperFirst));

	static string UpperFirst(string word) =>
		word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();

	static string LowerFirst(string word) =>
		word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word.Substring(1).ToLowerInvariant();

	/// <summary>Splits an identifier into words on Pascal/camel word boundaries — identical rule set to the generator's own <c>NameCasing.Split</c>.</summary>
	static List<string> Split(string name)
	{
		List<string> words = [];
		if (name.Length == 0)
			return words;

		var start = 0;
		for (var i = 1; i < name.Length; i++)
		{
			var boundary = false;
			if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
				boundary = true; // lower/digit -> upper
			else if (char.IsUpper(name[i]) && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]))
				boundary = true; // acronym run -> the last capital starts the next word

			if (boundary)
			{
				words.Add(name.Substring(start, i - start));
				start = i;
			}
		}
		words.Add(name.Substring(start));
		return words;
	}
}
