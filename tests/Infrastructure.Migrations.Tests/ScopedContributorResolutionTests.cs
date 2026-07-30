using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Migrations;
using Norse.Abstractions.Migrations.Seeding;

namespace Norse.Infrastructure.Migrations.Tests;

/// <summary>
/// Regression coverage for the captive-dependency crash: contributors resolve from a scoped call
/// site (an <c>EfMigrationContributor&lt;TContext&gt;</c> is registered transient but takes a scoped
/// <c>DbContext</c>), so a singleton <see cref="IHostedService"/> that injects them directly is
/// rejected the moment scope validation is on — which is exactly what Aspire hands its children.
/// </summary>
public sealed class ScopedContributorResolutionTests
{
	[Fact]
	void Migrations_runner_builds_when_contributors_resolve_from_a_scoped_call_site()
	{
		var builder = CreateValidatingBuilder();
		builder.Services.AddSingleton<ExecutionLog>();
		builder.Services.AddScoped<ScopedContext>();
		builder.Services.AddTransient<IMigrationContributor, ScopedMigrationContributor>();
		builder.AddNorseMigrationsRunner();

		using var host = builder.Build();

		host.Services.GetRequiredService<IHostedService>().ShouldBeOfType<MigrationRunnerService>();
	}

	[Fact]
	void Seeding_runner_builds_when_contributors_resolve_from_a_scoped_call_site()
	{
		var builder = CreateValidatingBuilder();
		builder.Services.AddSingleton<ExecutionLog>();
		builder.Services.AddScoped<ScopedContext>();
		builder.Services.AddTransient<ISeedContributor, ScopedSeedContributor>();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();

		host.Services.GetRequiredService<IHostedService>().ShouldBeOfType<SeedRunnerService>();
	}

	[Fact]
	async Task Scoped_contributors_still_run_when_the_host_starts()
	{
		var builder = CreateValidatingBuilder();
		builder.Services.AddSingleton<ExecutionLog>();
		builder.Services.AddScoped<ScopedContext>();
		builder.Services.AddTransient<IMigrationContributor, ScopedMigrationContributor>();
		builder.Services.AddTransient<ISeedContributor, ScopedSeedContributor>();
		builder.AddNorseMigrationsRunner();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();
		await host.StartAsync(TestContext.Current.CancellationToken);

		host.Services.GetRequiredService<ExecutionLog>().Entries.ShouldBe(["migration", "seed"]);
	}

	static HostApplicationBuilder CreateValidatingBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		ServiceProviderOptions options = new()
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		};
		DefaultServiceProviderFactory factory = new(options);
		builder.ConfigureContainer(factory);
		return builder;
	}

	sealed class ExecutionLog
	{
		readonly ConcurrentQueue<string> _entries = new();

		public IReadOnlyList<string> Entries => [.. _entries];

		public void Add(string entry) => _entries.Enqueue(entry);
	}

	sealed class ScopedContext
	{
		public Guid InstanceId { get; } = Guid.NewGuid();
	}

	sealed class ScopedMigrationContributor(ScopedContext context, ExecutionLog log) : IMigrationContributor
	{
		public string Name => $"Migration {context.InstanceId}";

		public Task MigrateAsync(CancellationToken cancellationToken)
		{
			log.Add("migration");
			return Task.CompletedTask;
		}
	}

	sealed class ScopedSeedContributor(ScopedContext context, ExecutionLog log) : ISeedContributor
	{
		public string Name => $"Seed {context.InstanceId}";

		public Task SeedAsync(CancellationToken cancellationToken)
		{
			log.Add("seed");
			return Task.CompletedTask;
		}
	}
}
