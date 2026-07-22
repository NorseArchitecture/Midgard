using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Migrations;
using Norse.Abstractions.Migrations.Seeding;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationsAndSeedingOrderingTests
{
	[Fact]
	async Task Host_runs_every_migration_to_completion_before_any_seed_contributor_starts()
	{
		List<string> executionLog = [];

		var migrationContributor = Substitute.For<IMigrationContributor>();
		migrationContributor.Name.Returns("Migration");
		migrationContributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				executionLog.Add("migration");
				return Task.CompletedTask;
			});

		var seedContributor = Substitute.For<ISeedContributor>();
		seedContributor.Name.Returns("Seed");
		seedContributor.SeedAsync(Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				executionLog.Add("seed");
				return Task.CompletedTask;
			});

		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(migrationContributor);
		builder.Services.AddSingleton(seedContributor);
		builder.AddNorseMigrationsRunner();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();
		await host.StartAsync(CancellationToken.None);

		executionLog.ShouldBe(["migration", "seed"]);
	}

	[Fact]
	async Task Host_with_no_seed_contributors_still_runs_migrations_and_completes()
	{
		var migrationContributor = Substitute.For<IMigrationContributor>();
		migrationContributor.Name.Returns("Migration");
		migrationContributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(migrationContributor);
		builder.AddNorseMigrationsRunner();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();
		await host.StartAsync(CancellationToken.None);

		await migrationContributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}
}
