using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Generator.Policies;

namespace Norse.Infrastructure.Web.Server.Tests.Authorization;

/// <summary>
///     The default-closed lane (spec 7.5), proven through the server generator's real output: three declarations are
///     compiled with <see cref="PolicyRegistrationGenerator" />, the generated <c>AddNorsePolicies()</c> is loaded and
///     called, and the policies are evaluated against the handlers <c>AddNorseAuthentication()</c> registers.
/// </summary>
public sealed class GeneratedPolicyLaneTests
{
	const string Declarations = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Components.Authorization;

		namespace Lanes.Host;

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

	[Fact]
	async Task A_policy_declaring_no_lane_admits_only_identity()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Stamped(NorseLanes.Identity), "Lanes.Undeclared")).ShouldBeTrue();
		(await AuthorizeAsync(provider, Stamped(NorseLanes.User), "Lanes.Undeclared")).ShouldBeFalse();
		(await AuthorizeAsync(provider, Stamped(NorseLanes.Machine), "Lanes.Undeclared")).ShouldBeFalse();
		(await LaneRequirementsAsync(provider, "Lanes.Undeclared")).ShouldHaveSingleItem().Lanes
			.ShouldBe([NorseLanes.Identity]);
	}

	[Fact]
	async Task Require_lane_declarations_are_untouched()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Stamped(NorseLanes.User), "Lanes.UserOnly")).ShouldBeTrue();
		(await AuthorizeAsync(provider, Stamped(NorseLanes.Identity), "Lanes.UserOnly")).ShouldBeFalse();
		(await LaneRequirementsAsync(provider, "Lanes.UserOnly")).ShouldHaveSingleItem().Lanes
			.ShouldBe([NorseLanes.User]);
	}

	[Fact]
	async Task Allow_any_lane_declarations_are_untouched()
	{
		await using var provider = Provider();

		(await AuthorizeAsync(provider, Stamped(NorseLanes.Identity), "Lanes.Any")).ShouldBeTrue();
		(await AuthorizeAsync(provider, Stamped(NorseLanes.User), "Lanes.Any")).ShouldBeTrue();
		(await AuthorizeAsync(provider, Stamped(NorseLanes.Machine), "Lanes.Any")).ShouldBeTrue();
		(await LaneRequirementsAsync(provider, "Lanes.Any")).ShouldBeEmpty();
	}

	static ClaimsPrincipal Stamped(string lane) => new(new ClaimsIdentity([new Claim("sub", "subject")], lane));

	static ServiceProvider Provider()
	{
		ServiceCollection services = new();
		// Every host carries configuration; AddNorseAuthentication() binds Norse:Authentication from it.
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()).AddLogging().AddNorseAuthentication();
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

	// Compiled against this test host's own trusted platform assemblies, so the emitted assembly binds to exactly
	// the Asgard, Midgard and ASP.NET Core assemblies already loaded.
	static MethodInfo GenerateAddNorsePolicies()
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create(
			"Lanes.Host",
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
			.GetType("Lanes.Host.NorsePolicyRegistration", throwOnError: true)!
			.GetMethod("AddNorsePolicies", BindingFlags.Public | BindingFlags.Static)!;
	}
}
