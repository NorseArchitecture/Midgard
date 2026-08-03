using System.Runtime.CompilerServices;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// The one enum name/value mechanism generated XML shapes, JSON converters, and the OpenAPI
/// transformer all consume — a linear scan over an <see cref="EnumNameTable"/>, never a dictionary
/// (tables are small: one row per defined member). Deliberately <see langword="public"/>, not
/// <see langword="internal sealed"/>: called from generated code and converters in host compilations
/// (other repos, later tasks), so it must be visible outside this assembly.
/// </summary>
public static class EnumLexical
{
	/// <summary>Renders <paramref name="value"/> in the style at <paramref name="styleIndex"/> of <paramref name="table"/>.</summary>
	/// <exception cref="InvalidOperationException"><paramref name="value"/> is not a defined member of <paramref name="table"/>.</exception>
	public static string Format<TEnum>(EnumNameTable table, TEnum value, int styleIndex) where TEnum : unmanaged, Enum
	{
		var bits = ToBits(value);
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			if (table.Value(memberIndex) == bits)
				return table.Name(memberIndex, styleIndex);
		}

		throw new InvalidOperationException($"'{value}' is an undefined value of '{table.EnumType}' and is illegal to write.");
	}

	/// <summary>Parses <paramref name="content"/> against the style at <paramref name="styleIndex"/> of <paramref name="table"/>.</summary>
	/// <returns>
	/// A success carrying the matched member on an exact name match; otherwise a
	/// <see cref="ParseFailure.Malformed"/> failure — wrong case, off-list, and empty content all miss
	/// the same way, since <c>""</c> is content, never absence, at this layer.
	/// </returns>
	public static Result<TEnum> Parse<TEnum>(EnumNameTable table, string content, int styleIndex) where TEnum : unmanaged, Enum
	{
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			if (table.Name(memberIndex, styleIndex) == content)
				return FromBits<TEnum>(table.Value(memberIndex));
		}

		return new Failure(ParseFailure.Malformed, content, table.TypeName);
	}

	// Twinned with ResultEnumSerializer<TEnum>.ToBits (Infrastructure.Web.Grpc, different assembly —
	// deliberately not shared): Unsafe.As identity reinterprets between the enum and its underlying
	// integral, sound per branch because Unsafe.SizeOf<TEnum> is a JIT constant for a closed generic.
	static long ToBits<TEnum>(TEnum value) where TEnum : unmanaged, Enum => Unsafe.SizeOf<TEnum>() switch
	{
		1 => Unsafe.As<TEnum, byte>(ref value),
		2 => Unsafe.As<TEnum, ushort>(ref value),
		4 => Unsafe.As<TEnum, uint>(ref value),
		_ => Unsafe.As<TEnum, long>(ref value),
	};

	// Twinned with ResultEnumSerializer<TEnum>.FromBits (Infrastructure.Web.Grpc, different assembly —
	// deliberately not shared): the inverse Unsafe.As identity reinterpret, narrowing the long back to
	// the enum's own storage width before reinterpreting.
	static TEnum FromBits<TEnum>(long raw) where TEnum : unmanaged, Enum
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
