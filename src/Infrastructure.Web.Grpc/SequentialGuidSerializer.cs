using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Puts <see cref="SequentialGuid"/> on the wire as the canonical 16-byte RFC 9562 <c>bytes</c> payload:
/// writes normalize to RFC order (SQL Server order never crosses the wire), reads re-validate the
/// version-7 bits and rehydrate tagged <see cref="GuidByteOrder.Rfc9562"/>.
/// </summary>
sealed class SequentialGuidSerializer : ISerializer<SequentialGuid>, ISerializer<SequentialGuid?>
{
	public SerializerFeatures Features =>
		SerializerFeatures.WireTypeString | SerializerFeatures.CategoryScalar;

	public SequentialGuid Read(ref ProtoReader.State state, SequentialGuid value) =>
		new(GuidWire.Read(ref state), GuidByteOrder.Rfc9562);

	public void Write(ref ProtoWriter.State state, SequentialGuid value) =>
		GuidWire.Write(ref state, value.ToRfcOrder().Value);

	SequentialGuid? ISerializer<SequentialGuid?>.Read(ref ProtoReader.State state, SequentialGuid? value) =>
		Read(ref state, value.GetValueOrDefault());

	void ISerializer<SequentialGuid?>.Write(ref ProtoWriter.State state, SequentialGuid? value) =>
		Write(ref state, value.GetValueOrDefault());
}
