using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
///     Proves the JSON channel emits the identical <c>errors</c> payload shape the XML channel does (spec
///     §11.1, §15's tri-protocol swoop doctrine) — <c>[{path, detail}]</c>, never
///     <c>ValidationProblemDetails</c>' <c>IDictionary&lt;string, string[]&gt;</c>. Serializes with the same
///     camelCase policy ASP.NET Core's default <c>JsonOptions</c> applies to every MVC JSON response, so
///     this is proof about the real wire shape, not merely about the CLR type.
/// </summary>
public sealed class ProblemDetailsJsonParityTests
{
	[Fact]
	void The_errors_extension_serializes_as_a_flattened_path_detail_array_not_a_dictionary()
	{
		ProblemDetails problem = new() { Title = "Validation", Status = 400 };
		problem.Extensions["errors"] = new[]
		{
			new ProblemErrorEntry("Policy/@birthDate", "malformed DateOnly"),
			new ProblemErrorEntry("Policy/Coverage[2]/@limit", "malformed decimal")
		};

		var json = JsonSerializer.Serialize(problem, CamelCaseOptions());

		json.ShouldContain(
			"""errors":[{"path":"Policy/@birthDate","detail":"malformed DateOnly"},{"path":"Policy/Coverage[2]/@limit","detail":"malformed decimal"}]""");
	}

	[Fact]
	void Extension_members_serialize_flattened_at_the_top_level_not_nested_under_an_Extensions_property()
	{
		ProblemDetails problem = new() { Title = "Fault", Status = 500 };
		problem.Extensions["correlationId"] = Guid.Parse("0b917371-1111-2222-3333-444455556666");

		var json = JsonSerializer.Serialize(problem, CamelCaseOptions());

		json.ShouldContain(""""correlationId":"0b917371-1111-2222-3333-444455556666"""");
		json.ShouldNotContain("\"extensions\"");
	}

	static JsonSerializerOptions CamelCaseOptions() =>
		new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
