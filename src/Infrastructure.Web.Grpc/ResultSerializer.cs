using System.Globalization;
using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Reads a scalar <see cref="Result{T}"/> off the wire as <typeparamref name="T"/>'s own native
/// protobuf-net encoding wherever one exists — the same binary form a plain <typeparamref name="T"/>
/// field would use, no parsing, the binary is already typed. <see cref="Guid"/> is the platform's own
/// raw-bytes convention (<see cref="GuidWire"/>), unrelated to this type. <see cref="DateTimeOffset"/>
/// is the one genuine exception: protobuf-net has zero native support for it at any compatibility
/// level, so it rides a plain wire <c>string</c> carrying the exact §7 lexical form ("O" round-trip)
/// the JSON and XML legs already write — the same form <see cref="DateTimeOffsetSerializer"/> (the
/// bare type's registered wire law) emits, one wire form by construction —
/// funneled through <see cref="Parser.ParseRequired{T}"/> — the platform's one parsing door —
/// so a malformed value on this one type produces the platform's typed <see cref="Failure"/> rather
/// than either a thrown exception or an unrepresentable byte pattern. <see cref="Write"/> always
/// throws <see cref="InvalidOperationException"/>: <see cref="Result{T}"/> is a deserialization-only
/// type, and nothing downstream of an already-valid <typeparamref name="T"/> has legitimate business
/// round-tripping it back through the type that exists to validate untrusted input in the first place
/// — this holds regardless of state (success, failure, or default) and regardless of which native or
/// fallback encoding <typeparamref name="T"/> would otherwise read. <see cref="Result{T}"/>'s own
/// dispatch over the closed <see cref="ISpanParsable{TSelf}"/> taxonomy is <c>typeof</c>-branched and
/// JIT-eliminated per closed generic instantiation, the same pattern <c>Norse.Primitives.Parser</c>
/// itself uses — <see cref="Unsafe.As{TFrom,TTo}"/> is a sound identity reinterpret in each branch
/// because <typeparamref name="T"/> is statically the branch's concrete type there, never a real
/// layout coercion.
/// </summary>
/// <typeparam name="T">The validated value's type — one row of the platform's closed scalar taxonomy.</typeparam>
sealed class ResultSerializer<T> : ISerializer<Result<T>>, ISerializer<Result<T>?> where T : notnull, ISpanParsable<T>
{
	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | WireFeature();

	public Result<T> Read(ref ProtoReader.State state, Result<T> value)
	{
		if (typeof(T) == typeof(DateTimeOffset))
		{
			var text = state.ReadString(null) ?? string.Empty;
			var routed = Parser.ParseRequired<DateTimeOffset>(text, CultureInfo.InvariantCulture);
			return Unsafe.As<Result<DateTimeOffset>, Result<T>>(ref routed);
		}

		return new Success<T>(ReadScalar(ref state));
	}

	/// <exception cref="InvalidOperationException">Always.</exception>
	public void Write(ref ProtoWriter.State state, Result<T> value) =>
		throw new InvalidOperationException(ResultSerializers.DeserializationOnlyMessage);

	Result<T>? ISerializer<Result<T>?>.Read(ref ProtoReader.State state, Result<T>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">Always.</exception>
	void ISerializer<Result<T>?>.Write(ref ProtoWriter.State state, Result<T>? value) =>
		Write(ref state, value.GetValueOrDefault());

	static SerializerFeatures WireFeature()
	{
		if (typeof(T) == typeof(float))
			return SerializerFeatures.WireTypeFixed32;
		if (typeof(T) == typeof(double))
			return SerializerFeatures.WireTypeFixed64;
		if (typeof(T) == typeof(string) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Guid) ||
			typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(TimeSpan))
			return SerializerFeatures.WireTypeString;
		return SerializerFeatures.WireTypeVarint;
	}

	/// <summary>Native per-type read dispatch. Never called for <see cref="DateTimeOffset"/> — <see cref="Read"/> routes that one through the string+parser fallback before ever reaching here.</summary>
	static T ReadScalar(ref ProtoReader.State state)
	{
		if (typeof(T) == typeof(bool))
		{ var v = state.ReadBoolean(); return Unsafe.As<bool, T>(ref v); }
		if (typeof(T) == typeof(byte))
		{ var v = state.ReadByte(); return Unsafe.As<byte, T>(ref v); }
		if (typeof(T) == typeof(sbyte))
		{ var v = state.ReadSByte(); return Unsafe.As<sbyte, T>(ref v); }
		if (typeof(T) == typeof(short))
		{ var v = state.ReadInt16(); return Unsafe.As<short, T>(ref v); }
		if (typeof(T) == typeof(ushort))
		{ var v = state.ReadUInt16(); return Unsafe.As<ushort, T>(ref v); }
		if (typeof(T) == typeof(int))
		{ var v = state.ReadInt32(); return Unsafe.As<int, T>(ref v); }
		if (typeof(T) == typeof(uint))
		{ var v = state.ReadUInt32(); return Unsafe.As<uint, T>(ref v); }
		if (typeof(T) == typeof(long))
		{ var v = state.ReadInt64(); return Unsafe.As<long, T>(ref v); }
		if (typeof(T) == typeof(ulong))
		{ var v = state.ReadUInt64(); return Unsafe.As<ulong, T>(ref v); }
		if (typeof(T) == typeof(float))
		{ var v = state.ReadSingle(); return Unsafe.As<float, T>(ref v); }
		if (typeof(T) == typeof(double))
		{ var v = state.ReadDouble(); return Unsafe.As<double, T>(ref v); }
		if (typeof(T) == typeof(decimal))
		{ var v = BclHelpers.ReadDecimalString(ref state); return Unsafe.As<decimal, T>(ref v); }
		if (typeof(T) == typeof(char))
		{ var v = (char)state.ReadUInt16(); return Unsafe.As<char, T>(ref v); }
		if (typeof(T) == typeof(string))
		{ var v = state.ReadString(null) ?? string.Empty; return Unsafe.As<string, T>(ref v); }
		if (typeof(T) == typeof(Guid))
		{ var v = GuidWire.Read(ref state); return Unsafe.As<Guid, T>(ref v); }
		if (typeof(T) == typeof(DateOnly))
		{ var v = BclHelpers.ReadDateOnly(ref state); return Unsafe.As<DateOnly, T>(ref v); }
		if (typeof(T) == typeof(TimeOnly))
		{ var v = BclHelpers.ReadTimeOnly(ref state); return Unsafe.As<TimeOnly, T>(ref v); }
		if (typeof(T) == typeof(DateTime))
		{ var v = BclHelpers.ReadTimestamp(ref state); return Unsafe.As<DateTime, T>(ref v); }
		if (typeof(T) == typeof(TimeSpan))
		{ var v = BclHelpers.ReadDuration(ref state); return Unsafe.As<TimeSpan, T>(ref v); }
		throw new NotSupportedException($"No Result<{typeof(T).Name}> wire mapping registered.");
	}
}
