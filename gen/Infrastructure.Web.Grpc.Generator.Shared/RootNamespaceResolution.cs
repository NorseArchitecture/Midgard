using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared;

// Linked into both Infrastructure.Web.Server.Generator and Infrastructure.Web.Client.Generator via
// <Compile Include> -- same shared-source-per-consumer shape as ContractDiscovery.cs and
// ComponentDiscovery.cs, for the same reason (Roslyn generators can't reference other analyzer-only
// assemblies).
/// <summary>
/// Resolves the root namespace an incremental generator emits generated source into. Prefers
/// MSBuild's own <c>RootNamespace</c> property -- read via <c>build_property.RootNamespace</c>, the
/// standard <see cref="AnalyzerConfigOptionsProvider"/> interop mechanism source generators use to
/// see build properties -- over the compiling assembly's raw <see cref="Compilation.AssemblyName"/>:
/// an assembly name is not guaranteed to be a legal C# namespace token (hyphens, a leading digit, a
/// reserved character) and is independent of the project's actual <c>RootNamespace</c>, which can
/// diverge from the assembly name freely (renamed assembly, hyphenated package id, ...). Falls back
/// to the assembly name, then a fixed default, when <c>RootNamespace</c> isn't configured, and
/// sanitizes whichever value wins so a generator using this can never emit an illegal
/// <c>namespace {{...}};</c> token regardless of which value backed it.
/// </summary>
static class RootNamespaceResolution
{
	const string Fallback = "Norse.Generated";

	public static string Resolve(Compilation compilation, AnalyzerConfigOptionsProvider options)
	{
		var candidate = options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var configured) && !string.IsNullOrWhiteSpace(configured)
			? configured
			: compilation.AssemblyName ?? Fallback;

		return Sanitize(candidate);
	}

	/// <summary>
	/// Segment-by-segment identifier sanitization: every character that isn't a Unicode letter,
	/// digit, or underscore becomes an underscore; a segment that would start with a digit gets an
	/// underscore prefix; an empty segment (leading/trailing/doubled '.') is dropped outright. Falls
	/// back to <see cref="Fallback"/> only if every segment sanitizes away to nothing.
	/// </summary>
	static string Sanitize(string candidate)
	{
		var segments = candidate.Split('.')
			.Select(SanitizeSegment)
			.Where(segment => segment.Length > 0)
			.ToArray();

		return segments.Length == 0 ? Fallback : string.Join(".", segments);
	}

	static string SanitizeSegment(string segment)
	{
		StringBuilder builder = new();
		foreach (var c in segment)
			builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

		if (builder.Length == 0)
			return string.Empty;

		if (char.IsDigit(builder[0]))
			builder.Insert(0, '_');

		return builder.ToString();
	}
}
