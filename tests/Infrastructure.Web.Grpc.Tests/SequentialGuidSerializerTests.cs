using Norse.Primitives.Identifiers;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class SequentialGuidSerializerTests
{
	// The RFC 9562 §A.6 example UUIDv7.
	static readonly Guid _knownV7 = new("017f22e2-79b0-7cc3-98c4-dc0c0c07398f");
	const string KnownWireHex = "0A10017F22E279B07CC398C4DC0C0C07398F";

	[Fact]
	void Serializes_as_sixteen_rfc_9562_bytes()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model,
			new SequentialGuidEnvelope { Id = new(_knownV7, GuidByteOrder.Rfc9562) });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Normalizes_a_sql_ordered_value_to_rfc_order_on_the_wire()
	{
		var model = TestModel.Create();
		var sqlOrdered = new SequentialGuid(_knownV7, GuidByteOrder.Rfc9562).ToSqlOrder();
		var payload = TestModel.Serialize(model, new SequentialGuidEnvelope { Id = sqlOrdered });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Rehydrates_in_rfc_order_and_equal_to_the_original()
	{
		var model = TestModel.Create();
		SequentialGuid original = new(_knownV7, GuidByteOrder.Rfc9562);
		var payload = TestModel.Serialize(model, new SequentialGuidEnvelope { Id = original });
		var back = TestModel.Deserialize<SequentialGuidEnvelope>(model, payload).Id;
		back.Order.ShouldBe(GuidByteOrder.Rfc9562);
		back.ShouldBe(original);
	}

	[Fact]
	void Round_trips_a_nullable_member()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model,
			new NullableSequentialGuidEnvelope { Id = new(_knownV7, GuidByteOrder.Rfc9562) });
		TestModel.Deserialize<NullableSequentialGuidEnvelope>(model, payload)
			.Id.ShouldBe(new SequentialGuid(_knownV7, GuidByteOrder.Rfc9562));
	}

	[Fact]
	void Round_trips_a_null_nullable_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableSequentialGuidEnvelope());
		TestModel.Deserialize<NullableSequentialGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Throws_on_a_truncated_payload()
	{
		var model = TestModel.Create();
		byte[] truncated = [0x0A, 0x0F, .. new byte[15]];
		Should.Throw<InvalidDataException>(() =>
			TestModel.Deserialize<SequentialGuidEnvelope>(model, truncated));
	}

	[Fact]
	void Throws_on_sixteen_bytes_that_are_not_a_version_7_uuid()
	{
		var model = TestModel.Create();
		byte[] allZero = [0x0A, 0x10, .. new byte[16]];
		Should.Throw<ArgumentException>(() =>
			TestModel.Deserialize<SequentialGuidEnvelope>(model, allZero));
	}
}

[ProtoContract]
public sealed class SequentialGuidEnvelope
{
	[ProtoMember(1)]
	public SequentialGuid Id { get; set; }
}

[ProtoContract]
public sealed class NullableSequentialGuidEnvelope
{
	[ProtoMember(1)]
	public SequentialGuid? Id { get; set; }
}
