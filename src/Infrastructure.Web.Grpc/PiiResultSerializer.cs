using Norse.Primitives;
using Norse.Primitives.Pii;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     Reads a PII-scalar <see cref="Result{T}" /> off the wire as a plain <c>string</c> carrying the
///     scalar's canonical <see cref="IPiiScalar{TSelf}.WireValue" /> form, funneled through
///     <typeparamref name="T" />'s own <c>Parse</c> — the PII taxonomy's one parsing door — so a
///     malformed value produces the typed <see cref="Failure" />, never a throw. Mirrors
///     <see cref="ResultSerializer{T}" />'s <see cref="DateTimeOffset" /> branch: protobuf-net has no
///     native encoding for these types, and the lexical form is already the platform's canonical wire
///     representation. Write unwraps a success to <see cref="IPiiScalar{TSelf}.WireValue" /> — the
///     deliberate plaintext egress, the union never rides the wire — and a failed or default
///     <see cref="Result{T}" /> is illegal to write, same one law as every other leg.
/// </summary>
/// <typeparam name="T">The PII scalar's type — one row of the forge's PII taxonomy.</typeparam>
sealed class PiiResultSerializer<T> : ISerializer<Result<T>>, ISerializer<Result<T>?> where T : struct, IPiiScalar<T>
{
	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

	public Result<T> Read(ref ProtoReader.State state, Result<T> value) =>
		T.Parse(state.ReadString() ?? string.Empty);

	/// <summary>
	///     Unwraps a success to the scalar's canonical wire string — deliberate
	///     <see cref="IPiiScalar{TSelf}.WireValue" /> egress, never the masked rendering.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> is a failure or default; both are illegal to
	///     write.
	/// </exception>
	public void Write(ref ProtoWriter.State state, Result<T> value)
	{
		if (!value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException(ResultSerializers.IllegalWriteMessage);
		state.WriteString(success.Value.WireValue);
	}

	Result<T>? ISerializer<Result<T>?>.Read(ref ProtoReader.State state, Result<T>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> is present but a failure or default; both are
	///     illegal to write.
	/// </exception>
	void ISerializer<Result<T>?>.Write(ref ProtoWriter.State state, Result<T>? value) =>
		Write(ref state, value.GetValueOrDefault());
}
