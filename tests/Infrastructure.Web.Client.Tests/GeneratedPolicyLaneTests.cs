using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Client.Authorization;
using Norse.Infrastructure.Web.Client.Generator.Policies;

namespace Norse.Infrastructure.Web.Client.Tests;

/// <summary>
///     The default-closed lane (spec 7.5) on the client, proven through the client generator's real output: three
///     declarations are compiled with <see cref="PolicyRegistrationGenerator" /> against WASM-reachable references
///     only, and the generated <c>AddNorsePolicies()</c> is loaded and called with nothing else registered, so the
///     client lane handlers arrive through its own bootstrap, as they do in a WASM host.
/// </summary>
public sealed class GeneratedPolicyLaneTests
{
	const string Declarations = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Components.Authorization;

		namespace Lanes.Client;

		public static class LanePolicies
		{
			[NorsePolicy("Lanes.Undeclared")]
			public static void Undeclared(AuthorizationPolicyBuilder policy) => policy.RequireAuthenticatedUser();

			[NorsePolicy("Lanes.UserOnly")]
			public static void UserOnly(AuthorizationPolicyBuilder policy) => policy.RequireLane(NorseLanes.User);

			[NorsePolicy("Lanes.Any")]
			public static void Any(AuthorizationPolicyBuilder policy) => policy.AllowAnyLane().RequireAuthenticatedUser();
		}
		""";

	static readonly Lazy<MethodInfo> _addNorsePolicies = new(GenerateAddNorsePolicies);

	// The browser's principal is the identity lane whatever its authentication type, so "only identity" on the client
	// is: the closed policy carries exactly the identity lane, an authenticated principal passes, an empty one fails.
	[Fact]
	async Task A_policy_declaring_no_lane_admits_only_identity()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Authenticated(), "Lanes.Undeclared")).ShouldBeTrue();
		(await AuthorizeAsync(provider, new(), "Lanes.Undeclared")).ShouldBeFalse();
		(await LaneRequirementsAsync(provider, "Lanes.Undeclared")).ShouldHaveSingleItem().Lanes
			.ShouldBe([NorseLanes.Identity]);
	}

	[Fact]
	async Task Require_lane_declarations_are_untouched()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Authenticated(), "Lanes.UserOnly")).ShouldBeFalse();
		(await LaneRequirementsAsync(provider, "Lanes.UserOnly")).ShouldHaveSingleItem().Lanes
			.ShouldBe([NorseLanes.User]);
	}

	[Fact]
	async Task Allow_any_lane_declarations_are_untouched()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Authenticated(), "Lanes.Any")).ShouldBeTrue();
		(await LaneRequirementsAsync(provider, "Lanes.Any")).ShouldBeEmpty();
	}

	static ClaimsPrincipal Authenticated() =>
		new(new ClaimsIdentity([new Claim("sub", "subject")], "Identity.Application"));

	static ServiceProvider Provider()
	{
		ServiceCollection services = new();
		services.AddLogging();
		_addNorsePolicies.Value.Invoke(null, [services]);
		return services.BuildServiceProvider();
	}

	static async Task<bool> AuthorizeAsync(IServiceProvider provider, ClaimsPrincipal user, string policyName) =>
		(await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policyName)).Succeeded;

	static async Task<LaneRequirement[]> LaneRequirementsAsync(IServiceProvider provider, string policyName)
	{
		var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policyName);
		return [.. policy.ShouldNotBeNull().Requirements.OfType<LaneRequirement>()];
	}

	// Microsoft.NETCore.App plus the standalone packages a WASM host references, never the ASP.NET Core shared
	// framework this test host also loads: with it visible the client generator stands down for the server's.
	static MethodInfo GenerateAddNorsePolicies()
	{
		MetadataReference[] references =
		[
			.. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
				.Where(IsManagedAssembly)
				.Select(path => MetadataReference.CreateFromFile(path)),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(AuthorizationBuilder).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(NorsePolicyAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(ClientAuthorizationExtensions).Assembly.Location)
		];
		var compilation = CSharpCompilation.Create(
			"Lanes.Client",
			[CSharpSyntaxTree.ParseText(Declarations, cancellationToken: TestContext.Current.CancellationToken)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create(new PolicyRegistrationGenerator().AsSourceGenerator())
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics,
				TestContext.Current.CancellationToken);
		diagnostics.ShouldBeEmpty();

		using MemoryStream stream = new();
		var result = output.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		result.Success.ShouldBeTrue(string.Join('\n', result.Diagnostics));
		return Assembly.Load(stream.ToArray())
			.GetType("Lanes.Client.NorsePolicyRegistration", throwOnError: true)!
			.GetMethod("AddNorsePolicies", BindingFlags.Public | BindingFlags.Static)!;
	}

	// Native images (coreclr.dll and siblings on Windows) carry no metadata and would fail the compilation (CS0009).
	static bool IsManagedAssembly(string path)
	{
		using var stream = File.OpenRead(path);
		using PEReader reader = new(stream);
		return reader.HasMetadata;
	}
}
