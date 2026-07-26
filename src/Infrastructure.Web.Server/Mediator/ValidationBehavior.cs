using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Runs the request's <see cref="IValidator{T}"/> (resolved by the generator via the
/// <c>{RequestName}Validator</c> naming convention, registered as <c>IValidator&lt;TRequest&gt;</c> in
/// DI) and collapses failures into field-grouped <see cref="ErrorCategory.Validation"/>.
///
/// Stays <c>internal</c> (2026-07-25): see <see cref="TelemetryBehavior{TRequest,TResponse}"/>'s
/// remark — visible to InProcessHost-mode consumers via this project's <c>InternalsVisibleTo</c>
/// grant, not by widening to <c>public</c>.
/// </summary>
sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest> validator) : IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next, CancellationToken cancellationToken = default)
	{
		var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

		if (!result.IsValid)
		{
			var errors = result.Errors
				.GroupBy(failure => failure.PropertyName)
				.ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
			return Outcome<TResponse>.Err(ErrorCategory.Validation, errors);
		}

		return await next().ConfigureAwait(false);
	}
}
