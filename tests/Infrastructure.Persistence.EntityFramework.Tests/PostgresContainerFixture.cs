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
/// Notes "hot"); "gamma" deliberately shares alpha's CustomerId "C1", held in reserve for Task 4's
/// <c>SingleAsync</c> MultipleMatches case; "beta" (CustomerId "C2") is the odd one out.
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
		context.AddRange(
			new WidgetEntity
			{
				Id = KnownWidgetId,
				CustomerId = "C1",
				Tags = [new() { Id = Guid.NewGuid(), WidgetId = KnownWidgetId, Label = "featured" }],
				View = new WidgetView
				{
					Id = KnownWidgetId,
					CustomerId = "C1",
					Name = "alpha",
					Notes = "hot",
					Tags = [new() { Label = "featured" }],
				},
			},
			new WidgetEntity
			{
				Id = betaId,
				CustomerId = "C2",
				View = new WidgetView { Id = betaId, CustomerId = "C2", Name = "beta", Notes = "cold" },
			},
			new WidgetEntity
			{
				Id = gammaId,
				CustomerId = "C1",
				View = new WidgetView { Id = gammaId, CustomerId = "C1", Name = "gamma", Notes = "cold" },
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
