using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Abstractions.Backend;
using Norse.Persistence.EntityFramework;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Mirror-law-conformant synthetic pair, real EF-mapped shapes this time (SyntheticWell.cs's
// PolicyEntity/PolicyView are plain records with no EF configuration at all, sufficient for
// PredicateRewriterTests' pure expression-tree assertions but not for a real query against
// Postgres). CustomerId is a promoted, indexed scalar; Tags is a promoted collection retargeting
// to a real child table; Notes and Name are view-extra (residual, JSON-path) members with no
// entity counterpart.
sealed record WidgetTagView
{
	public required string Label { get; init; }
}

sealed record WidgetTagEntity : NorseEntityBase<WidgetTagEntity>, INorseEntity<WidgetTagEntity>
{
	public required Guid Id { get; init; }
	public required Guid WidgetId { get; init; }
	public required string Label { get; init; }

	public static void Configure(EntityTypeBuilder<WidgetTagEntity> builder)
	{
		builder.HasKey(t => t.Id);
		builder.Property(t => t.Label).HasMaxLength(64);
	}
}

sealed record WidgetView
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public required string Name { get; init; }
	public string? Notes { get; init; }
	public IReadOnlyList<WidgetTagView> Tags { get; init; } = [];
}

sealed record WidgetEntity : NorseEntityBase<WidgetEntity>, IViewBearer<WidgetView>, INorseEntity<WidgetEntity>
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public ICollection<WidgetTagEntity> Tags { get; init; } = [];
	public required WidgetView View { get; init; }

	public static void Configure(EntityTypeBuilder<WidgetEntity> builder)
	{
		builder.HasKey(w => w.Id);
		builder.Property(w => w.CustomerId).HasMaxLength(64);
		builder.HasIndex(w => w.CustomerId);
		builder.HasMany(w => w.Tags).WithOne().HasForeignKey(t => t.WidgetId);
		builder.OwnsOne(w => w.View, view =>
		{
			view.ToJson();
			view.OwnsMany(v => v.Tags);
		});
	}
}

// Test-only synthetic schema — no migrations project exists for it (a real migrations chassis for
// a throwaway integration-test model would be scope creep against the well-and-wire slice this
// task proves out); PostgresContainerFixture stands up its schema via EnsureCreatedAsync instead
// of EfMigrationContributor<TContext>.MigrateAsync, a deliberate, documented deviation from every
// other real Norse DbContext's schema-creation path. Declared `partial`: Urðarbrunnr's
// EntityConfigurationApplicationGenerator emits the second partial declaration overriding
// ConfigureNorseEntities once it finds this class (a partial NorseDbContext subclass) alongside
// WidgetEntity/WidgetTagEntity in this project's own compilation.
sealed partial class WellContext(DbContextOptions<WellContext> options) : NorseDbContext(options)
{
	// AddWell<TContext>'s discovery law (Task 5) scans public DbSet<TEntity> properties by CLR
	// reflection, not the built EF model — RepositoryReadTests/PostgresContainerFixture never needed
	// this accessor (they go through context.Set<WidgetEntity>() directly), but AddWellTests does.
	public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();
}
