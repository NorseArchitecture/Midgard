using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Backend;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

public sealed class AddNorseWellTests
{
	[Fact]
	void AddNorseWell_registers_both_the_context_factory_and_the_repository()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";

		builder.AddNorseWell<WellContext>(NorsePostgresEfProvider.Instance, "test");
		using var host = builder.Build();

		host.Services.GetRequiredService<IDbContextFactory<WellContext>>().ShouldNotBeNull();
		host.Services.GetRequiredService<IReadRepository<WidgetView>>().ShouldNotBeNull();
	}
}
