using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Norse.Infrastructure.Web.Server.Tests;

/// <summary>
///     Ruling 7: OpenIddict never leaves Himinbjörg. Every Midgard <c>src/</c> project is scanned twice: its built
///     assembly's metadata references, and its restored dependency closure (<c>obj/project.assets.json</c>), which
///     catches what metadata alone cannot. A const-only use (a scheme name) compiles to no assembly reference at
///     all, which is exactly how the leak hid before. This test project references every <c>src/</c> project with
///     <c>ReferenceOutputAssembly="false"</c>, so all of them are built and restored before it runs.
/// </summary>
public sealed class OpenIddictReferenceScanTests
{
	const string OpenIddictPrefix = "OpenIddict.";

	// The count guard: a new src/ project fails this until the count is raised and the project is added to this
	// test project's build-order references, so no project joins the realm unseen.
	const int MidgardSourceProjects = 10;

	static string Configuration =>
		typeof(OpenIddictReferenceScanTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

	[Fact]
	void Every_midgard_source_project_is_scanned() =>
		SourceProjects().Count.ShouldBe(MidgardSourceProjects);

	[Fact]
	void No_midgard_assembly_references_openiddict()
	{
		List<string> offenders = [];
		foreach (var project in SourceProjects())
		{
			var assemblies = BuiltAssemblies(project);
			assemblies.ShouldNotBeEmpty($"{Path.GetFileName(project)} has no built assembly under bin/{Configuration}");
			foreach (var path in assemblies)
			{
				using var stream = File.OpenRead(path);
				using PEReader pe = new(stream);
				var metadata = pe.GetMetadataReader();
				offenders.AddRange(metadata.AssemblyReferences
					.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
					.Where(static name => name.StartsWith(OpenIddictPrefix, StringComparison.OrdinalIgnoreCase))
					.Select(name => $"{Path.GetFileName(path)} -> {name}"));
			}
		}

		offenders.ShouldBeEmpty();
	}

	[Fact]
	void No_midgard_dependency_closure_contains_openiddict()
	{
		List<string> offenders = [];
		foreach (var project in SourceProjects())
		{
			var assets = Path.Combine(project, "obj", "project.assets.json");
			File.Exists(assets).ShouldBeTrue($"{Path.GetFileName(project)} is not restored");
			using var document = JsonDocument.Parse(File.ReadAllText(assets));
			offenders.AddRange(document.RootElement.GetProperty("libraries").EnumerateObject()
				.Select(static library => library.Name)
				.Where(static name => name.StartsWith(OpenIddictPrefix, StringComparison.OrdinalIgnoreCase))
				.Select(name => $"{Path.GetFileName(project)} -> {name}"));
		}

		offenders.ShouldBeEmpty();
	}

	// Every directory under src/ holding a project file, found from the realm root (the directory carrying
	// Midgard.slnx) above this test's output.
	static IReadOnlyList<string> SourceProjects()
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while (root is not null && !File.Exists(Path.Combine(root.FullName, "Midgard.slnx")))
			root = root.Parent;
		root.ShouldNotBeNull("the realm root (Midgard.slnx) was not found above the test output");

		return
		[
			.. Directory.GetFiles(Path.Combine(root.FullName, "src"), "*.csproj", SearchOption.AllDirectories)
				.Select(static project => Path.GetDirectoryName(project)!)
				.Order(StringComparer.Ordinal)
		];
	}

	// The brand is build-injected: project Infrastructure.X builds Norse.Infrastructure.X.dll.
	static string[] BuiltAssemblies(string project)
	{
		var bin = Path.Combine(project, "bin", Configuration);
		return Directory.Exists(bin)
			? Directory.GetFiles(bin, $"Norse.{Path.GetFileName(project)}.dll", SearchOption.AllDirectories)
			: [];
	}
}
