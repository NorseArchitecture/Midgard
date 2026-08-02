using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Builds the <c>[ApiController]</c> automatic-400 response for an invalid <see cref="ActionContext.ModelState"/>
/// — wired as <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c> by
/// <see cref="MvcBuilderExtensions.AddNorseXml"/>. Deliberately never <c>ValidationProblemDetails</c>:
/// its <c>Errors</c> shape is <c>IDictionary&lt;string, string[]&gt;</c>, and Futhark spec §11.1 rejects
/// that shape for the same reason Asgard's <c>GrpcControllerBase.FoldAsync</c> does — paths repeat, and a
/// dictionary needs value-array ceremony for no benefit. This factory renders the identical
/// <see cref="ProblemErrorEntry"/> <c>[{path, detail}]</c> array both code paths use, so a client sees
/// one <c>errors</c> shape regardless of whether the 400 came from MVC model binding or a failed
/// <c>Outcome&lt;T&gt;</c>. Explicitly sets <see cref="ObjectResult.ContentTypes"/> to the RFC 9457 media
/// types — <c>GrpcControllerBase</c>'s class-level <c>[Produces("application/json", "application/xml")]</c>
/// would otherwise lock every response, including this one, to the plain (non-problem) media types, and
/// <see cref="XmlContractOutputFormatter"/> has no shape registered for <see cref="ProblemDetails"/> and
/// would throw if content negotiation ever handed it one.
/// </summary>
static class InvalidModelStateProblemFactory
{
	/// <summary>Renders <paramref name="context"/>'s accumulated <see cref="ActionContext.ModelState"/> failures as a problem+json/problem+xml 400.</summary>
	public static IActionResult Create(ActionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		List<ProblemErrorEntry> errors = [];
		foreach (var (path, entry) in context.ModelState)
		{
			if (entry is null)
				continue;

			foreach (var error in entry.Errors)
				errors.Add(new ProblemErrorEntry(path, DetailFor(error)));
		}

		ProblemDetails problem = new()
		{
			Title = "One or more validation errors occurred.",
			Status = StatusCodes.Status400BadRequest
		};
		problem.Extensions["errors"] = errors;

		ObjectResult result = new(problem) { StatusCode = StatusCodes.Status400BadRequest };
		result.ContentTypes.Add("application/problem+json");
		result.ContentTypes.Add("application/problem+xml");
		return result;
	}

	static string DetailFor(ModelError error) =>
		string.IsNullOrEmpty(error.ErrorMessage) ? error.Exception?.Message ?? "invalid value" : error.ErrorMessage;
}
