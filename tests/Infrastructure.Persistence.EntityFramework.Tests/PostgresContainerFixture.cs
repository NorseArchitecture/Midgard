using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;
using Testcontainers.PostgreSql;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

/// <summary>
/// Stands up a real Postgres container and a synthetic three-widget well against
/// <see cref="WellContext"/> — beyond the container-lifecycle shape shared with every other
/// realm's Postgres fixture, this one also owns schema creation and seeding, since
/// <see cref="RepositoryReadTests"/> exercises the repository against real data rather than an
/// empty database. <see cref="KnownWidgetId"/> names the seeded "alpha" widget (CustomerId "C1",
/// EffectiveDate 2026-01-01, Notes "hot", Labels ["featured","new"]); "gamma" deliberately shares
/// alpha's CustomerId "C1" and EffectiveDate, held in reserve for Task 4's <c>SingleAsync</c>
/// MultipleMatches case and Task 6's anchored-composite seek-verification case (differentiated
/// from alpha only by the residual Notes leg); "beta" (CustomerId "C2") is the odd one out.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_well")
		.Build();

	// internal, not public: WellContext (and the Widget* shapes it maps) are internal — the least
	// accessible shape that still lets every test in this assembly consume the fixture.
	// null! justified: hydrated by InitializeAsync before xUnit hands the fixture to any test.
	internal IDbContextFactory<WellContext> ContextFactory { get; private set; } = null!;

	/// <summary>The seeded "alpha" widget's id — CustomerId "C1", Name "alpha", Notes "hot".</summary>
	public Guid KnownWidgetId { get; } = Guid.NewGuid();

	/// <summary>
	/// The live container's connection string — Task 6's canary/seek-verification tests need their
	/// own <see cref="DbContextOptionsBuilder{TContext}"/> per test (wired with <c>LogTo</c> into a
	/// test-owned log), so they cannot ride <see cref="ContextFactory"/>'s already-fixed options.
	/// </summary>
	public string ConnectionString => _container.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();

		DbContextOptionsBuilder<WellContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance, _container.GetConnectionString(), migrationsAssemblyName: null);
		ContextFactory = new WellContextFactory(optionsBuilder.Options);

		await using var context = await ContextFactory.CreateDbContextAsync();
		// No EfMigrationContributor<WellContext> exists for this synthetic test-only schema — see
		// WellContext.cs's remarks. EnsureCreatedAsync is the pragmatic substitute for a throwaway
		// integration-test model.
		await context.Database.EnsureCreatedAsync();
		await SeedAsync(context);
	}

	async Task SeedAsync(WellContext context)
	{
		var betaId = Guid.NewGuid();
		var gammaId = Guid.NewGuid();
		var alphaEffectiveDate = new DateOnly(2026, 1, 1);
		context.AddRange(
			new WidgetEntity
			{
				Id = KnownWidgetId,
				CustomerId = "C1",
				EffectiveDate = alphaEffectiveDate,
				Tags = [new() { Id = Guid.NewGuid(), WidgetId = KnownWidgetId, Label = "featured" }],
				View = new WidgetView
				{
					Id = KnownWidgetId,
					CustomerId = "C1",
					EffectiveDate = alphaEffectiveDate,
					Name = "alpha",
					Notes = "hot",
					Tags = [new() { Label = "featured" }],
					Labels = ["featured", "new"],
				},
			},
			new WidgetEntity
			{
				Id = betaId,
				CustomerId = "C2",
				EffectiveDate = new DateOnly(2026, 2, 1),
				View = new WidgetView
				{
					Id = betaId,
					CustomerId = "C2",
					EffectiveDate = new DateOnly(2026, 2, 1),
					Name = "beta",
					Notes = "cold",
				},
			},
			new WidgetEntity
			{
				Id = gammaId,
				CustomerId = "C1",
				EffectiveDate = alphaEffectiveDate,
				View = new WidgetView
				{
					Id = gammaId,
					CustomerId = "C1",
					EffectiveDate = alphaEffectiveDate,
					Name = "gamma",
					Notes = "cold",
					Labels = ["legacy"],
				},
			});
		await context.SaveChangesAsync();
	}

	public ValueTask DisposeAsync() =>
		_container.DisposeAsync();

	sealed class WellContextFactory(DbContextOptions<WellContext> options) : IDbContextFactory<WellContext>
	{
		public WellContext CreateDbContext() =>
			new(options);
	}
}
