using System.Reflection;
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
		MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(System.Runtime.Serialization.DataContractAttribute).Assembly.Location)
	];

	// Every assembly under the .NET / ASP.NET Core shared framework directories the FrameworkReference
	// resolves against — enumerated from already-loaded types' own assembly directories rather than
	// hardcoding an SDK path, so this survives an SDK bump untouched (mirrors the sibling
	// Infrastructure.Web.Server.Generator.Tests harness).
	public static readonly MetadataReference[] AspNetCore =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Mvc.ControllerBase).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f))
	];
}
