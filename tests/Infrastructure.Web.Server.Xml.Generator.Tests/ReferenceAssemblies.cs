using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

static class ReferenceAssemblies
{
	public static readonly MetadataReference[] Bcl =
	[
		MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
		MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
		MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
		MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(DataContractAttribute).Assembly.Location),
		// XmlWriter/XmlReader — the runtime IXmlShape<T> seam's Write/Read parameters (Task 6/7
		// emission). Neither typeof(XmlWriter).Assembly.Location (System.Private.Xml — the real
		// implementation assembly, wrong identity: the reference System.Infrastructure.Web.Server.dll
		// binds to System.Xml.ReaderWriter's assembly identity, not System.Private.Xml's) nor
		// Assembly.Load("System.Xml.ReaderWriter") (resolves to the same implementation assembly at
		// runtime) gives a reference with the right identity — only the SDK's own ref-pack copy of
		// System.Xml.ReaderWriter.dll (a forwarder-only contract assembly, never loaded at runtime,
		// so no typeof/Assembly.Load path reaches it) has it.
		MetadataReference.CreateFromFile(XmlReaderWriterRefPath())
	];

	// Every assembly under the .NET / ASP.NET Core shared framework directories the FrameworkReference
	// resolves against — enumerated from already-loaded types' own assembly directories rather than
	// hardcoding an SDK path, so this survives an SDK bump untouched (mirrors the sibling
	// Infrastructure.Web.Server.Generator.Tests harness).
	public static readonly MetadataReference[] AspNetCore =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(ControllerBase).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f))
	];

	static string XmlReaderWriterRefPath()
	{
		var runtimeDirectory =
			Path.GetDirectoryName(typeof(object).Assembly.Location)!; // .../shared/Microsoft.NETCore.App/{version}
		var runtimeVersion = Path.GetFileName(runtimeDirectory);
		var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
		var tfm = $"net{runtimeVersion.Split('.')[0]}.0";
		return Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", runtimeVersion, "ref", tfm,
			"System.Xml.ReaderWriter.dll");
	}
}
