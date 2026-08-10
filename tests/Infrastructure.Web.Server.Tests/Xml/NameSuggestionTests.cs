using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class NameSuggestionTests
{
	[Fact]
	void Nearest_returns_the_exact_match_at_distance_zero() =>
		NameSuggestion.Nearest("birthDate", ["birthDate", "policyNumber"]).ShouldBe("birthDate");

	[Fact]
	void Nearest_returns_the_brief_literal_birthday_for_birthDate_case()
	{
		// "birthday" vs "birthDate": case-sensitive edit distance is 3 (the 'd'/'D' substitution plus
		// "ay"/"ate"), but case-insensitive it is exactly 2 — the MaxDistance boundary this case is
		// pinned to prove. Verified via an independent DP computation before writing this test.
		NameSuggestion.Nearest("birthday", ["birthDate", "policyNumber"]).ShouldBe("birthDate");
	}

	[Fact]
	void Nearest_returns_null_when_nothing_is_within_max_distance()
	{
		// "kitten"/"sitting" is the textbook distance-3 pair — one past MaxDistance.
		NameSuggestion.Nearest("kitten", ["sitting", "policyNumber"]).ShouldBeNull();
	}

	[Fact]
	void Nearest_returns_null_for_an_empty_known_set() =>
		NameSuggestion.Nearest("birthday", []).ShouldBeNull();

	[Fact]
	void Nearest_picks_the_closest_of_several_candidates() =>
		NameSuggestion.Nearest("cat", ["bat", "cats", "dog"]).ShouldBe("bat");

	[Fact]
	void Nearest_is_case_insensitive_for_an_otherwise_exact_match() =>
		NameSuggestion.Nearest("BIRTHDATE", ["birthDate"]).ShouldBe("birthDate");

	[Fact]
	void Nearest_throws_on_null_candidate() =>
		Should.Throw<ArgumentNullException>(() => NameSuggestion.Nearest(null!, ["x"]));

	[Fact]
	void Nearest_throws_on_null_known_set() =>
		Should.Throw<ArgumentNullException>(() => NameSuggestion.Nearest("x", null!));
}
