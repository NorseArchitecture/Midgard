using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// Compiles a caller's one-view predicate into the entity's physical plan (well-and-wire spec
/// §5.2): promoted scalars retarget to relational columns, promoted-collection Any(...) retargets
/// to the navigation (EF emits EXISTS against the indexed child table), and everything else routes
/// through the View JSON path — legal, cost-bearing, residual. The law binds callers, not this
/// machinery (spec §3.3): promoted structures are projections of the view's own data, so intent is
/// preserved and only the physical plan changes.
/// </summary>
static class PredicateRewriter
{
	public static Expression<Func<TEntity, bool>> Rewrite<TEntity, TView>(Expression<Func<TView, bool>> predicate, WellMap map)
	{
		ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
		Visitor visitor = new(predicate.Parameters[0], parameter, map);
		return Expression.Lambda<Func<TEntity, bool>>(visitor.Visit(predicate.Body), parameter);
	}

	sealed class Visitor(ParameterExpression viewParameter, ParameterExpression entityParameter, WellMap map) : ExpressionVisitor
	{
		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Expression != viewParameter)
				return base.VisitMember(node);
			if (map.PromotedScalars.TryGetValue(node.Member.Name, out var column))
				return Expression.Property(entityParameter, column);
			if (map.PromotedCollections.TryGetValue(node.Member.Name, out var collection))
				return Expression.Property(entityParameter, collection.Navigation);
			// Residual: e.View.Member — the JSON path.
			return Expression.Property(Expression.Property(entityParameter, map.ViewProperty), (PropertyInfo)node.Member);
		}

		// ExpressionVisitor.VisitMethodCall is a fixed override signature, so it cannot itself carry
		// [RequiresUnreferencedCode]/[RequiresDynamicCode] (the base member has neither, and the
		// trimmer/AOT analyzers require an override to match its base exactly). The reflection over
		// the navigation's element type and the dynamic Expression.Lambda/Expression.Call construction
		// below are both safe under the mirror law (well-and-wire spec §4.2 — WellMap.For already
		// validated the shapes at map-build time) but not statically provable to either analyzer.
		[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Navigation.PropertyType's element type is resolved via WellMap.For, which already validated the mirror-law shape.")]
		[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Building the rewritten Any(...) call requires runtime Expression construction; this rewriter is not meant to run in an AOT-published context (EF's own query pipeline is not AOT-compatible either).")]
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			// Any(source, lambda) over a promoted collection: the source retargets via VisitMember
			// above; the inner lambda's parameter must re-type from the view element to the entity
			// element (mirror law guarantees name+type-matched members).
			if (node.Method.DeclaringType == typeof(Enumerable)
				&& node.Method.Name == nameof(Enumerable.Any)
				&& node.Arguments is [MemberExpression { Expression: var src } member, LambdaExpression inner]
				&& src == viewParameter
				&& map.PromotedCollections.TryGetValue(member.Member.Name, out var collection))
			{
				var elementType = collection.Navigation.PropertyType.GetInterfaces().Append(collection.Navigation.PropertyType)
					.First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
					.GetGenericArguments()[0];
				ParameterExpression elementParameter = Expression.Parameter(elementType, inner.Parameters[0].Name);
				ElementVisitor elementVisitor = new(inner.Parameters[0], elementParameter, collection.ElementMembers);
				var rewrittenInner = Expression.Lambda(elementVisitor.Visit(inner.Body), elementParameter);
				return Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [elementType],
					Expression.Property(entityParameter, collection.Navigation), rewrittenInner);
			}
			return base.VisitMethodCall(node);
		}
	}

	sealed class ElementVisitor(ParameterExpression viewElement, ParameterExpression entityElement, IReadOnlyDictionary<string, PropertyInfo> members) : ExpressionVisitor
	{
		protected override Expression VisitMember(MemberExpression node) =>
			node.Expression == viewElement && members.TryGetValue(node.Member.Name, out var property) ?
				Expression.Property(entityElement, property) :
				base.VisitMember(node);
	}
}
