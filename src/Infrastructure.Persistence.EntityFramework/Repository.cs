using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Backend;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// The generic repository closing <see cref="IReadRepository{TView}"/> per well entity — exactly
/// once, in Midgard, per the well-and-wire spec §5. Create-execute-dispose per operation; the
/// context never escapes. Query shape: filter on the rewritten relational surface first, then
/// surgical JSON extraction via the cached view selector.
/// </summary>
/// <remarks>
/// <typeparamref name="TEntity"/> carries the exact <c>DynamicallyAccessedMembers</c> set
/// <see cref="DbContext.Set{TEntity}()"/> itself declares on its own type parameter — required so
/// this still-open generic class self-certifies trim-safe (<c>IsAotCompatible=true</c>) on its own,
/// not merely at each closed-generic call site (where the trimmer can already see the concrete
/// entity type directly and this requirement is trivially satisfied).
/// </remarks>
sealed class Repository<
	TContext,
	[DynamicallyAccessedMembers(
		DynamicallyAccessedMemberTypes.PublicConstructors |
		DynamicallyAccessedMemberTypes.NonPublicConstructors |
		DynamicallyAccessedMemberTypes.PublicFields |
		DynamicallyAccessedMemberTypes.NonPublicFields |
		DynamicallyAccessedMemberTypes.PublicProperties |
		DynamicallyAccessedMemberTypes.NonPublicProperties |
		DynamicallyAccessedMemberTypes.Interfaces)] TEntity,
	TView>(IDbContextFactory<TContext> factory, WellMap map) : IReadRepository<TView>
	where TContext : DbContext
	where TEntity : class, IViewBearer<TView>
	where TView : notnull
{
	readonly Expression<Func<TEntity, TView>> _selector = ViewSelector.For<TEntity, TView>(map);

	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "QueryById resolves the entity's primary-key CLR property (and, for a non-Guid key, its Guid constructor) via KeyPredicate, which is annotated RequiresUnreferencedCode; safe under the well-and-wire mirror law (spec §5.1) but not statically provable to the trimmer. This method implements IReadRepository<TView>.GetAsync, whose interface declaration carries no such annotation, so the requirement is suppressed here rather than propagated.")]
	public async Task<Outcome<TView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		var view = await QueryById(context, id).Select(_selector).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
		return view is null ? Outcome<TView>.Err(ErrorCategory.NotFound) : Outcome<TView>.Ok(view);
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "QueryById resolves the entity's primary-key CLR property (and, for a non-Guid key, its Guid constructor) via KeyPredicate, which is annotated RequiresUnreferencedCode; safe under the well-and-wire mirror law (spec §5.1) but not statically provable to the trimmer. This method implements IReadRepository<TView>.GetAsync<TProjection>, whose interface declaration carries no such annotation, so the requirement is suppressed here rather than propagated.")]
	public async Task<Outcome<TProjection>> GetAsync<TProjection>(Guid id, Expression<Func<TView, TProjection>> projection, CancellationToken cancellationToken = default)
		where TProjection : notnull
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		// Take(1) + count inspection, NOT FirstOrDefaultAsync: in an unconstrained generic,
		// TProjection? is not Nullable<TProjection> for value types, so a null check over
		// default(TProjection) would fabricate Ok(default) from absence — a succeeded Outcome
		// for a row that does not exist, violating the core invariant. Same TOP(1) SQL,
		// same philosophy as the Take(2) law, zero default-value ambiguity.
		var results = await QueryById(context, id).Select(_selector).Select(projection).Take(1).ToListAsync(cancellationToken).ConfigureAwait(false);
		return results.Count == 0 ? Outcome<TProjection>.Err(ErrorCategory.NotFound) : Outcome<TProjection>.Ok(results[0]);
	}

	public async Task<Outcome<TView>> FirstAsync(Expression<Func<TView, bool>> predicate, CancellationToken cancellationToken = default)
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		var view = await Query(context, predicate).Select(_selector).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
		return view is null ? Outcome<TView>.Err(ErrorCategory.NotFound) : Outcome<TView>.Ok(view);
	}

	public async Task<Outcome<TProjection>> FirstAsync<TProjection>(Expression<Func<TView, bool>> predicate, Expression<Func<TView, TProjection>> projection, CancellationToken cancellationToken = default)
		where TProjection : notnull
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		// Take(1) + count inspection — see GetAsync<TProjection> for why FirstOrDefaultAsync is
		// forbidden on projection overloads (value-type default fabrication).
		var results = await Query(context, predicate).Select(_selector).Select(projection).Take(1).ToListAsync(cancellationToken).ConfigureAwait(false);
		return results.Count == 0 ? Outcome<TProjection>.Err(ErrorCategory.NotFound) : Outcome<TProjection>.Ok(results[0]);
	}

	public async Task<Outcome<IReadOnlyList<TView>>> ListAsync(Expression<Func<TView, bool>> predicate, CancellationToken cancellationToken = default)
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		IReadOnlyList<TView> list = await Query(context, predicate).Select(_selector).ToListAsync(cancellationToken).ConfigureAwait(false);
		return Outcome<IReadOnlyList<TView>>.Ok(list);
	}

	public async Task<Outcome<IReadOnlyList<TProjection>>> ListAsync<TProjection>(Expression<Func<TView, bool>> predicate, Expression<Func<TView, TProjection>> projection, CancellationToken cancellationToken = default)
		where TProjection : notnull
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		IReadOnlyList<TProjection> list = await Query(context, predicate).Select(_selector).Select(projection).ToListAsync(cancellationToken).ConfigureAwait(false);
		return Outcome<IReadOnlyList<TProjection>>.Ok(list);
	}

	public async Task<Outcome<TView>> SingleAsync(Expression<Func<TView, bool>> predicate, CancellationToken cancellationToken = default)
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		// Take(2) + count inspection — the same TOP(2)/LIMIT 2 SQL EF's SingleOrDefaultAsync emits,
		// with the exception replaced by a count check. NEVER SingleOrDefaultAsync + catch: EF's
		// InvalidOperationException is a junk drawer shared with untranslatable-predicate and
		// context-lifetime failures — catching it would ship translation bugs up the wire as
		// phantom duplicate data (well-and-wire spec §5.3, pinned by RepositorySingleTests).
		var candidates = await Query(context, predicate).Select(_selector).Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
		return candidates.Count switch
		{
			0 => Outcome<TView>.Err(ErrorCategory.NotFound),
			1 => Outcome<TView>.Ok(candidates[0]),
			_ => Outcome<TView>.Err(ErrorCategory.MultipleMatches),
		};
	}

	public async Task<Outcome<TProjection>> SingleAsync<TProjection>(Expression<Func<TView, bool>> predicate, Expression<Func<TView, TProjection>> projection, CancellationToken cancellationToken = default)
		where TProjection : notnull
	{
		var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
		await using var contextDisposable = context.ConfigureAwait(false);
		// Take(2) + count inspection — see the non-projection overload; the projection rides the SQL
		// via Select(projection), it does not post-process a materialized TView.
		var candidates = await Query(context, predicate).Select(_selector).Select(projection).Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
		return candidates.Count switch
		{
			0 => Outcome<TProjection>.Err(ErrorCategory.NotFound),
			1 => Outcome<TProjection>.Ok(candidates[0]),
			_ => Outcome<TProjection>.Err(ErrorCategory.MultipleMatches),
		};
	}

	IQueryable<TEntity> Query(TContext context, Expression<Func<TView, bool>> predicate) =>
		context.Set<TEntity>().AsNoTracking().Where(PredicateRewriter.Rewrite<TEntity, TView>(predicate, map));

	[RequiresUnreferencedCode("Delegates to KeyPredicate.For, which resolves the entity's primary-key CLR property (and, for a non-Guid key, its Guid constructor) via runtime reflection; safe under the well-and-wire mirror law (spec §5.1) but not statically provable to the trimmer.")]
	static IQueryable<TEntity> QueryById(TContext context, Guid id) =>
		context.Set<TEntity>().AsNoTracking().Where(KeyPredicate.For<TEntity>(context, id));
}
