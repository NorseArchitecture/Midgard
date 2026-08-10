using System.Runtime.Serialization;
using System.Text.Json;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

[DataContract]
sealed record OptInFixture
{
	[DataMember(Order = 1)] public string Name { get; set; } = "";
	public string Shadow { get; set; } = ""; // undecorated — must not exist to STJ
}

sealed record PlainFixture
{
	public string Name { get; set; } = ""; // no [DataContract] — default STJ behavior holds
}

public sealed class OptInContractModifierTests
{
	[Fact]
	void A_non_DataMember_property_on_a_DataContract_type_does_not_serialize()
	{
		var options = NorseJsonTestOptions.Create();
		var json = JsonSerializer.Serialize(new OptInFixture { Name = "Alice", Shadow = "leak" }, options);

		json.ShouldBe("""{"name":"Alice"}""");
	}

	[Fact]
	void An_incoming_member_naming_a_stripped_property_dies_under_the_unmapped_ratchet()
	{
		var options = NorseJsonTestOptions.Create();

		Should.Throw<JsonException>(() =>
			JsonSerializer.Deserialize<OptInFixture>("""{"name":"Alice","shadow":"second door"}""", options));
	}

	[Fact]
	void A_type_without_DataContract_keeps_default_membership()
	{
		var options = NorseJsonTestOptions.Create();
		JsonSerializer.Serialize(new PlainFixture { Name = "Alice" }, options).ShouldBe("""{"name":"Alice"}""");
	}
}
