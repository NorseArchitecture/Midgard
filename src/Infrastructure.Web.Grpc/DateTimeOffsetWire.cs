using System.Buffers.Binary;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Shared read/write of the <see cref="DateTimeOffset"/> wire payload. protobuf-net has no native
/// serializer for this type at any compatibility level — verified: a plain <c>DateTimeOffset</c>
/// <see cref="ProtoBuf.ProtoMemberAttribute"/> member throws <see cref="InvalidOperationException"/>
/// ("No serializer defined for type: System.DateTimeOffset") at model-build time, unlike every other
/// row in the platform's closed scalar taxonomy. This is therefore a first-of-its-kind wire
/// convention for the type, not a mirror of an existing default: a bare 10-byte payload —
/// <see cref="DateTimeOffset.Ticks"/> (the wall-clock reading the BCL's own two-argument constructor
/// expects, big-endian <see langword="long"/>) followed by <see cref="DateTimeOffset.Offset"/> in
/// whole minutes (big-endian <see langword="short"/> — the BCL itself constrains
/// <see cref="DateTimeOffset"/> offsets to whole minutes, so this never loses precision). The shape
/// mirrors <see cref="GuidWire"/>'s own raw-bytes-scalar convention rather than a tagged sub-message.
/// </summary>
static class DateTimeOffsetWire
{
	internal static DateTimeOffset Read(ref ProtoReader.State state)
	{
		var bytes = state.AppendBytes(null);
		if (bytes.Length != 10)
			throw new InvalidDataException($"Expected a 10-byte DateTimeOffset payload (8-byte ticks + 2-byte offset minutes), got {bytes.Length} bytes.");
		var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(0, 8));
		var offsetMinutes = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(8, 2));
		return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
	}

	internal static void Write(ref ProtoWriter.State state, in DateTimeOffset value)
	{
		var bytes = new byte[10];
		BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(0, 8), value.Ticks);
		BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(8, 2), (short)value.Offset.TotalMinutes);
		state.WriteBytes(bytes);
	}
}
