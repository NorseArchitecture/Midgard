using System.Diagnostics.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Looks up an <see cref="IXmlShape"/> by its <see cref="IXmlShape.ContractType"/>. Deliberately
/// <see langword="public"/>, not <see langword="internal sealed"/>: generated registration code in a
/// host compilation (a different repo, later task) populates this registry, so it must be visible
/// outside this assembly.
/// </summary>
public sealed class XmlShapeRegistry
{
	readonly Dictionary<Type, IXmlShape> _shapes = [];

	/// <summary>Registers <paramref name="shape"/> under its <see cref="IXmlShape.ContractType"/>.</summary>
	/// <exception cref="ArgumentException">A shape for the same <see cref="IXmlShape.ContractType"/> is already registered.</exception>
	public void Add(IXmlShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);
		if (!_shapes.TryAdd(shape.ContractType, shape))
			throw new ArgumentException($"A shape for contract type '{shape.ContractType}' is already registered.", nameof(shape));
	}

	/// <summary>Looks up the shape registered for <paramref name="contractType"/>.</summary>
	public bool TryGet(Type contractType, [NotNullWhen(true)] out IXmlShape? shape) =>
		_shapes.TryGetValue(contractType, out shape);
}
