using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     Puts <see cref="DeterministicGuid" /> on the wire as the canonical 16-byte RFC 9562 <c>bytes</c>
///     payload; reads re-validate the version-5 bits via the wrapping constructor and fail loudly on garbage.
/// </summary>
sealed class DeterministicGuidSerializer : ISerializer<DeterministicGuid>, ISerializer<DeterministicGuid?>
{
	public SerializerFeatures Features =>
		SerializerFeatures.WireTypeString | SerializerFeatures.CategoryScalar;

	public DeterministicGuid Read(ref ProtoReader.State state, DeterministicGuid value) =>
		new(GuidWire.Read(ref state));

	public void Write(ref ProtoWriter.State state, DeterministicGuid value) =>
		GuidWire.Write(ref state, value.Value);

	DeterministicGuid? ISerializer<DeterministicGuid?>.Read(ref ProtoReader.State state, DeterministicGuid? value) =>
		Read(ref state, value.GetValueOrDefault());

	void ISerializer<DeterministicGuid?>.Write(ref ProtoWriter.State state, DeterministicGuid? value) =>
		Write(ref state, value.GetValueOrDefault());
}
