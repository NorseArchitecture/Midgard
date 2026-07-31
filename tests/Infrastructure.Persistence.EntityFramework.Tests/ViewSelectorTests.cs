using System.Linq.Expressions;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

public sealed class ViewSelectorTests
{
	[Fact]
	void Selector_is_built_against_the_concrete_entity_not_the_interface()
	{
		var selector = ViewSelector.For<WidgetEntity, WidgetView>(WellMap.For<WidgetEntity, WidgetView>());
		// Interface member access (IViewBearer<T>.View) does not translate in EF; the selector must
		// bind the concrete entity's own property. Pinned here so it never gets "simplified" away.
		((MemberExpression)selector.Body).Member.DeclaringType.ShouldBe(typeof(WidgetEntity));
	}
}
