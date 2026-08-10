using System.Xml;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The non-generic shape seam every Futhark contract shape implements. Deliberately
///     <see langword="public" />, not <see langword="internal sealed" />: generated code in a host
///     compilation (a different repo, later task) implements this interface per contract, so it must
///     be visible outside this assembly.
/// </summary>
public interface IXmlShape
{
	/// <summary>The CLR type this shape reads and writes.</summary>
	Type ContractType { get; }

	/// <summary>The root element name, projected through <paramref name="style" />.</summary>
	string RootName(XmlCaseStyle style);

	/// <summary>Writes <paramref name="value" />, boxed, as XML.</summary>
	void WriteObject(XmlWriter writer, object value, XmlCaseStyle style);

	/// <summary>
	///     Reads XML into a boxed instance of <see cref="ContractType" />, accumulating failures into
	///     <paramref name="context" /> rather than throwing.
	/// </summary>
	object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context);
}

/// <summary>
///     The strongly-typed shape seam every Futhark contract shape implements alongside <see cref="IXmlShape" />.
///     Deliberately <see langword="public" />, not <see langword="internal sealed" />: generated code in a
///     host compilation (a different repo, later task) implements this interface per contract, so it must
///     be visible outside this assembly.
/// </summary>
public interface IXmlShape<T> : IXmlShape
{
	/// <summary>Writes <paramref name="value" /> as XML.</summary>
	void Write(XmlWriter writer, T value, XmlCaseStyle style);

	/// <summary>
	///     Reads XML into a <typeparamref name="T" />, accumulating failures into <paramref name="context" /> rather than
	///     throwing.
	/// </summary>
	T? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context);
}
