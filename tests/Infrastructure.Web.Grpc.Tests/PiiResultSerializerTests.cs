using Norse.Primitives;
using Norse.Primitives.Pii;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc.Tests;

/// <summary>
///     <see cref="PiiResultSerializer{T}" /> — the PII rows of the Result wire law. Wire form is a
///     plain string carrying the scalar's canonical <c>WireValue</c>, so the plain-field mirror
///     technique from <see cref="ResultSerializerTests" /> applies directly: bytes produced by a
///     plain <c>string</c> field deserialize through the stamped field, and a stamped success
///     serializes to the identical bytes. Read is the parse event — malformed wire text produces the
///     typed <see cref="Failure" />, never a throw; a failed or default stamp is illegal to write.
/// </summary>
public sealed class PiiResultSerializerTests
{
	const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

	[Fact]
	void Round_trips_a_success_Result_of_EmailAddress_as_the_plain_wire_string()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainPiiEnvelope { Email = "buvy@example.com" });

		var back = TestModel.Deserialize<PiiEnvelope>(model, payload);

		back.Email.TryGetValue(out Success<EmailAddress> email).ShouldBeTrue();
		email.Value.WireValue.ShouldBe("buvy@example.com");
	}

	[Fact]
	void Write_unwraps_a_success_to_the_identical_plain_field_bytes()
	{
		var model = TestModel.Create();
		EmailAddress.Parse("buvy@example.com").TryGetValue(out Success<EmailAddress> parsed).ShouldBeTrue();

		var stamped = TestModel.Serialize(model, new PiiEnvelope { Email = new Success<EmailAddress>(parsed.Value) });
		var plain = TestModel.Serialize(model, new PlainPiiEnvelope { Email = "buvy@example.com" });

		stamped.ShouldBe(plain);
	}

	[Fact]
	void Read_restamps_malformed_wire_text_as_a_typed_Failure()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainPiiEnvelope { Email = "not-an-email" });

		var back = TestModel.Deserialize<PiiEnvelope>(model, payload);

		back.Email.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void An_absent_field_deserializes_to_the_default_stamp()
	{
		var model = TestModel.Create();
		var back = TestModel.Deserialize<PiiEnvelope>(model, TestModel.Serialize(model, new EmptyEnvelope()));

		back.Email.HasValue.ShouldBeFalse();
	}

	[Fact]
	void A_failed_stamp_is_illegal_to_write()
	{
		var model = TestModel.Create();
		EmailAddress.Parse("garbage").TryGetValue(out Failure _).ShouldBeTrue();
		var envelope = new PiiEnvelope { Email = EmailAddress.Parse("garbage") };

		Should.Throw<InvalidOperationException>(() => TestModel.Serialize(model, envelope))
			.Message.ShouldContain(IllegalWriteMessage);
	}

	[Fact]
	void A_default_stamp_is_illegal_to_write()
	{
		var model = TestModel.Create();

		Should.Throw<InvalidOperationException>(() => TestModel.Serialize(model, new PiiEnvelope()))
			.Message.ShouldContain(IllegalWriteMessage);
	}

	[Fact]
	void Round_trips_the_remaining_taxonomy_rows_PersonalName_PhoneNumber_BirthDate()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainPiiTrioEnvelope { Name = "Brian", Phone = "+15125550143", Born = "1980-01-02" });

		var back = TestModel.Deserialize<PiiTrioEnvelope>(model, payload);

		back.Name.TryGetValue(out Success<PersonalName> name).ShouldBeTrue();
		name.Value.WireValue.ShouldBe("Brian");
		back.Phone.TryGetValue(out Success<PhoneNumber> phone).ShouldBeTrue();
		phone.Value.WireValue.ShouldBe("+15125550143");
		back.Born.TryGetValue(out Success<BirthDate> born).ShouldBeTrue();
		born.Value.WireValue.ShouldBe("1980-01-02");
	}
}

[ProtoContract]
public sealed class PiiEnvelope
{
	[ProtoMember(1)]
	public Result<EmailAddress> Email { get; set; }
}

[ProtoContract]
public sealed class PlainPiiEnvelope
{
	[ProtoMember(1)]
	public string? Email { get; set; }
}

[ProtoContract]
public sealed class EmptyEnvelope;

[ProtoContract]
public sealed class PiiTrioEnvelope
{
	[ProtoMember(1)]
	public Result<PersonalName> Name { get; set; }

	[ProtoMember(2)]
	public Result<PhoneNumber> Phone { get; set; }

	[ProtoMember(3)]
	public Result<BirthDate> Born { get; set; }
}

[ProtoContract]
public sealed class PlainPiiTrioEnvelope
{
	[ProtoMember(1)]
	public string? Name { get; set; }

	[ProtoMember(2)]
	public string? Phone { get; set; }

	[ProtoMember(3)]
	public string? Born { get; set; }
}
