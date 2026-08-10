using System.Globalization;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     The general (non-<see cref="Result{T}" />-wrapped) <see cref="DateTimeOffset" /> wire law — the §7
///     row protobuf-net cannot represent natively at any compatibility level (a bare
///     <c>[ProtoMember] DateTimeOffset</c> throws "No serializer defined" on an unregistered model). Wire
///     form: a plain <c>string</c> carrying the §7 lexical form ("O" round-trip, invariant) — the same
///     text the JSON and XML legs emit, and exactly what <see cref="ResultSerializer{T}" />'s
///     <see cref="DateTimeOffset" /> branch reads, so a bare response field written here and a
///     <see cref="Result{T}" />-wrapped request member read there share one wire form by construction —
///     never by a test fixture's registration order. Read funnels through
///     <see cref="Parser.ParseRequired{T}" />, the platform's one parsing door; a malformed wire string on
///     a bare field is a first-party producer bug (binary clients drive generated stubs, spec §1.3), so it
///     throws a <see cref="ProtoException" /> loudly rather than capturing failure-as-data — there is no
///     <see cref="Result{T}" /> here to carry it.
/// </summary>
sealed class DateTimeOffsetSerializer : ISerializer<DateTimeOffset>, ISerializer<DateTimeOffset?>
{
	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

	public DateTimeOffset Read(ref ProtoReader.State state, DateTimeOffset value)
	{
		var text = state.ReadString() ?? string.Empty;
		return Parser.ParseRequired<DateTimeOffset>(text, CultureInfo.InvariantCulture) switch
		{
			Success<DateTimeOffset>(var parsed) => parsed,
			Failure failure => throw new ProtoException($"cannot parse '{failure.Input}' as {failure.ExpectedType}")
		};
	}

	public void Write(ref ProtoWriter.State state, DateTimeOffset value) =>
		state.WriteString(value.ToString("O", CultureInfo.InvariantCulture));

	DateTimeOffset? ISerializer<DateTimeOffset?>.Read(ref ProtoReader.State state, DateTimeOffset? value) =>
		Read(ref state, value.GetValueOrDefault());

	void ISerializer<DateTimeOffset?>.Write(ref ProtoWriter.State state, DateTimeOffset? value) =>
		Write(ref state, value.GetValueOrDefault());
}
