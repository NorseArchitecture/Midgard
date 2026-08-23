using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.OpenApi;

namespace Norse.Infrastructure.Web.Server.Tests.OpenApi;

/// <summary>
///     Proves the bearer security scheme lands on a real generated document, and only on operations
///     whose declaring controller derives from <see cref="GrpcControllerBase" /> — the same "call the
///     real ASP.NET Core OpenAPI pipeline, never a stand-in" idiom <c>TransformerTests</c> uses.
/// </summary>
public sealed class MachineAuthTransformerTests
{
	[Fact]
	async Task The_bearer_scheme_component_is_registered_once()
	{
		var document = await BuildDocumentAsync();

		var scheme = document["components"]!["securitySchemes"]!["Bearer"]!;
		scheme["type"]!.GetValue<string>().ShouldBe("http");
		scheme["scheme"]!.GetValue<string>().ShouldBe("bearer");
		scheme["bearerFormat"]!.GetValue<string>().ShouldBe("JWT");
	}

	[Fact]
	async Task A_facade_operation_carries_the_bearer_security_requirement()
	{
		var document = await BuildDocumentAsync();

		var security = document["paths"]!["/facade"]!["get"]!["security"]!.AsArray();
		security.Count.ShouldBe(1);
		security[0]!.AsObject().ContainsKey("Bearer").ShouldBeTrue();
	}

	[Fact]
	async Task A_non_facade_operation_carries_no_security_requirement()
	{
		var document = await BuildDocumentAsync();

		document["paths"]!["/plain"]!["get"]!.AsObject().ContainsKey("security").ShouldBeFalse();
	}

	static async Task<JsonNode> BuildDocumentAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers()
			.ConfigureApplicationPartManager(manager =>
			{
				for (var i = manager.FeatureProviders.Count - 1; i >= 0; i--)
					if (manager.FeatureProviders[i] is ControllerFeatureProvider)
						manager.FeatureProviders.RemoveAt(i);

				manager.FeatureProviders.Add(new OnlyMachineAuthFixtureControllersFeatureProvider());
			});
		builder.Services.AddOpenApi(options =>
		{
			options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
			options.AddOperationTransformer<MachineAuthOperationTransformer>();
		});

		await using var app = builder.Build();
		app.MapOpenApi();
		app.MapControllers();

		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative),
			TestContext.Current.CancellationToken);
		var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		await app.StopAsync(TestContext.Current.CancellationToken);

		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(json);

		return JsonNode.Parse(json)!;
	}
}

sealed class OnlyMachineAuthFixtureControllersFeatureProvider : ControllerFeatureProvider
{
	protected override bool IsController(TypeInfo typeInfo) =>
		typeInfo == typeof(FacadeFixtureController).GetTypeInfo() ||
		typeInfo == typeof(PlainFixtureController).GetTypeInfo();
}

[Route("facade")]
sealed class FacadeFixtureController : GrpcControllerBase
{
#pragma warning disable CA1822 // ASP.NET Core actions must be instance methods.
	[HttpGet]
	public Task<ActionResult<string>> Get() =>
		FoldAsync(new ValueTask<Outcome<string>>(Outcome<string>.Ok("ok")));
#pragma warning restore CA1822
}

[ApiController]
[Route("plain")]
sealed class PlainFixtureController : ControllerBase
{
#pragma warning disable CA1822
	[HttpGet]
	public ActionResult<string> Get() => Ok("ok");
#pragma warning restore CA1822
}
