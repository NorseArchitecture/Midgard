using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.ServiceDefaults.Tests;

public sealed class HostApplicationBuilderExtensionsTests
{
	[Fact]
	void Add_default_health_checks_registers_the_self_liveness_check()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddDefaultHealthChecks();
		using var host = builder.Build();
		var registration = host.Services
			.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldHaveSingleItem();
		registration.Name.ShouldBe("self");
		registration.Tags.ShouldContain("live");
	}
}
