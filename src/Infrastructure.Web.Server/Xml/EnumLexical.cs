using System.Runtime.CompilerServices;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The one enum name/value mechanism generated XML shapes, JSON converters, and the OpenAPI
///     transformer all consume — a linear scan over an <see cref="EnumNameTable" />, never a dictionary
///     (tables are small: one row per defined member). Deliberately <see langword="public" />, not
///     <see langword="internal sealed" />: called from generated code and converters in host compilations
///     (other repos, later tasks), so it must be visible outside this assembly.
/// </summary>
public static class EnumLexical
{
	/// <summary>Renders <paramref name="value" /> in the style at <paramref name="styleIndex" /> of <paramref name="table" />.</summary>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> is not a defined member of
	///     <paramref name="table" />.
	/// </exception>
	public static string Format<TEnum>(EnumNameTable table, TEnum value, int styleIndex) where TEnum : unmanaged, Enum
	{
		var bits = ToBits(value);
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			if (table.Value(memberIndex) == bits)
				return table.Name(memberIndex, styleIndex);
		}

		throw new InvalidOperationException(
			$"'{value}' is an undefined value of '{table.EnumType}' and is illegal to write.");
	}

	/// <summary>
	///     Parses <paramref name="content" /> against the style at <paramref name="styleIndex" /> of
	///     <paramref name="table" />.
	/// </summary>
	/// <returns>
	///     A success carrying the matched member on an exact name match; otherwise a
	///     <see cref="ParseFailure.Malformed" /> failure — wrong case, off-list, and empty content all miss
	///     the same way, since <c>""</c> is content, never absence, at this layer.
	/// </returns>
	public static Result<TEnum> Parse<TEnum>(EnumNameTable table, string content, int styleIndex)
		where TEnum : unmanaged, Enum
	{
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			if (table.Name(memberIndex, styleIndex) == content)
				return FromBits<TEnum>(table.Value(memberIndex));
		}

		return new Failure(ParseFailure.Malformed, content, table.TypeName);
	}

	/// <summary>
	///     Renders a <c>[Flags]</c> <paramref name="value" />'s set bits as governed names, in
	///     <paramref name="table" />'s declaration order — the array/flags twin of <see cref="Format{TEnum}" />.
	///     Composite (multi-bit) and zero-valued table members never decompose into the output; the zero
	///     value renders as an empty array.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> carries a bit with no single-bit member of <paramref name="table" /> —
	///     illegal to write, whether or not that exact combination is itself a named (composite) member.
	/// </exception>
	public static string[] FormatFlags<TEnum>(EnumNameTable table, TEnum value, int styleIndex)
		where TEnum : unmanaged, Enum
	{
		var bits = ToBits(value);
		var mask = SingleBitMask(table);
		if ((bits & ~mask) != 0L)
			throw new InvalidOperationException(
				$"'{value}' carries bits with no single-bit member of '{table.EnumType}' and is illegal to write.");

		List<string> names = [];
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			var flag = table.Value(memberIndex);
			if (flag == 0L || (flag & (flag - 1L)) != 0L || (bits & flag) != flag)
				continue;
			names.Add(table.Name(memberIndex, styleIndex));
			bits &= ~flag;
		}

		return [.. names];
	}

	/// <summary>
	///     Parses <paramref name="tokens" /> against the style at <paramref name="styleIndex" /> of
	///     <paramref name="table" /> — the array/flags twin of <see cref="Parse{TEnum}" />. Each token
	///     resolves by exact name match against <paramref name="table" /> (composite/named-combination
	///     members match too, exactly as <see cref="Parse{TEnum}" /> already allows) and OR-accumulates;
	///     an empty sequence is the zero value, legal with or without a named zero member.
	/// </summary>
	/// <returns>
	///     A success carrying the OR of every token's matched value; otherwise the first failure
	///     encountered, in token order. An unknown token's <see cref="Failure.Detail" /> carries a
	///     "did you mean" suggestion (via <see cref="NameSuggestion" />) when one is found; a token
	///     repeating an earlier token in the same sequence fails with <see cref="ParseFailure.Duplicate" />
	///     instead — the token parsed fine, it just may not appear twice.
	/// </returns>
	public static Result<TEnum> ParseFlags<TEnum>(EnumNameTable table, IReadOnlyList<string> tokens, int styleIndex)
		where TEnum : unmanaged, Enum
	{
		var bits = 0L;
		HashSet<string> seen = [];
		foreach (var token in tokens)
		{
			var parsed = Parse<TEnum>(table, token, styleIndex);
			if (!parsed.TryGetValue(out Success<TEnum> success))
			{
				var known = new string[table.Count];
				for (var memberIndex = 0; memberIndex < known.Length; memberIndex++)
					known[memberIndex] = table.Name(memberIndex, styleIndex);
				var suggestion = NameSuggestion.Nearest(token, known);
				return new Failure(ParseFailure.Malformed, token, table.TypeName,
					detail: suggestion is null ? null : $"did you mean '{suggestion}'?");
			}

			if (!seen.Add(token))
				return new Failure(ParseFailure.Duplicate, token, table.TypeName);

			bits |= ToBits(success.Value);
		}

		return FromBits<TEnum>(bits);
	}

	static long SingleBitMask(EnumNameTable table)
	{
		var mask = 0L;
		for (var memberIndex = 0; memberIndex < table.Count; memberIndex++)
		{
			var candidate = table.Value(memberIndex);
			if (candidate != 0L && (candidate & (candidate - 1L)) == 0L)
				mask |= candidate;
		}

		return mask;
	}

	// Twinned with ResultEnumSerializer<TEnum>.ToBits (Infrastructure.Web.Grpc, different assembly —
	// deliberately not shared): Unsafe.As identity reinterprets between the enum and its underlying
	// integral, sound per branch because Unsafe.SizeOf<TEnum> is a JIT constant for a closed generic.
	static long ToBits<TEnum>(TEnum value) where TEnum : unmanaged, Enum => Unsafe.SizeOf<TEnum>() switch
	{
		1 => Unsafe.As<TEnum, byte>(ref value),
		2 => Unsafe.As<TEnum, ushort>(ref value),
		4 => Unsafe.As<TEnum, uint>(ref value),
		_ => Unsafe.As<TEnum, long>(ref value)
	};

	// Twinned with ResultEnumSerializer<TEnum>.FromBits (Infrastructure.Web.Grpc, different assembly —
	// deliberately not shared): the inverse Unsafe.As identity reinterpret, narrowing the long back to
	// the enum's own storage width before reinterpreting.
	static TEnum FromBits<TEnum>(long raw) where TEnum : unmanaged, Enum
	{
		if (Unsafe.SizeOf<TEnum>() == 1)
		{
			var v = (byte)raw;
			return Unsafe.As<byte, TEnum>(ref v);
		}

		if (Unsafe.SizeOf<TEnum>() == 2)
		{
			var v = (ushort)raw;
			return Unsafe.As<ushort, TEnum>(ref v);
		}

		if (Unsafe.SizeOf<TEnum>() == 4)
		{
			var v = (uint)raw;
			return Unsafe.As<uint, TEnum>(ref v);
		}

		return Unsafe.As<long, TEnum>(ref raw);
	}
}
