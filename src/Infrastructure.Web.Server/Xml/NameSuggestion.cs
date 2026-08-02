namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Finds the nearest known name to an unrecognized attribute or element name — the "did you mean"
/// half of the §8.1 unknown-attribute/unknown-element accumulable failures. Deliberately
/// <see langword="public"/>, not <see langword="internal sealed"/>: generated reader code in a host
/// compilation (a different repo, later task) calls this, so it must be visible outside this assembly.
/// </summary>
public static class NameSuggestion
{
	/// <summary>The maximum edit distance a suggestion is offered within — beyond this, the names are unrelated, and no suggestion is better than a wrong one.</summary>
	public const int MaxDistance = 2;

	/// <summary>
	/// Returns the closest name in <paramref name="known"/> to <paramref name="candidate"/>, by
	/// case-insensitive Levenshtein edit distance, or <see langword="null"/> when nothing is within
	/// <see cref="MaxDistance"/>. Case-insensitive by design — a typo commonly carries a casing slip
	/// alongside the content mistake (e.g. <c>birthday</c> for <c>birthDate</c>: case-sensitive distance
	/// 3, case-insensitive distance 2), and the wire's own case style is already a per-request constant
	/// the caller controls, not something a suggestion needs to re-litigate. Ties keep the first
	/// minimal-distance candidate encountered, so a deterministic <paramref name="known"/> ordering
	/// (declaration order, as every caller in this codebase supplies) yields a deterministic suggestion.
	/// </summary>
	/// <param name="candidate">The unrecognized name actually seen on the wire.</param>
	/// <param name="known">The declared names it might have been a typo of.</param>
	/// <exception cref="ArgumentNullException"><paramref name="candidate"/> or <paramref name="known"/> is null.</exception>
	public static string? Nearest(string candidate, IEnumerable<string> known)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentNullException.ThrowIfNull(known);

		string? best = null;
		var bestDistance = int.MaxValue;
		foreach (var name in known)
		{
			var distance = Distance(candidate, name);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = name;
			}
		}

		return bestDistance <= MaxDistance ? best : null;
	}

	/// <summary>Case-insensitive Levenshtein edit distance between <paramref name="a"/> and <paramref name="b"/> — classic single-row dynamic-programming form, O(a.Length * b.Length) time, O(min(a.Length, b.Length)) space.</summary>
	static int Distance(string a, string b)
	{
		if (a.Length < b.Length)
			(a, b) = (b, a);

		var previousRow = new int[b.Length + 1];
		for (var j = 0; j <= b.Length; j++)
			previousRow[j] = j;

		var currentRow = new int[b.Length + 1];
		for (var i = 1; i <= a.Length; i++)
		{
			currentRow[0] = i;
			for (var j = 1; j <= b.Length; j++)
			{
				var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
				currentRow[j] = Math.Min(Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1), previousRow[j - 1] + cost);
			}

			(previousRow, currentRow) = (currentRow, previousRow);
		}

		return previousRow[b.Length];
	}
}
