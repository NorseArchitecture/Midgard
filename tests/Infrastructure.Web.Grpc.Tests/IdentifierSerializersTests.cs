using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class IdentifierSerializersTests
{
	static readonly Guid _knownGuid = new("12345678-9abc-def0-1234-56789abcdef0");
	const string KnownWireHex = "0A10123456789ABCDEF0123456789ABCDEF0";

	[Fact]
	void Serializes_a_Guid_member_as_sixteen_rfc_9562_bytes_on_an_auto_discovered_type()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = _knownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Matches_protobuf_nets_own_level_300_fixed_size_form_bit_for_bit()
	{
		var reference = RuntimeTypeModel.Create();
		reference.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		var expected = TestModel.Serialize(reference, new FixedSizeGuidEnvelope { Id = _knownGuid });
		var actual = TestModel.Serialize(TestModel.Create(), new GuidEnvelope { Id = _knownGuid });
		actual.ShouldBe(expected);
	}

	[Fact]
	void Sweeps_a_type_added_explicitly_to_the_model()
	{
		var model = TestModel.Create();
		model.Add(typeof(GuidEnvelope));
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = _knownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Sweeps_nullable_Guid_members()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableGuidEnvelope { Id = _knownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Round_trips_a_null_nullable_Guid_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableGuidEnvelope());
		TestModel.Deserialize<NullableGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Round_trips_Guid_Empty()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = Guid.Empty });
		TestModel.Deserialize<GuidEnvelope>(model, payload).Id.ShouldBe(Guid.Empty);
	}

	static readonly DateTime _knownInstant = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
	const decimal KnownAmount = 1234.56m;

	[Fact]
	void Applies_level_300_semantics_per_member_without_touching_the_model_default()
	{
		var reference = RuntimeTypeModel.Create();
		reference.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		var expected = TestModel.Serialize(reference,
			new Level300Envelope { When = _knownInstant, Amount = KnownAmount });
		var model = TestModel.Create();
		model.DefaultCompatibilityLevel.ShouldBe(CompatibilityLevel.Level200);
		var actual = TestModel.Serialize(model,
			new Level300Envelope { When = _knownInstant, Amount = KnownAmount });
		actual.ShouldBe(expected);
	}

	[Fact]
	void Applies_the_wire_law_on_the_default_model()
	{
		Should.NotThrow(() => IdentifierSerializers.Register(RuntimeTypeModel.Default));
		var payload = TestModel.Serialize(RuntimeTypeModel.Default, new GuidEnvelope { Id = _knownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Registers_idempotently_when_called_twice_on_one_model()
	{
		var model = TestModel.Create();
		Should.NotThrow(() => IdentifierSerializers.Register(model));
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = _knownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Renders_Guid_members_as_bytes_fields_in_the_schema()
	{
		var model = TestModel.Create();
		model.Add(typeof(GuidEnvelope));
		model.GetSchema(typeof(GuidEnvelope), ProtoSyntax.Proto3).ShouldContain("bytes Id = 1;");
	}

	[Fact]
	async Task Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch()
	{
		// Regression test for the race filed 2026-08-03
		// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md): the old flag-first
		// guard let a second caller observe "claimed" and return immediately while the first caller's
		// registration was still mid-flight. Every concurrent first-touch caller, across many fresh
		// models, must see SequentialGuid registered by the time its OWN call to Register returns -- not
		// just eventually, and not just "no exception was thrown".
		const int ModelCount = 500;
		const int CallersPerModel = 8;

		await Task.WhenAll(Enumerable.Range(0, ModelCount).Select(async _ =>
		{
			var model = RuntimeTypeModel.Create();
			using Barrier barrier = new(CallersPerModel);

			await Task.WhenAll(Enumerable.Range(0, CallersPerModel).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				IdentifierSerializers.Register(model);
				model.IsDefined(typeof(SequentialGuid)).ShouldBeTrue();
			})));
		}));
	}
}

[ProtoContract]
public sealed class GuidEnvelope
{
	[ProtoMember(1)]
	public Guid Id { get; set; }
}

[ProtoContract]
public sealed class NullableGuidEnvelope
{
	[ProtoMember(1)]
	public Guid? Id { get; set; }
}

[ProtoContract]
public sealed class FixedSizeGuidEnvelope
{
	[ProtoMember(1, DataFormat = DataFormat.FixedSize)]
	public Guid Id { get; set; }
}

[ProtoContract]
public sealed class Level300Envelope
{
	[ProtoMember(1)]
	public DateTime When { get; set; }
	[ProtoMember(2)]
	public decimal Amount { get; set; }
}
