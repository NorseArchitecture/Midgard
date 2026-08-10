using System.Reflection;
using Microsoft.CodeAnalysis;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Analyzers.Tests;

static class ReferenceAssemblies
{
	// Every fixture in this project exercises RuntimeTypeModel, unlike Architecture.Analyzers.Tests
	// (where only some fixtures need a specific banned assembly) — so protobuf-net's assembly belongs
	// in the shared baseline here, not threaded through a per-test extraReferences parameter.
	public static readonly MetadataReference[] Bcl =
	[
		MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Exception).Assembly.Location),
		MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
		MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
		MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(RuntimeTypeModel).Assembly.Location),
		// RuntimeTypeModel (above) lives in the protobuf-net facade assembly, but its base type
		// TypeModel and CompatibilityLevel live in the separate protobuf-net.Core assembly the facade
		// depends on — a fixture invoking inherited/overload-resolved members (IsDefined, Add's
		// CompatibilityLevel-bearing overload) needs that assembly on the reference list too, or the
		// fixture fails to compile with CS0012.
		MetadataReference.CreateFromFile(typeof(TypeModel).Assembly.Location)
	];
}
