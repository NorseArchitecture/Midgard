using Norse.Abstractions.Migrations.Seeding;

namespace Norse.Infrastructure.Migrations;

sealed partial class SeedRunnerService(
	IServiceScopeFactory scopeFactory,
	IHostApplicationLifetime lifetime,
	ILogger<SeedRunnerService> logger) : IHostedService
{
	// Contributors resolve from a scoped call site — an EF seed contributor is registered transient
	// but takes a scoped DbContext — so injecting IEnumerable<ISeedContributor> straight into this
	// singleton is a captive dependency that scope validation rejects at build.
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var scope = scopeFactory.CreateAsyncScope();
		await using (scope.ConfigureAwait(false))
		{
			var contributors = scope.ServiceProvider.GetServices<ISeedContributor>();
			await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken))).ConfigureAwait(false);
		}

		lifetime.StopApplication();
	}

	public Task StopAsync(CancellationToken cancellationToken) =>
		Task.CompletedTask;

	async Task RunAsync(ISeedContributor contributor, CancellationToken ct)
	{
		LogStarting(logger, contributor.Name);
		await contributor.SeedAsync(ct).ConfigureAwait(false);
		LogCompleted(logger, contributor.Name);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting seed contributor {Name}")]
	static partial void LogStarting(ILogger logger, string name);

	[LoggerMessage(Level = LogLevel.Information, Message = "Seed contributor {Name} completed")]
	static partial void LogCompleted(ILogger logger, string name);
}
