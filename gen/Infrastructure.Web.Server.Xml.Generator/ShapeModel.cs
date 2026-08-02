using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

/// <summary>
/// A structurally value-equatable, immutable array — <see cref="ImmutableArray{T}"/> compares by
/// reference identity of its backing storage, which silently defeats incremental-generator caching
/// the moment two content-equal pipeline values are built from independently-allocated arrays across
/// driver runs. Every collection nested in <see cref="ShapeModel"/> (directly or transitively) uses
/// this instead, so the whole model participates correctly in Roslyn's step-output equality check.
/// </summary>
readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T> where T : IEquatable<T>
{
	readonly T[] _values;

	EquatableArray(T[] values) =>
		_values = values;

	public static readonly EquatableArray<T> Empty = new([]);

	public static EquatableArray<T> Create(IEnumerable<T> values) =>
		new([.. values]);

	public int Count =>
		_values.Length;

	public T this[int index] =>
		_values[index];

	public bool Equals(EquatableArray<T> other)
	{
		if (ReferenceEquals(_values, other._values))
			return true;

		var mine = _values ?? [];
		var theirs = other._values ?? [];
		if (mine.Length != theirs.Length)
			return false;

		for (var i = 0; i < mine.Length; i++)
			if (!EqualityComparer<T>.Default.Equals(mine[i], theirs[i]))
				return false;

		return true;
	}

	public override bool Equals(object? obj) =>
		obj is EquatableArray<T> other && Equals(other);

	public override int GetHashCode()
	{
		var hash = 17;
		foreach (var value in _values ?? [])
			hash = unchecked((hash * 31) + EqualityComparer<T>.Default.GetHashCode(value!));
		return hash;
	}

	public IEnumerator<T> GetEnumerator() =>
		((IEnumerable<T>)(_values ?? [])).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() =>
		GetEnumerator();

	public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) =>
		left.Equals(right);

	public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) =>
		!left.Equals(right);
}

/// <summary>
/// A file position with no <see cref="SyntaxTree"/>/<see cref="Compilation"/> attached — the
/// symbol-free stand-in for <see cref="Location"/> that pipeline values carry instead of a live one.
/// Round-trips through <see cref="ToLocation"/> into a real <see cref="Location"/> only at
/// <c>RegisterSourceOutput</c> time, once the caching boundary no longer matters.
/// </summary>
readonly record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
	/// <summary>
	/// The zero-width location at the empty path — <c>default(LocationInfo)</c> would otherwise carry
	/// a <see langword="null"/> <see cref="FilePath"/> (the record's auto-generated parameterless
	/// default), and <see cref="Location.Create(string, TextSpan, LinePositionSpan)"/> throws
	/// <see cref="ArgumentNullException"/> on a null path — a real crash, not a theoretical one: any
	/// reachable type from a referenced assembly (no in-source location) that trips a diagnostic hits
	/// exactly this path in <see cref="FromSymbol"/>.
	/// </summary>
	public static readonly LocationInfo None = new(string.Empty, default, default);

	public Location ToLocation() =>
		Location.Create(FilePath ?? string.Empty, Span, LineSpan);

	public static LocationInfo FromLocation(Location location) =>
		new(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);

	/// <summary>The symbol's first source location, or <see cref="None"/> when the symbol has none in source (a referenced-assembly type).</summary>
	public static LocationInfo FromSymbol(ISymbol symbol) =>
		symbol.Locations.FirstOrDefault(l => l.IsInSource) is { } location ? FromLocation(location) : None;
}

/// <summary>
/// A symbol-free, fully equatable stand-in for a reported <see cref="Diagnostic"/> — pipeline values
/// carry this, never a real <see cref="Diagnostic"/>, so a diagnostic produced from an unchanged
/// syntax node still compares equal across driver runs. <see cref="Descriptor"/> is one of the
/// static, immutable instances in <see cref="Diagnostics"/>; <see cref="DiagnosticDescriptor"/> itself
/// carries no symbol/compilation state and is safe to hold directly.
/// </summary>
sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, EquatableArray<string> MessageArgs)
{
	public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] messageArgs) =>
		new(descriptor, LocationInfo.FromSymbol(symbol), EquatableArray<string>.Create(messageArgs));

	public Diagnostic ToDiagnostic() =>
		Diagnostic.Create(Descriptor, Location.ToLocation(), [.. MessageArgs.Cast<object>()]);
}

/// <summary>Whether a contract member is a wrapped/raw scalar, a nested complex type, or a collection of a complex type.</summary>
enum MemberKind
{
	Scalar,
	Complex,
	Collection
}

/// <summary>One defined value of an enum-typed scalar member, carried so the writer/reader (a later task) can emit a build-time name↔value table with no <c>Enum.Parse</c>/reflection at runtime.</summary>
sealed record EnumValueModel(string ClrName, EquatableArray<string> WireNames, long Value);

/// <summary>
/// One member of a <see cref="ShapeModel"/>, in declaration order. <see cref="ScalarTypeName"/> is
/// set only when <see cref="Kind"/> is <see cref="MemberKind.Scalar"/> (the type inside the
/// <c>Result&lt;T&gt;</c> wrapper, or the raw scalar type itself); <see cref="ComplexTypeName"/> is
/// set when <see cref="Kind"/> is <see cref="MemberKind.Complex"/> (the member's own type) or
/// <see cref="MemberKind.Collection"/> (the collection's item type) — the key a later task resolves
/// against the sibling <see cref="ShapeModel"/> emitted for that type.
/// </summary>
sealed record MemberModel(
	string ClrName,
	MemberKind Kind,
	EquatableArray<string> WireNames,
	bool IsResultWrapped,
	bool IsNullable,
	string? ScalarTypeName,
	string? ComplexTypeName,
	bool IsFlagsEnum,
	EquatableArray<EnumValueModel> EnumValues);

/// <summary>
/// The build-time shape of one complex type reachable from a facade controller's request or response
/// closure — fully equatable and symbol-free by construction (no <see cref="ISymbol"/>,
/// <see cref="Compilation"/>, or <see cref="SyntaxNode"/> anywhere in it, directly or nested), so an
/// edit that doesn't change the exposed surface hits the incremental cache and a later task's
/// emission stage does no work. <see cref="Members"/> is in declaration order — attributes-before-
/// elements and each group's internal order (the wire grammar) is a later task's projection over this
/// same order, not a second ordering this model has to carry.
/// </summary>
sealed record ShapeModel(string TypeName, EquatableArray<string> WireNames, EquatableArray<MemberModel> Members);

/// <summary>
/// Everything discovered from one facade-controller class declaration: the shapes of every complex
/// type reachable from its actions' request/response closures, plus every shape-law diagnostic found
/// along the way. <see langword="null"/> when the class does not derive from <c>GrpcControllerBase</c>
/// — filtered out of the pipeline before this type is ever constructed.
/// </summary>
readonly record struct ControllerShapeResult(EquatableArray<ShapeModel> Shapes, EquatableArray<DiagnosticInfo> Diagnostics);
