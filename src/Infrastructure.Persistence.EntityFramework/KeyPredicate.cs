using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// Builds the identity predicate <c>e => e.&lt;KeyProp&gt; == &lt;converted id&gt;</c> behind
/// <see cref="Repository{TContext,TEntity,TView}"/>'s two <c>GetAsync</c> overloads — the only
/// caller-inaccessible predicate in the well-and-wire read surface, so an accidental full-table
/// scan on the identity path cannot be written. The entity's single primary-key property is
/// discovered once per entity type from <see cref="DbContext.Model"/> and the resulting predicate
/// factory cached — the incoming <see cref="Guid"/> varies per call, the key's CLR type and
/// conversion path do not.
/// </summary>
static class KeyPredicate
{
	[RequiresUnreferencedCode("Delegates to Cache<TEntity>.FactoryFor, which resolves the EF model's primary-key CLR property (and, for a non-Guid key, its Guid constructor) via runtime reflection on first use; not statically provable to the trimmer.")]
	public static Expression<Func<TEntity, bool>> For<TEntity>(DbContext context, Guid id) where TEntity : class =>
		Cache<TEntity>.FactoryFor(context)(id);

	static class Cache<TEntity> where TEntity : class
	{
		// Not `static readonly`: the shape depends on the EF model, which is only reachable through a
		// DbContext instance handed to us at first call — there is no parameterless static-initializer
		// path to it. Once built for a given TEntity, the factory is reused for every subsequent id;
		// a benign duplicate build under a rare first-call race is idempotent, not a correctness bug.
		static Func<Guid, Expression<Func<TEntity, bool>>>? _factory;

		[RequiresUnreferencedCode("Delegates to Build on first use, which resolves the EF model's primary-key CLR property (and, for a non-Guid key, its Guid constructor) via runtime reflection; not statically provable to the trimmer.")]
		public static Func<Guid, Expression<Func<TEntity, bool>>> FactoryFor(DbContext context) =>
			_factory ??= Build(context);

		[RequiresUnreferencedCode("Resolves the EF model's primary-key CLR property, and, for a non-Guid key type, that type's public single-Guid constructor, both via runtime reflection over a Type discovered from the model at first use (well-and-wire spec §5.1) — not statically provable to the trimmer.")]
		static Func<Guid, Expression<Func<TEntity, bool>>> Build(DbContext context)
		{
			var primaryKey = context.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()
				?? throw new InvalidOperationException($"'{typeof(TEntity)}' has no primary key registered in the EF model.");
			if (primaryKey.Properties is not [IProperty keyProperty])
				throw new InvalidOperationException($"'{typeof(TEntity)}' has a composite primary key; well-and-wire identity lookup requires a single-column key.");
			var keyPropertyInfo = keyProperty.PropertyInfo
				?? throw new InvalidOperationException($"'{typeof(TEntity)}'s primary key '{keyProperty.Name}' is a shadow property; well-and-wire identity lookup requires a declared CLR property.");

			ParameterExpression entity = Expression.Parameter(typeof(TEntity), "e");
			MemberExpression keyAccess = Expression.Property(entity, keyPropertyInfo);

			if (keyPropertyInfo.PropertyType == typeof(Guid))
				return id => Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(keyAccess, Expression.Constant(id)), entity);

			var guidConstructor = keyPropertyInfo.PropertyType.GetConstructor([typeof(Guid)])
				?? throw new InvalidOperationException($"Key type '{keyPropertyInfo.PropertyType}' is neither Guid nor exposes a public constructor accepting a single Guid; well-and-wire identity lookup cannot convert the incoming id.");
			return id => Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(keyAccess, Expression.New(guidConstructor, Expression.Constant(id))), entity);
		}
	}
}
