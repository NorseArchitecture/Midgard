using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Three throwaway synthetic schemas, local to this file — deliberately plain DbContext, not
// NorseDbContext: AddWell<TContext> only constrains TContext : DbContext, and staying off
// NorseDbContext sidesteps EntityConfigurationApplicationGenerator's single-Tier1-context-per-
// compilation discovery (WellContext.cs already claims that slot) along with every mirror-law
// convention irrelevant to these three narrow shapes. Never connected — "Host=unused" never gets
// dialed, since AddWellTests only ever touches DbContext.Model, which EF builds without opening a
// connection (same load-bearing fact WellContext.cs and KeyPredicateTests.cs already rely on).

sealed record BrokenView
{
	public required Guid Id { get; init; }
}

// Price is a declared, non-FK, non-[NotProjected] scalar with no BrokenView counterpart —
// the exact shape A_missing_scalar_pair_throws_at_startup_naming_the_member exercises.
sealed record BrokenEntity : IViewBearer<BrokenView>
{
	public required Guid Id { get; init; }
	public required decimal Price { get; init; }
	public required BrokenView View { get; init; }
}

sealed class BrokenMirrorContext(DbContextOptions<BrokenMirrorContext> options) : DbContext(options)
{
	public DbSet<BrokenEntity> Brokens => Set<BrokenEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<BrokenEntity>(broken =>
		{
			broken.HasKey(e => e.Id);
			broken.OwnsOne(e => e.View, view => view.ToJson());
		});
	}
}

sealed record ExemptView
{
	public required Guid Id { get; init; }
}

// RowStamp carries [NotProjected] and deliberately has no ExemptView counterpart — legal under the
// mirror law's declared exception, exercised by A_not_projected_scalar_is_exempt_from_the_mirror_law.
sealed record ExemptEntity : IViewBearer<ExemptView>
{
	public required Guid Id { get; init; }
	[NotProjected]
	public required byte[] RowStamp { get; init; }
	public required ExemptView View { get; init; }
}

sealed class ExemptContext(DbContextOptions<ExemptContext> options) : DbContext(options)
{
	public DbSet<ExemptEntity> Exempts => Set<ExemptEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<ExemptEntity>(exempt =>
		{
			exempt.HasKey(e => e.Id);
			exempt.OwnsOne(e => e.View, view => view.ToJson());
		});
	}
}

// Two unrelated, otherwise-unremarkable roots both claiming WidgetView (from WellContext.cs) — the
// collision fires from AddWell's own CLR scan, before any DbContext is ever created, so neither
// entity needs real EF configuration for Two_entities_claiming_the_same_view_throw_at_startup to pass.
sealed record DuplicateEntityA : IViewBearer<WidgetView>
{
	public required Guid Id { get; init; }
	public required WidgetView View { get; init; }
}

sealed record DuplicateEntityB : IViewBearer<WidgetView>
{
	public required Guid Id { get; init; }
	public required WidgetView View { get; init; }
}

sealed class DuplicateViewContext(DbContextOptions<DuplicateViewContext> options) : DbContext(options)
{
	public DbSet<DuplicateEntityA> As => Set<DuplicateEntityA>();
	public DbSet<DuplicateEntityB> Bs => Set<DuplicateEntityB>();
}

public sealed class AddWellTests
{
	static ServiceProvider Build<TContext>() where TContext : DbContext
	{
		ServiceCollection services = new();
		services.AddDbContextFactory<TContext>(o => o.UseNpgsql("Host=unused"));
		services.AddWell<TContext>();
		return services.BuildServiceProvider();
	}

	[Fact]
	void AddWell_registers_a_read_repository_per_view_bearer()
	{
		using var provider = Build<WellContext>();
		provider.GetRequiredService<IReadRepository<WidgetView>>().ShouldBeOfType<Repository<WellContext, WidgetEntity, WidgetView>>();
	}

	[Fact]
	void A_missing_scalar_pair_throws_at_startup_naming_the_member()
	{
		var resolve = () => Build<BrokenMirrorContext>().GetRequiredService<IReadRepository<BrokenView>>();
		resolve.ShouldThrow<InvalidOperationException>().Message.ShouldContain(nameof(BrokenEntity.Price));
	}

	[Fact]
	void A_not_projected_scalar_is_exempt_from_the_mirror_law()
	{
		using var provider = Build<ExemptContext>();
		provider.GetRequiredService<IReadRepository<ExemptView>>().ShouldNotBeNull();
	}

	[Fact]
	void Two_entities_claiming_the_same_view_throw_at_startup()
	{
		var resolve = () => Build<DuplicateViewContext>().GetRequiredService<IReadRepository<WidgetView>>();
		resolve.ShouldThrow<InvalidOperationException>().Message.ShouldContain(nameof(WidgetView));
	}
}
