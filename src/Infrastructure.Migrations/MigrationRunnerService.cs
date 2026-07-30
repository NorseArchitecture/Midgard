using Norse.Abstractions.Migrations;

namespace Norse.Infrastructure.Migrations;

sealed partial class MigrationRunnerService(
	IServiceScopeFactory scopeFactory,
	ILogger<MigrationRunnerService> logger) : IHostedService
{
	// Contributors resolve from a scoped call site — EfMigrationContributor<TContext> is registered
	// transient but takes a scoped DbContext — so injecting IEnumerable<IMigrationContributor>
	// straight into this singleton is a captive dependency that scope validation rejects at build.
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var scope = scopeFactory.CreateAsyncScope();
		await using (scope.ConfigureAwait(false))
		{
			var contributors = scope.ServiceProvider.GetServices<IMigrationContributor>();
			await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken))).ConfigureAwait(false);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) =>
		Task.CompletedTask;

	async Task RunAsync(IMigrationContributor contributor, CancellationToken ct)
	{
		LogStarting(logger, contributor.Name);
		await contributor.MigrateAsync(ct).ConfigureAwait(false);
		LogCompleted(logger, contributor.Name);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting migration contributor {Name}")]
	static partial void LogStarting(ILogger logger, string name);

	[LoggerMessage(Level = LogLevel.Information, Message = "Migration contributor {Name} completed")]
	static partial void LogCompleted(ILogger logger, string name);
}
