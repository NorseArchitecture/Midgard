namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// The generated per-enum lookup <see cref="EnumLexical"/> scans: one row per defined member, one
/// column per <see cref="XmlCaseStyle"/> (Camel/Pascal/Snake/Upper/Lower, in that enum's declared
/// order). <paramref name="names"/> is <c>names[memberIndex][styleIndex]</c>; <paramref name="values"/>
/// is the member's underlying integral, widened to <see langword="long"/>, at the same
/// <c>memberIndex</c>. Deliberately <see langword="public"/>, not <see langword="internal sealed"/>:
/// generated host code, the JSON converters, and the OpenAPI transformer (later tasks, other
/// compilations) all construct and read these tables, so the type must be visible outside this
/// assembly.
/// </summary>
/// <param name="enumType">The enum type this table describes.</param>
/// <param name="typeName">The type name surfaced in <see cref="Norse.Primitives.Failure.ExpectedType"/> and undefined-value throw messages.</param>
/// <param name="names">Per-member, per-style name columns; <c>names[memberIndex][styleIndex]</c>.</param>
/// <param name="values">Per-member underlying integral values, widened to <see langword="long"/>.</param>
public sealed class EnumNameTable(Type enumType, string typeName, string[][] names, long[] values)
{
	/// <summary>The enum type this table describes.</summary>
	public Type EnumType { get; } = enumType;

	/// <summary>The type name surfaced in <see cref="Norse.Primitives.Failure.ExpectedType"/> and undefined-value throw messages.</summary>
	public string TypeName { get; } = typeName;

	/// <summary>The number of defined members this table carries.</summary>
	public int Count => values.Length;

	/// <summary>The name of member <paramref name="memberIndex"/> in the style at <paramref name="styleIndex"/>.</summary>
	public string Name(int memberIndex, int styleIndex) => names[memberIndex][styleIndex];

	/// <summary>The underlying integral value of member <paramref name="memberIndex"/>, widened to <see langword="long"/>.</summary>
	public long Value(int memberIndex) => values[memberIndex];
}
