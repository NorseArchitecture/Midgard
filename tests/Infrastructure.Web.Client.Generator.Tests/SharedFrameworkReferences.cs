using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Client.Generator.Tests;

/// <summary>
///     Every <em>managed</em> assembly in a shared-framework directory, as metadata references —
///     enumerated from an already-loaded type's own assembly directory rather than a hardcoded SDK
///     path, so these harnesses survive an SDK bump untouched.
///     Native images are skipped deliberately: on Windows the .NET and ASP.NET Core shared frameworks
///     ship <c>coreclr.dll</c>, <c>clrjit.dll</c>, <c>hostpolicy.dll</c>, <c>msquic.dll</c> and
///     <c>aspnetcorev2_inprocess.dll</c> (the IIS in-process shim) alongside the managed assemblies,
///     and handing Roslyn a metadata-free PE fails the whole compilation with CS0009. Linux and macOS
///     name their native siblings <c>.so</c>/<c>.dylib</c>, so a <c>"*.dll"</c> glob never picks one
///     up there — which is exactly why every call site was green on CI and red on a Windows dev box.
/// </summary>
static class SharedFrameworkReferences
{
	public static IEnumerable<MetadataReference> In(string directory) =>
		Directory.GetFiles(directory, "*.dll")
			.Where(IsManagedAssembly)
			.Select(path => MetadataReference.CreateFromFile(path));

	static bool IsManagedAssembly(string path)
	{
		using var stream = File.OpenRead(path);
		using PEReader peReader = new(stream);
		return peReader.HasMetadata;
	}
}
