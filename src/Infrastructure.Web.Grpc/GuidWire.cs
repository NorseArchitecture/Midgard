using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     Shared read/write of the canonical identifier payload: a bare <c>bytes</c> field of 16 bytes in RFC 9562
///     order.
/// </summary>
static class GuidWire
{
	internal static Guid Read(ref ProtoReader.State state)
	{
		var bytes = state.AppendBytes(null);
		return bytes.Length == 16 ?
			new Guid(bytes, bigEndian: true) :
			throw new InvalidDataException($"Expected a 16-byte RFC 9562 UUID payload, got {bytes.Length} bytes.");
	}

	internal static void Write(ref ProtoWriter.State state, in Guid value)
	{
		var bytes = new byte[16];
		value.TryWriteBytes(bytes, bigEndian: true, out _);
		state.WriteBytes(bytes);
	}
}
