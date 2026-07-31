using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// The one public member of this project (well-and-wire spec §5, smallest-footprint law) —
/// <see cref="Repository{TContext,TEntity,TView}"/>, <see cref="WellMap"/>, and
/// <see cref="WellValidation"/> all stay internal.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// A well that models its entities correctly gets its repositories by existing: scans
	/// <c>TContext</c>'s public <see cref="DbSet{TEntity}"/> properties for
	/// <see cref="IViewBearer{TView}"/> implementors — DbSet-rooted-ness is the discovery law, so a
	/// view-bearing entity reachable only by navigation is not a well root and gets no repository —
	/// and registers a singleton <see cref="IReadRepository{TView}"/> per pair. Two roots claiming
	/// the same view throw immediately, from the CLR scan itself; the total-mirror law
	/// (<see cref="WellValidation"/>) is deferred into each singleton's factory closure and
	/// validated once, at first resolution, since it needs a live <see cref="DbContext.Model"/> that
	/// discovery cannot wait for MS DI's own closed-service-type requirement to produce.
	/// </summary>
	[RequiresUnreferencedCode("Scans TContext's public DbSet<TEntity> properties and each entity's interfaces via runtime reflection to discover well roots, then dispatches to RegisterCore<TContext,TEntity,TView> — the well-and-wire mirror law (spec §4.2) guarantees the shapes are valid at runtime, but neither the discovery scan nor the generic dispatch is statically provable to the trimmer.")]
	[RequiresDynamicCode("Closes RegisterCore<TContext,TEntity,TView> via MakeGenericMethod over CLR types discovered at startup by reflection; a well's entity/view pairs are not known at compile time, so ahead-of-time generic instantiation cannot cover them.")]
	public static IServiceCollection AddWell<TContext>(this IServiceCollection services) where TContext : DbContext
	{
		// Looked up fresh, not cached in a static field: RegisterCore is itself
		// RequiresUnreferencedCode, and a static field initializer runs in the implicit, unannotated
		// static constructor — AddWell is already the correctly annotated home for this lookup, and
		// it is one-time startup wiring, not a hot path.
		var registerCore = typeof(ServiceCollectionExtensions).GetMethod(nameof(RegisterCore), BindingFlags.NonPublic | BindingFlags.Static)!;

		Dictionary<Type, Type> rootByView = [];
		foreach (var dbSetProperty in typeof(TContext).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (!dbSetProperty.PropertyType.IsGenericType || dbSetProperty.PropertyType.GetGenericTypeDefinition() != typeof(DbSet<>))
				continue;
			var entityType = dbSetProperty.PropertyType.GetGenericArguments()[0];

			foreach (var viewBearer in entityType.GetInterfaces())
			{
				if (!viewBearer.IsGenericType || viewBearer.GetGenericTypeDefinition() != typeof(IViewBearer<>))
					continue;
				var viewType = viewBearer.GetGenericArguments()[0];

				if (rootByView.TryGetValue(viewType, out var priorEntityType))
					throw new InvalidOperationException(
						$"'{viewType.Name}' is claimed by both '{priorEntityType.Name}' and '{entityType.Name}' — the well-and-wire spec requires exactly one DbSet-rooted well per view (spec §3.1). Remove IViewBearer<{viewType.Name}> from one of them.");
				rootByView[viewType] = entityType;

				registerCore.MakeGenericMethod(typeof(TContext), entityType, viewType).Invoke(null, [services]);
			}
		}
		return services;
	}

	[RequiresUnreferencedCode("Calls WellMap.For<TEntity,TView>, itself RequiresUnreferencedCode (well-and-wire spec §4.2 mirror law promotion map, not statically provable to the trimmer).")]
	static void RegisterCore<TContext,
		[DynamicallyAccessedMembers(
			DynamicallyAccessedMemberTypes.PublicConstructors |
			DynamicallyAccessedMemberTypes.NonPublicConstructors |
			DynamicallyAccessedMemberTypes.PublicFields |
			DynamicallyAccessedMemberTypes.NonPublicFields |
			DynamicallyAccessedMemberTypes.PublicProperties |
			DynamicallyAccessedMemberTypes.NonPublicProperties |
			DynamicallyAccessedMemberTypes.Interfaces)] TEntity,
		TView>(IServiceCollection services)
		where TContext : DbContext
		where TEntity : class, IViewBearer<TView>
		where TView : notnull
	{
		services.AddSingleton<IReadRepository<TView>>(provider =>
		{
			var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
			// Create-inspect-dispose: validation needs a live Model, but the repository itself
			// creates its own contexts per operation via the factory — this one never escapes.
			var context = factory.CreateDbContext();
			try
			{
				var entityModel = context.Model.FindEntityType(typeof(TEntity))
					?? throw new InvalidOperationException($"'{typeof(TEntity)}' is not part of '{typeof(TContext)}''s EF model.");
				WellValidation.Validate(entityModel, typeof(TView));
			}
			finally
			{
				context.Dispose();
			}
			// Singleton factory semantics already cache the result of this call — including the
			// WellMap built below — for the lifetime of the provider; no separate cache needed.
			return new Repository<TContext, TEntity, TView>(factory, WellMap.For<TEntity, TView>());
		});
	}
}
