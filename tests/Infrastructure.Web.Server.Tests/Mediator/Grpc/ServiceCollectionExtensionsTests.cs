using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ServiceCollectionExtensionsTests
{
	[Fact]
	void AddNorseCodeFirstGrpc_registers_all_three_interceptors_in_net_order()
	{
		ServiceCollection services = new();

		services.AddNorseCodeFirstGrpc();

		using var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

		options.Interceptors.Select(registration => registration.Type).ShouldBe(
		[
			typeof(UnhandledExceptionInterceptor),
			typeof(PrincipalSeedingInterceptor),
			typeof(OutcomeServerInterceptor)
		]);
	}
}
