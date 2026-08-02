using System.Globalization;
using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Puts a scalar <see cref="Result{T}"/> on the wire as a plain protobuf <c>string</c> field,
/// presence-tracked: <see cref="Write"/> always throws — <see cref="Result{T}"/> is a
/// deserialization-only type, and nothing downstream of a valid <typeparamref name="T"/> has
/// legitimate business round-tripping it back through the type that exists to validate untrusted
/// input in the first place. <see cref="Read"/> funnels the wire string through
/// <see cref="Parser.ParseRequired{T}"/> — the same funnel the JSON and XML legs use — so a
/// genuinely malformed string on the wire (e.g. a non-.NET producer sending <c>"2026-02-30"</c>)
/// produces the platform's one typed <see cref="Failure"/>, never a structurally-unrepresentable
/// native binary decode. A present field always parses; an absent field is protobuf-net's own
/// <see cref="Nullable{T}"/>/default-on-missing-tag behavior and never reaches this type at all —
/// <see cref="Read"/> is only ever invoked for a tag protobuf-net actually found on the wire.
/// <see cref="Result{T}"/>'s own dispatch over the closed <see cref="ISpanParsable{TSelf}"/>
/// taxonomy is <c>typeof</c>-branched and JIT-eliminated per closed generic instantiation, the same
/// pattern <c>Norse.Primitives.Parser</c> itself uses — <see cref="Unsafe.As{TFrom,TTo}"/> is a sound
/// identity reinterpret in the <see cref="string"/> branch because <typeparamref name="T"/> is
/// statically <see cref="string"/> there, never a real layout coercion.
/// </summary>
/// <typeparam name="T">The validated value's type — one row of the platform's closed scalar taxonomy.</typeparam>
sealed class ResultSerializer<T> : ISerializer<Result<T>>, ISerializer<Result<T>?> where T : notnull, ISpanParsable<T>
{
	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeString;

	public Result<T> Read(ref ProtoReader.State state, Result<T> value)
	{
		var text = state.ReadString(null) ?? string.Empty;

		// Present is content, always — even an empty wire string is a present value, not "required
		// missing" (there is no absent-vs-empty distinction to preserve here: an absent field never
		// reaches this method at all, per the class remarks). string has nothing to parse — the wire
		// text IS the domain value — so it bypasses Parser.ParseRequired<string> exactly the way the
		// JSON and XML legs' own string carve-outs do; every other type in the taxonomy routes through
		// the parser.
		if (typeof(T) == typeof(string))
		{
			Result<string> routed = new Success<string>(text);
			return Unsafe.As<Result<string>, Result<T>>(ref routed);
		}

		return Parser.ParseRequired<T>(text, CultureInfo.InvariantCulture);
	}

	/// <exception cref="InvalidOperationException">Always.</exception>
	public void Write(ref ProtoWriter.State state, Result<T> value) =>
		throw new InvalidOperationException("Result<T> is a deserialization-only type and must never be written");

	Result<T>? ISerializer<Result<T>?>.Read(ref ProtoReader.State state, Result<T>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">Always.</exception>
	void ISerializer<Result<T>?>.Write(ref ProtoWriter.State state, Result<T>? value) =>
		Write(ref state, value.GetValueOrDefault());
}
