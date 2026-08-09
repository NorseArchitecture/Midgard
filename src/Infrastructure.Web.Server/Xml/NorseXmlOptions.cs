namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     Options governing Futhark's XML serialization. Deliberately <see langword="public" />, not
///     <see langword="internal sealed" />: generated code in a host compilation (a different repo, later
///     task) constructs and reads these options, so the type must be visible outside this assembly.
/// </summary>
public sealed class NorseXmlOptions
{
	/// <summary>The wire-name casing convention element and attribute names project through.</summary>
	public XmlCaseStyle CaseStyle { get; set; }
}
