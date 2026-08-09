using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
///     Tests <see cref="InvalidModelStateProblemFactory" /> — the <c>[ApiController]</c> automatic-400
///     factory that must render the identical <c>[{path, detail}]</c> shape (<see cref="ProblemErrorEntry" />)
///     as <c>GrpcControllerBase.FoldAsync</c> (Asgard) and negotiate to the RFC 9457 problem media types, not
///     the plain ones the class-level <c>[Produces]</c> would otherwise back-fill.
/// </summary>
public sealed class InvalidModelStateProblemFactoryTests
{
	[Fact]
	void Renders_ModelState_failures_as_the_flattened_path_detail_array_and_negotiates_to_the_problem_media_types()
	{
		ModelStateDictionary modelState = new();
		modelState.AddModelError("Policy/@birthDate", "cannot parse 'x' as DateOnly");
		modelState.AddModelError("Policy/Coverage[2]/@limit", "cannot parse 'y' as decimal");
		modelState.AddModelError("Policy/Coverage[2]/@limit", "value out of range");
		ActionContext context = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);

		var result = InvalidModelStateProblemFactory.Create(context).ShouldBeOfType<ObjectResult>();

		result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
		result.ContentTypes.ShouldBe(["application/problem+json", "application/problem+xml"]);
		var problem = result.Value.ShouldBeOfType<ProblemDetails>();
		problem.Status.ShouldBe(StatusCodes.Status400BadRequest);
		var entries = problem.Extensions["errors"].ShouldBeAssignableTo<IEnumerable<ProblemErrorEntry>>()
			.ShouldNotBeNull().ToArray();
		entries.Length.ShouldBe(3);
		entries.ShouldContain(new ProblemErrorEntry("Policy/@birthDate", "cannot parse 'x' as DateOnly"));
		entries.ShouldContain(new ProblemErrorEntry("Policy/Coverage[2]/@limit", "cannot parse 'y' as decimal"));
		entries.ShouldContain(new ProblemErrorEntry("Policy/Coverage[2]/@limit", "value out of range"));
	}

	[Fact]
	void A_key_with_no_recorded_errors_contributes_no_entries()
	{
		ModelStateDictionary modelState = new();
		modelState.AddModelError("Policy/Coverage[2]/@limit", "cannot parse 'y' as decimal");
		modelState.MarkFieldSkipped("Policy/@birthDate"); // valid on its own — a key present with zero errors
		ActionContext context = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);

		var result = InvalidModelStateProblemFactory.Create(context).ShouldBeOfType<ObjectResult>();

		var problem = result.Value.ShouldBeOfType<ProblemDetails>();
		var entries = ((IEnumerable<ProblemErrorEntry>)problem.Extensions["errors"]!).ToArray();
		entries.Length.ShouldBe(1);
		entries[0].Path.ShouldBe("Policy/Coverage[2]/@limit");
	}

	[Fact]
	void Throws_on_a_null_context() =>
		Should.Throw<ArgumentNullException>(() => InvalidModelStateProblemFactory.Create(null!));
}
