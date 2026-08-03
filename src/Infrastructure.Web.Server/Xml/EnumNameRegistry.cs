using System.Diagnostics.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Looks up an <see cref="EnumNameTable"/> by its <see cref="EnumNameTable.EnumType"/>. Deliberately
/// <see langword="public"/>, not <see langword="internal sealed"/>: generated registration code in a
/// host compilation (a different repo, later task) populates this registry, and the JSON converters
/// and OpenAPI transformer read it, so it must be visible outside this assembly.
/// </summary>
public sealed class EnumNameRegistry
{
	readonly Dictionary<Type, EnumNameTable> _tables = [];

	/// <summary>Registers <paramref name="table"/> under its <see cref="EnumNameTable.EnumType"/>.</summary>
	/// <exception cref="InvalidOperationException">A table for the same <see cref="EnumNameTable.EnumType"/> is already registered.</exception>
	public void Add(EnumNameTable table)
	{
		ArgumentNullException.ThrowIfNull(table);
		if (!_tables.TryAdd(table.EnumType, table))
			throw new InvalidOperationException($"A table for enum type '{table.EnumType}' is already registered.");
	}

	/// <summary>Looks up the table registered for <paramref name="enumType"/>.</summary>
	public bool TryGet(Type enumType, [NotNullWhen(true)] out EnumNameTable? table) =>
		_tables.TryGetValue(enumType, out table);
}
