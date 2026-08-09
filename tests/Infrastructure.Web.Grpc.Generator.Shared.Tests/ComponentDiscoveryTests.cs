using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared.Tests;

public sealed class ComponentDiscoveryTests
{
	const string ValidatorSource = """
		using FluentValidation;

		namespace Own;

		public sealed record FakeRequest;

		public sealed class FakeValidator : AbstractValidator<FakeRequest>;
		""";

	// Field declaration order matters here: static field initializers run top-to-bottom, and the
	// fixture MetadataReference fields below call BuildReferenceAssembly, which reads _extraReferences
	// -- so it must be declared first, not just textually convenient.
	static readonly MetadataReference[] _extraReferences =
	[
		.. ReferenceAssemblies.Net110,
		MetadataReference.CreateFromFile(typeof(IValidator<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(RouteAttribute).Assembly.Location)
	];

	// A genuinely separate assembly (CompilationReference via ToMetadataReference -- no IL emission
	// needed) so ComponentDiscovery.Discover sees it as a referenced IAssemblySymbol distinct from the
	// harness's own compilation, the way a real consuming project sees FluentValidation validators
	// declared in an upstream package.
	static readonly MetadataReference _referencedValidatorAssembly = BuildReferenceAssembly(
		"Referenced.Validators",
		"""
		using FluentValidation;

		namespace Referenced;

		public sealed record OtherRequest;

		public sealed class OtherValidator : AbstractValidator<OtherRequest>;
		""");

	static readonly MetadataReference _routableAssembly = BuildReferenceAssembly(
		"Routable.Pages",
		"""
		using Microsoft.AspNetCore.Components;

		namespace RoutableAsm;

		[Route("/widget")]
		public sealed class WidgetPage;
		""");

	static readonly MetadataReference _plainAssembly = BuildReferenceAssembly(
		"Plain.Library",
		"""
		namespace PlainAsm;

		public sealed class Nothing;
		""");

	// A referenced assembly whose only validator is internal -- the discovering compilation can't
	// legally write typeof(Referenced.InternalValidator) (CS0122), so discovery must exclude it even
	// though it genuinely implements IValidator<T>.
	static readonly MetadataReference _internalValidatorAssembly = BuildReferenceAssembly(
		"Internal.Validators",
		"""
		using FluentValidation;

		namespace Referenced;

		public sealed record InternalValidatedRequest;

		internal sealed class InternalValidator : AbstractValidator<InternalValidatedRequest>;
		""");

	// Same shape, for the route-marker side: a referenced assembly whose only [Route]-attributed type
	// is internal, so the emitted typeof(...) would be just as illegal.
	static readonly MetadataReference _internalRoutedAssembly = BuildReferenceAssembly(
		"Internal.Routes",
		"""
		using Microsoft.AspNetCore.Components;

		namespace InternalRoutableAsm;

		[Route("/internal")]
		internal sealed class InternalPage;
		""");

	// Carries its own [Route]-attributed type (HomePage) alongside Routes itself, so the exclusion
	// test below actually exercises the exclusion -- if ComponentDiscovery fell back to treating this
	// assembly like any other routable assembly instead of excluding it outright, HomePage would leak
	// into RoutableAssemblyMarkers even though RoutesHolderMarker itself (a different string) would
	// still pass a ShouldNotContain-only check.
	static readonly MetadataReference _routesHolderAssembly = BuildReferenceAssembly(
		"Hosting.Web.Components",
		"""
		using Microsoft.AspNetCore.Components;

		namespace Norse.Hosting.Web.Components;

		[Route("/")]
		public sealed class Routes;

		[Route("/home")]
		public sealed class HomePage;
		""");

	[Fact]
	void Discovers_concrete_validators_in_own_and_referenced_assemblies()
	{
		var compilation = HarnessCompilation(sources: [ValidatorSource], references: [_referencedValidatorAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		// Ascending ordinal by ValidatorTypeName, per ComponentDiscoveryResult's own contract ("ordered
		// by ValidatorTypeName, ordinal") -- "Own" < "Referenced" ordinally, so Own sorts first.
		result.Validators.Select(v => v.ValidatorTypeName)
			.ShouldBe(["global::Own.FakeValidator", "global::Referenced.OtherValidator"]);
		result.Validators.Select(v => v.RequestTypeName)
			.ShouldBe(["global::Own.FakeRequest", "global::Referenced.OtherRequest"]);
	}

	[Fact]
	void Records_one_routable_marker_per_assembly_and_skips_assemblies_without_routes()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly, _plainAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutableAssemblyMarkers.ShouldHaveSingleItem();
		result.RoutableAssemblyMarkers.ShouldContain("global::RoutableAsm.WidgetPage");
	}

	[Fact]
	void Identifies_the_routes_holder_assembly_separately()
	{
		var compilation = HarnessCompilation(references: [_routesHolderAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutesHolderMarker.ShouldBe("global::Norse.Hosting.Web.Components.Routes");
		result.RoutableAssemblyMarkers.ShouldNotContain(result.RoutesHolderMarker);
		result.RoutableAssemblyMarkers.ShouldBeEmpty();
	}

	[Fact]
	void RoutesHolderMarker_is_null_when_Routes_is_unreferenced()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutesHolderMarker.ShouldBeNull();
	}

	[Fact]
	void RoutesAdditionalAssembliesTypeExists_is_false_when_the_type_is_unreferenced()
	{
		var compilation = HarnessCompilation();

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutesAdditionalAssembliesTypeExists.ShouldBeFalse();
	}

	// Task 5's endpoint-discovery exclusion rule (never the compilation's own assembly) needs a
	// marker specifically identifying the compilation's own assembly, distinct from the full
	// RoutableAssemblyMarkers list Task 4/5's Router registration consumes unfiltered.
	[Fact]
	void OwnAssemblyRoutableMarker_identifies_the_route_declared_in_the_compilation_itself()
	{
		const string OwnRoutablePage = """
			using Microsoft.AspNetCore.Components;

			namespace Own;

			[Route("/own")]
			public sealed class OwnPage;
			""";
		var compilation = HarnessCompilation(sources: [OwnRoutablePage], references: [_routableAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.OwnAssemblyRoutableMarker.ShouldBe("global::Own.OwnPage");
		// The router side has no equivalent exclusion -- the own-assembly marker still shows up
		// alongside the referenced assembly's marker in the unfiltered list.
		result.RoutableAssemblyMarkers.ShouldBe(["global::Own.OwnPage", "global::RoutableAsm.WidgetPage"]);
	}

	[Fact]
	void OwnAssemblyRoutableMarker_is_null_when_the_compilation_itself_declares_no_routable_type()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.OwnAssemblyRoutableMarker.ShouldBeNull();
	}

	// The gap OwnAssemblyDeclaresRazorRoutes exists to close: a .razor page in the compilation's own
	// project has no [Route] type for the semantic walk to find (the Razor SDK's generator shares this
	// generator's pass), so the marker stays null and the Router entry has to come from elsewhere.
	[Fact]
	void RequiresOwnAssemblyRouterEntry_when_only_a_razor_page_declares_the_own_assemblys_routes()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: true);

		result.OwnAssemblyRoutableMarker.ShouldBeNull();
		result.RequiresOwnAssemblyRouterEntry.ShouldBeTrue();
	}

	// A C#-declared [Route] type already puts the own assembly in RoutableAssemblyMarkers; a second
	// entry for the same assembly makes Blazor throw on duplicate route discovery.
	[Fact]
	void RequiresOwnAssemblyRouterEntry_is_false_when_a_Route_type_already_represents_the_own_assembly()
	{
		const string OwnRoutablePage = """
			using Microsoft.AspNetCore.Components;

			namespace Own;

			[Route("/own")]
			public sealed class OwnPage;
			""";
		var compilation = HarnessCompilation(sources: [OwnRoutablePage]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: true);

		result.RequiresOwnAssemblyRouterEntry.ShouldBeFalse();
	}

	// Same exclusion the per-assembly semantic walk already applies to the routes-holder assembly: the
	// Router's AppAssembly covers it, so naming it again via AdditionalAssemblies double-discovers.
	[Fact]
	void RequiresOwnAssemblyRouterEntry_is_false_when_the_compilation_itself_holds_Routes()
	{
		const string OwnRoutesHolder = """
			namespace Norse.Hosting.Web.Components;

			public sealed class Routes;
			""";
		var compilation = HarnessCompilation(sources: [OwnRoutesHolder]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: true);

		result.RoutesHolderIsOwnAssembly.ShouldBeTrue();
		result.OwnAssemblyDeclaresRazorRoutes.ShouldBeFalse();
		result.RequiresOwnAssemblyRouterEntry.ShouldBeFalse();
	}

	[Theory]
	// The shape every Blazor page and every project template uses.
	[InlineData("""@page "/Error" """, true)]
	// Leading whitespace is the only thing Razor permits before a directive.
	[InlineData("\t  @page \"/counter/{id:int}\"\n<h1>Counter</h1>", true)]
	// A file may open with a Razor comment before its directives.
	[InlineData("@* the error page *@\n@page \"/Error\"", true)]
	// A second @page (multi-route pages are legal) is found even when the first line is a @using.
	[InlineData("@using System\n@page \"/a\"\n@page \"/b\"", true)]
	// A component, not a page -- the single most common .razor file in any project.
	[InlineData("@inherits LayoutComponentBase\n<div>@Body</div>", false)]
	// "@@" is Razor's escape for a literal '@': this renders the text "@page", declares nothing.
	[InlineData("""@@page "/not-a-route" """, false)]
	// Identifier continuation -- a C# expression rendering a variable named pageSize.
	[InlineData("<p>@pageSize \"items\"</p>", false)]
	// Commented out via a real Razor comment only -- @* *@ is the sole comment form that suppresses
	// directive detection.
	[InlineData("@* @page \"/disabled\" *@", false)]
	// HTML comments do NOT suppress Razor directive processing: the Razor engine's directive scanning
	// is independent of HTML structure, so a @page wrapped in <!-- --> still compiles as a live route
	// and must still be detected -- this is the behavior-reversing case Codex's PR #64 review flagged.
	[InlineData("<!--\n@page \"/disabled\"\n-->", true)]
	// A genuine non-directive HTML comment must not spuriously match now that <!-- --> is no longer
	// treated as an opaque skippable span.
	[InlineData("<!-- TODO: remove this section -->", false)]
	// The word without a route template declares nothing.
	[InlineData("<p>Use @page to declare a route.</p>", false)]
	[InlineData("@page\n", false)]
	// A directive must be the first token on its line.
	[InlineData("<h1>x</h1> @page \"/mid-line\"", false)]
	// An unterminated comment opener must not swallow the directive below it.
	[InlineData("<!-- unclosed\n@page \"/Error\"", true)]
	// Classic-Mac-style CR-only line endings: the main loop must reset atLineStart on '\r' too, not
	// just '\n', or a directive on any line after the first is invisible.
	[InlineData("@using System\r@page \"/a\"", true)]
	void DeclaresRazorRoute_recognizes_only_a_real_page_directive(string razor, bool expected) =>
		ComponentDiscovery.DeclaresRazorRoute(SourceText.From(razor)).ShouldBe(expected);

	// Blazor's second route-declaration form. @page takes a literal template only, so a page routed
	// from a shared const has to say @attribute [Route(...)] -- the standard way to do it, and produced
	// by the same co-resident Razor generator, so it is invisible to the semantic walk in exactly the
	// same way @page is.
	[Theory]
	[InlineData("""@attribute [Route("/counter")] """, true)]
	// The whole reason the form exists: a const template @page cannot accept.
	[InlineData("@attribute [Route(RouteTemplates.Home)]", true)]
	// The explicit attribute-suffixed spelling is equally legal.
	[InlineData("""@attribute [RouteAttribute("/x")] """, true)]
	// C# permits whitespace between an attribute name and its argument-list open paren -- both
	// spellings must still match with a space before "(".
	[InlineData("""@attribute [Route (Routes.Home)] """, true)]
	[InlineData("""@attribute [RouteAttribute ("/x")] """, true)]
	// Namespace-qualified, and alongside another attribute in the same list.
	[InlineData("""@attribute [Microsoft.AspNetCore.Components.Route("/x")] """, true)]
	[InlineData("""@attribute [Authorize, Route("/x")] """, true)]
	// Found after other directives, the way a real page orders them.
	[InlineData("@using System\n@attribute [Route(Routes.Home)]\n<h1>Home</h1>", true)]
	// An @attribute that declares something other than a route contributes none.
	[InlineData("@attribute [Authorize]", false)]
	[InlineData("@attribute [StreamRendering]", false)]
	// An unrelated attribute whose name merely ends in "Route".
	[InlineData("""@attribute [MyRoute("/x")] """, false)]
	// A bare mention with no attribute construction.
	[InlineData("<p>Use @attribute [Route] for const templates.</p>", false)]
	// The attribute-splat expression, which really does sit alone on an indented line in markup (e.g.
	// Himinbjörg's PasskeySubmit.razor) -- "@attributes" only clears "@attribute" by its trailing 's',
	// so the word boundary is what stops it, not luck.
	[InlineData("<FluentButton\n\t@attributes=\"AdditionalAttributes\">x</FluentButton>", false)]
	// Same disciplines the @page form gets: word boundary, escape, line start, comments.
	[InlineData("""@attributeName [Route("/x")] """, false)]
	[InlineData("""@@attribute [Route("/x")] """, false)]
	[InlineData("""<h1>x</h1> @attribute [Route("/x")] """, false)]
	[InlineData("""@* @attribute [Route("/x")] *@""", false)]
	// HTML comments do NOT suppress directive processing -- see the @page case above for why.
	[InlineData("<!--\n@attribute [Route(\"/x\")]\n-->", true)]
	// The attribute must land on the directive's own line.
	[InlineData("@attribute\n[Route(\"/x\")]", false)]
	void DeclaresRazorRoute_recognizes_a_route_declared_by_attribute_directive(string razor, bool expected) =>
		ComponentDiscovery.DeclaresRazorRoute(SourceText.From(razor)).ShouldBe(expected);

	[Fact]
	void Excludes_an_internal_validator_declared_in_a_referenced_assembly()
	{
		var compilation = HarnessCompilation(references: [_internalValidatorAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.ShouldNotContain(v => v.ValidatorTypeName == "global::Referenced.InternalValidator");
	}

	[Fact]
	void Excludes_an_internal_routed_type_declared_in_a_referenced_assembly()
	{
		var compilation = HarnessCompilation(references: [_internalRoutedAssembly]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutableAssemblyMarkers.ShouldBeEmpty();
	}

	[Fact]
	void Excludes_a_validator_whose_only_constructor_is_private()
	{
		const string Source = """
			using FluentValidation;

			namespace Own;

			public sealed record PrivateCtorRequest;

			public sealed class PrivateCtorValidator : AbstractValidator<PrivateCtorRequest>
			{
				PrivateCtorValidator() { } // no accessibility modifier on a class ctor defaults to private
			}
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.ShouldBeEmpty();
	}

	// Distinct from the accessibility exclusion above: this validator's TYPE is perfectly discoverable
	// and referenceable from its own assembly (public class, internal-to-self is fine regardless), but
	// Microsoft.Extensions.DependencyInjection's reflection-based activation only ever sees public
	// constructors -- an explicit internal (or no-modifier, which defaults to private for a class
	// member) constructor is invisible to it. Deliberately an EXPLICIT internal constructor, not a
	// bare "no constructor declared" class: verified against real reflection
	// (Type.GetConstructors()/Activator.CreateInstance) that the C# compiler emits a fully implicit,
	// no-constructor-declared class's default constructor as IL-public regardless of the containing
	// type's own accessibility, so that shape is DI-constructible and must NOT be excluded -- only an
	// explicitly-written non-public constructor actually fails DI resolution.
	[Fact]
	void Excludes_a_validator_whose_only_constructor_is_explicitly_internal()
	{
		const string Source = """
			using FluentValidation;

			namespace Own;

			public sealed record ExplicitInternalCtorRequest;

			public sealed class ExplicitInternalCtorValidator : AbstractValidator<ExplicitInternalCtorRequest>
			{
				internal ExplicitInternalCtorValidator() { }
			}
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.ShouldBeEmpty();
	}

	// The mirror positive case: a validator with no explicit constructor at all gets the compiler's
	// fully implicit default constructor, which is DI-constructible regardless of the type's own
	// accessibility (own-assembly internal is fine per the accessibility guard above) -- this must NOT
	// be excluded by the constructor guard.
	[Fact]
	void Discovers_an_internal_validator_with_no_declared_constructor_at_all()
	{
		const string Source = """
			using FluentValidation;

			namespace Own;

			public sealed record ImplicitCtorRequest;

			internal sealed class ImplicitCtorValidator : AbstractValidator<ImplicitCtorRequest>;
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.Select(v => v.ValidatorTypeName).ShouldContain("global::Own.ImplicitCtorValidator");
	}

	[Fact]
	void Discovers_a_validator_nested_inside_a_partial_class()
	{
		const string Source = """
			using FluentValidation;

			namespace Own;

			public sealed record NestedRequest;

			public partial class Container
			{
				public sealed class NestedValidator : AbstractValidator<NestedRequest>;
			}
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.Select(v => v.ValidatorTypeName).ShouldContain("global::Own.Container.NestedValidator");
	}

	[Fact]
	void Discovers_a_routable_type_nested_inside_a_partial_class()
	{
		const string Source = """
			using Microsoft.AspNetCore.Components;

			namespace Own;

			public partial class PageContainer
			{
				[Route("/nested")]
				public sealed class NestedPage;
			}
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.RoutableAssemblyMarkers.ShouldContain("global::Own.PageContainer.NestedPage");
	}

	// Legal, if unusual, in FluentValidation: one concrete class implementing IValidator<T> for more
	// than one T. Every implemented interface needs its own registration -- FirstOrDefault would
	// silently leave every T past the first unvalidated.
	[Fact]
	void Registers_a_separate_entry_for_each_IValidator_interface_a_validator_implements()
	{
		const string Source = """
			using FluentValidation;
			using FluentValidation.Results;
			using System.Threading;
			using System.Threading.Tasks;

			namespace Own;

			public sealed record RequestA;
			public sealed record RequestB;

			public sealed class MultiValidator : AbstractValidator<RequestA>, IValidator<RequestB>
			{
				public ValidationResult Validate(ValidationContext<RequestB> context) => new();
				public Task<ValidationResult> ValidateAsync(ValidationContext<RequestB> context, CancellationToken cancellation = default) => Task.FromResult(new ValidationResult());
			}
			""";
		var compilation = HarnessCompilation(sources: [Source]);

		var result = ComponentDiscovery.Discover(compilation, ownAssemblyDeclaresRazorRoutes: false);

		result.Validators.ShouldContain(v =>
			v.ValidatorTypeName == "global::Own.MultiValidator" && v.RequestTypeName == "global::Own.RequestA");
		result.Validators.ShouldContain(v =>
			v.ValidatorTypeName == "global::Own.MultiValidator" && v.RequestTypeName == "global::Own.RequestB");
		result.Validators.Count(v => v.ValidatorTypeName == "global::Own.MultiValidator").ShouldBe(2);
	}

	static MetadataReference BuildReferenceAssembly(string assemblyName, string source) =>
		CSharpCompilation.Create(
				assemblyName,
				[CSharpSyntaxTree.ParseText(source)],
				_extraReferences,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.ToMetadataReference();

	static Compilation HarnessCompilation(string[]? sources = null, MetadataReference[]? references = null) =>
		CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. (sources ?? []).Select(s => CSharpSyntaxTree.ParseText(s))],
			[.. _extraReferences, .. references ?? []],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
