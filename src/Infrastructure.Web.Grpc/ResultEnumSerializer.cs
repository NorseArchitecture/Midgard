using System.Globalization;
using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// The enum row of the §7 scalar taxonomy on the gRPC leg — the one row
/// <see cref="ResultSerializer{T}"/> structurally cannot carry (no enum satisfies
/// <see cref="ISpanParsable{TSelf}"/>). The wire form is protobuf-net's own native enum encoding: the
/// underlying integral as a varint — names never touch the binary channel, so the text channels' case
/// styling and generated name tables are irrelevant here. Undefined values are the binary channel's
/// counterpart to the text channels' undefined-enum-name accumulable (spec §6.5/§8.1): a varint
/// carrying no defined member — or, for a <see cref="FlagsAttribute"/> enum, leftover bits outside the
/// defined set — is representable on the wire, so it funnels to the platform's one typed
/// <see cref="Failure"/> exactly as a malformed <see cref="DateTimeOffset"/> wire string does, never a
/// throw. <see cref="Write(ref ProtoWriter.State, Result{TEnum})"/> always throws: one deserialization-only law, every row,
/// every channel. Absent-field semantics are protobuf-net's own, identical to every other row:
/// <c>default(Result&lt;TEnum&gt;)</c> for required, <see langword="null"/> for optional, caught
/// downstream by <c>ResultRules</c> validation (spec §9.3).
/// </summary>
/// <typeparam name="TEnum">The contract's enum type — user-declared, so the set is open; registration is discovery-driven (see <see cref="ResultSerializers"/>).</typeparam>
sealed class ResultEnumSerializer<TEnum> : ISerializer<Result<TEnum>>, ISerializer<Result<TEnum>?> where TEnum : unmanaged, Enum
{
	// One-time per closed generic at type initialization — the "one-time startup wiring" allowance;
	// never per-read reflection.
	static readonly bool _isFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);
	static readonly long _definedBits = ComputeDefinedBits();

	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeVarint;

	public Result<TEnum> Read(ref ProtoReader.State state, Result<TEnum> value)
	{
		var raw = state.ReadInt64();
		var candidate = FromBits(raw);
		var defined = _isFlags ? (ToBits(candidate) & ~_definedBits) == 0 : Enum.IsDefined(candidate);
		if (defined)
			return new Success<TEnum>(candidate);
		return new Failure(ParseFailure.Malformed, raw.ToString(CultureInfo.InvariantCulture), typeof(TEnum).Name);
	}

	/// <exception cref="InvalidOperationException">Always.</exception>
	public void Write(ref ProtoWriter.State state, Result<TEnum> value) =>
		throw new InvalidOperationException(ResultSerializers.DeserializationOnlyMessage);

	Result<TEnum>? ISerializer<Result<TEnum>?>.Read(ref ProtoReader.State state, Result<TEnum>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">Always.</exception>
	void ISerializer<Result<TEnum>?>.Write(ref ProtoWriter.State state, Result<TEnum>? value) =>
		Write(ref state, value.GetValueOrDefault());

	static long ComputeDefinedBits()
	{
		long bits = 0;
		foreach (var defined in Enum.GetValues<TEnum>())
			bits |= ToBits(defined);
		return bits;
	}

	// Unsafe.As identity reinterprets between the enum and its underlying integral — sound per branch
	// because Unsafe.SizeOf<TEnum> is a JIT constant, the same closed-dispatch pattern
	// ResultSerializer<T> uses. Unsigned widening keeps flag masks bit-faithful regardless of the
	// underlying type's signedness.
	static long ToBits(TEnum value) => Unsafe.SizeOf<TEnum>() switch
	{
		1 => Unsafe.As<TEnum, byte>(ref value),
		2 => Unsafe.As<TEnum, ushort>(ref value),
		4 => Unsafe.As<TEnum, uint>(ref value),
		_ => Unsafe.As<TEnum, long>(ref value),
	};

	static TEnum FromBits(long raw)
	{
		if (Unsafe.SizeOf<TEnum>() == 1)
		{ var v = (byte)raw; return Unsafe.As<byte, TEnum>(ref v); }
		if (Unsafe.SizeOf<TEnum>() == 2)
		{ var v = (ushort)raw; return Unsafe.As<ushort, TEnum>(ref v); }
		if (Unsafe.SizeOf<TEnum>() == 4)
		{ var v = (uint)raw; return Unsafe.As<uint, TEnum>(ref v); }
		return Unsafe.As<long, TEnum>(ref raw);
	}
}
