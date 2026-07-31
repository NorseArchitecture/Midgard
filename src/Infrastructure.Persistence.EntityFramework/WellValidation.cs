using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Norse.Abstractions.Backend;

namespace Norse.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// The total-mirror law (well-and-wire spec §4.2), enforced against real EF model metadata rather
/// than the two CLR shapes alone. <see cref="WellMap.For{TEntity,TView}"/> silently skips an
/// unmatched entity scalar or collection — it just doesn't promote it, no error, since it exists to
/// build the promotion map, not police the shape. This is the check that turns an unmirrored member
/// into a loud startup failure, per <see cref="ServiceCollectionExtensions.AddWell{TContext}"/>'s
/// deferred-into-first-resolution validation step.
/// </summary>
static class WellValidation
{
	/// <summary>
	/// Validates <paramref name="entityType"/> (a well root's EF model metadata) against
	/// <paramref name="viewType"/>, throwing <see cref="InvalidOperationException"/> naming the
	/// entity, view, and offending member on the first violation found.
	/// </summary>
	[RequiresUnreferencedCode("Calls WellMap.ElementType to check a view collection property's IEnumerable<T> shape; that check is itself RequiresUnreferencedCode (reflects over the element type's interfaces), safe under the mirror law but not statically provable to the trimmer.")]
	public static void Validate(IEntityType entityType, Type viewType)
	{
		foreach (var property in entityType.GetProperties())
		{
			// Shadow properties (audit stamps, temporal period columns) have no PropertyInfo by
			// construction and are exempt outright — there is no CLR member to mirror in the first
			// place. FK properties belong to the relationship, not the mirror law; [NotProjected]
			// is the declared, deliberate exception for everything else.
			if (property.PropertyInfo is not PropertyInfo propertyInfo || property.IsForeignKey())
				continue;
			if (propertyInfo.GetCustomAttribute<NotProjectedAttribute>() is not null)
				continue;

			var viewProperty = viewType.GetProperty(propertyInfo.Name);
			if (viewProperty is null || viewProperty.PropertyType != property.ClrType)
				throw new InvalidOperationException(
					$"'{entityType.ClrType.Name}.{propertyInfo.Name}' has no matching scalar on '{viewType.Name}' — the well-and-wire total-mirror law (spec §4.2) requires every declared, non-FK entity scalar to have a same-named, same-typed view property. Add '{viewType.Name}.{propertyInfo.Name}' as '{property.ClrType}', or mark '{entityType.ClrType.Name}.{propertyInfo.Name}' [NotProjected] if it is deliberately excluded from the view.");
		}

		foreach (var navigation in entityType.GetNavigations())
		{
			// Reference navigations are excluded entirely — only collections carry the mirror law;
			// the view's own View property back-reference and any other singular navigation are not
			// in scope here.
			if (!navigation.IsCollection)
				continue;

			var viewProperty = viewType.GetProperty(navigation.Name);
			if (viewProperty is null || WellMap.ElementType(viewProperty.PropertyType) is null)
				throw new InvalidOperationException(
					$"'{entityType.ClrType.Name}.{navigation.Name}' has no matching collection on '{viewType.Name}' — the well-and-wire total-mirror law (spec §4.2) requires every declared collection navigation to pair by name with a view property exposing an element type. Add '{viewType.Name}.{navigation.Name}' as an IEnumerable<T>-shaped property.");
		}
	}
}
