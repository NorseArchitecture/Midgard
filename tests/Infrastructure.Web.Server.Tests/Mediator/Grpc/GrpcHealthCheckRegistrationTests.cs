using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class GrpcHealthCheckRegistrationTests
{
	[Fact]
	void Code_first_grpc_registration_bridges_health_results_to_grpc()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseCodeFirstGrpc();
		using var provider = services.BuildServiceProvider();
		provider.GetServices<IHealthCheckPublisher>().ShouldNotBeEmpty();
	}

	[Fact]
	void Code_first_grpc_registration_adds_no_health_check_of_its_own()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseCodeFirstGrpc();
		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldBeEmpty();
	}
}
