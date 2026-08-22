using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class HealthEndpointPolicyTests
{
	[Fact]
	async Task Health_endpoints_declare_the_probe_policy_and_no_anonymity_exemption()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAspNetServiceDefaults();
		await using var app = builder.Build();
		app.MapDefaultEndpoints();
		await app.StartAsync(TestContext.Current.CancellationToken);

		var health = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).ToArray();

		health.Length.ShouldBe(2);
		foreach (var endpoint in health)
		{
			endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
			endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
				.ShouldContain(data => data.Policy == NorsePolicies.Probe);
		}
	}
}
