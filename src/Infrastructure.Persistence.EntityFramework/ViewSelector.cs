using System.Linq.Expressions;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
///     Builds the entity→view selector once per closed generic. NEVER "simplify" this to the literal
///     lambda <c>e =&gt; e.View</c> written against <c>IViewBearer&lt;TView&gt;</c> — interface member
///     access does not translate in EF; the expression must bind the concrete entity's own property
///     (well-and-wire spec §5.1, pinned by ViewSelectorTests).
/// </summary>
static class ViewSelector
{
	public static Expression<Func<TEntity, TView>> For<TEntity, TView>(WellMap map)
	{
		var parameter = Expression.Parameter(typeof(TEntity), "e");
		return Expression.Lambda<Func<TEntity, TView>>(Expression.Property(parameter, map.ViewProperty), parameter);
	}
}
