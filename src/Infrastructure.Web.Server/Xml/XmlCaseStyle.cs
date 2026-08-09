namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The wire-name casing conventions Futhark's XML serialization can project element and attribute
///     names through. Deliberately <see langword="public" />, not <see langword="internal sealed" />:
///     generated code in a host compilation (a different repo, later task) selects a member of this
///     enum, so it must be visible outside this assembly.
/// </summary>
public enum XmlCaseStyle
{
	/// <summary>e.g. <c>policyNumber</c>.</summary>
	CamelCase,

	/// <summary>e.g. <c>PolicyNumber</c>.</summary>
	PascalCase,

	/// <summary>e.g. <c>policy_number</c>.</summary>
	SnakeCase,

	/// <summary>e.g. <c>POLICYNUMBER</c>.</summary>
	UpperCase,

	/// <summary>e.g. <c>policynumber</c>.</summary>
	LowerCase
}
