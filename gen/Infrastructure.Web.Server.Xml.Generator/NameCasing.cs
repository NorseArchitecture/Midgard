using System.Text;

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

/// <summary>
///     The five wire-name casing conventions Futhark projects element/attribute/enum-member names
///     through. A compiler-process-local mirror of the runtime <c>XmlCaseStyle</c> enum living in
///     <c>Norse.Infrastructure.Web.Server.Xml</c> (a different assembly, a different TFM, never
///     referenced from here) — ordinal values line up 1:1 so a generated shape's five-entry name table
///     can be indexed by <c>(int)</c> the runtime enum on the host side without this project ever taking
///     a compile-time dependency on it.
/// </summary>
enum XmlCaseStyle
{
	CamelCase,
	PascalCase,
	SnakeCase,
	UpperCase,
	LowerCase
}

/// <summary>
///     Projects a PascalCase CLR identifier through Futhark's five wire-name casing conventions.
///     Splits on Pascal word boundaries (a run of uppercase letters followed by a lowercase letter
///     starts a new word, and a lowercase-to-uppercase transition starts a new word), then rejoins per
///     style: camel/Pascal join with no separator (first word's case flipped for camel); snake
///     lower-joins with <c>_</c>; upper/lower flatten with no separator at all.
/// </summary>
static class NameCasing
{
	/// <summary>Projects <paramref name="name" /> through <paramref name="style" />.</summary>
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

	/// <summary>All five casings of <paramref name="name" />, ordinal-indexed to match <see cref="XmlCaseStyle" />.</summary>
	public static EquatableArray<string> ApplyAll(string name) =>
		EquatableArray<string>.Create(
		[
			Apply(XmlCaseStyle.CamelCase, name),
			Apply(XmlCaseStyle.PascalCase, name),
			Apply(XmlCaseStyle.SnakeCase, name),
			Apply(XmlCaseStyle.UpperCase, name),
			Apply(XmlCaseStyle.LowerCase, name)
		]);

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
		word.Length == 0 ?
			word :
			char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();

	static string LowerFirst(string word) =>
		word.Length == 0 ?
			word :
			char.ToLowerInvariant(word[0]) + word.Substring(1).ToLowerInvariant();

	/// <summary>
	///     Splits a PascalCase identifier into words on Pascal word boundaries. A digit run attaches to
	///     the preceding letter run (no boundary before a digit). An acronym run (<c>ABCFoo</c>) breaks
	///     before the final uppercase letter of the run when followed by a lowercase letter, so
	///     <c>ABCFoo</c> splits as <c>AB</c> / <c>CFoo</c> — the conventional acronym-boundary rule.
	/// </summary>
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
			else if (char.IsUpper(name[i]) && char.IsUpper(name[i - 1]) && i + 1 < name.Length &&
				char.IsLower(name[i + 1]))
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
