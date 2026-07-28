using Norse.Primitives.Identifiers;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class DeterministicGuidSerializerTests
{
	// The RFC 9562 §A.4 example UUIDv5 (DNS namespace, "www.example.com").
	static readonly Guid _knownV5 = new("2ed6657d-e927-568b-95e1-2665a8aea6a2");
	const string KnownWireHex = "0A102ED6657DE927568B95E12665A8AEA6A2";

	[Fact]
	void Serializes_as_sixteen_rfc_9562_bytes()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new DeterministicGuidEnvelope { Id = new(_knownV5) });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Round_trips_equal_to_the_original()
	{
		var model = TestModel.Create();
		DeterministicGuid original = new(_knownV5);
		var payload = TestModel.Serialize(model, new DeterministicGuidEnvelope { Id = original });
		TestModel.Deserialize<DeterministicGuidEnvelope>(model, payload).Id.ShouldBe(original);
	}

	[Fact]
	void Round_trips_a_nullable_member()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableDeterministicGuidEnvelope { Id = new(_knownV5) });
		TestModel.Deserialize<NullableDeterministicGuidEnvelope>(model, payload)
			.Id.ShouldBe(new DeterministicGuid(_knownV5));
	}

	[Fact]
	void Round_trips_a_null_nullable_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableDeterministicGuidEnvelope());
		TestModel.Deserialize<NullableDeterministicGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Throws_on_a_truncated_payload()
	{
		var model = TestModel.Create();
		byte[] truncated = [0x0A, 0x0F, .. new byte[15]];
		Should.Throw<InvalidDataException>(() =>
			TestModel.Deserialize<DeterministicGuidEnvelope>(model, truncated));
	}

	[Fact]
	void Throws_on_sixteen_bytes_that_are_not_a_version_5_uuid()
	{
		var model = TestModel.Create();
		// The §A.6 v7 example: valid UUID bits, wrong version for DeterministicGuid.
		byte[] v7Payload = [0x0A, 0x10, .. Convert.FromHexString("017F22E279B07CC398C4DC0C0C07398F")];
		Should.Throw<ArgumentException>(() =>
			TestModel.Deserialize<DeterministicGuidEnvelope>(model, v7Payload));
	}
}

[ProtoContract]
public sealed class DeterministicGuidEnvelope
{
	[ProtoMember(1)]
	public DeterministicGuid Id { get; set; }
}

[ProtoContract]
public sealed class NullableDeterministicGuidEnvelope
{
	[ProtoMember(1)]
	public DeterministicGuid? Id { get; set; }
}
