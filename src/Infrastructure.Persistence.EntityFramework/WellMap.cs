using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Norse.Abstractions.Backend;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// The promotion map for one entity/view pair: which view members retarget to entity columns
/// (promoted scalars), which view collections retarget to entity navigations (promoted
/// collections), and the element-type pairing for each collection. Built once per closed generic
/// and cached by the repository registration; the rewriter consumes it on every predicate.
/// </summary>
sealed record WellMap
{
	/// <summary>View scalar name → matching entity property (same name, same CLR type).</summary>
	public required FrozenDictionary<string, PropertyInfo> PromotedScalars { get; init; }
	/// <summary>View collection name → (entity navigation, view element type → entity element type map).</summary>
	public required FrozenDictionary<string, (PropertyInfo Navigation, FrozenDictionary<string, PropertyInfo> ElementMembers)> PromotedCollections { get; init; }
	/// <summary>The entity's View property — the residual JSON route and the selector's source.</summary>
	public required PropertyInfo ViewProperty { get; init; }

	[RequiresUnreferencedCode("Reflects over TEntity's and TView's public properties by name to build the promotion map; the mirror law (well-and-wire spec §4.2) guarantees the shapes match at runtime, but the trimmer cannot see that statically.")]
	public static WellMap For<TEntity, TView>() where TEntity : IViewBearer<TView>
	{
		var entityProps = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
		var viewProps = typeof(TView).GetProperties(BindingFlags.Public | BindingFlags.Instance);
		var viewProperty = entityProps.Single(p => p.Name == nameof(IViewBearer<>.View) && p.PropertyType == typeof(TView));
		Dictionary<string, PropertyInfo> scalars = [];
		Dictionary<string, (PropertyInfo, FrozenDictionary<string, PropertyInfo>)> collections = [];
		foreach (var viewProp in viewProps)
		{
			var entityMatch = entityProps.FirstOrDefault(p => p.Name == viewProp.Name && p.GetCustomAttribute<NotProjectedAttribute>() is null);
			if (entityMatch is null)
				continue;
			if (entityMatch.PropertyType == viewProp.PropertyType)
			{
				scalars[viewProp.Name] = entityMatch;
				continue;
			}
			var (viewElement, entityElement) = (ElementType(viewProp.PropertyType), ElementType(entityMatch.PropertyType));
			if (viewElement is not null && entityElement is not null)
				collections[viewProp.Name] = (entityMatch, entityElement
					.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(p => viewElement.GetProperty(p.Name)?.PropertyType == p.PropertyType)
					.ToFrozenDictionary(p => p.Name));
		}
		return new()
		{
			PromotedScalars = scalars.ToFrozenDictionary(),
			PromotedCollections = collections.ToFrozenDictionary(p => p.Key, p => p.Value),
			ViewProperty = viewProperty,
		};
	}

	// internal, not private: WellValidation (Task 5) reuses the identical IEnumerable<T>-shape check
	// to enforce the collection half of the total-mirror law against real EF model metadata — the
	// smallest widening that avoids duplicating this exact reflection logic a second time.
	[RequiresUnreferencedCode("Reflects over the element type's interfaces to detect IEnumerable<T> shapes; safe under the mirror law but not statically provable to the trimmer.")]
	internal static Type? ElementType(Type type) =>
		type != typeof(string) ?
			type.GetInterfaces().Append(type)
				.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				?.GetGenericArguments()[0] :
			null;
}
