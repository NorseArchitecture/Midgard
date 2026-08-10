using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     Runs every registered <see cref="IValidator{T}" /> for the request and collapses failures into
///     field-grouped <see cref="ErrorCategory.Validation" />. An empty validator collection is a valid
///     request by definition (spec §2.6) — queries and commands both flow through this chain, and most
///     queries never declare a validator. Absence is <c>[]</c>, not an error.
/// </summary>
sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		Dictionary<string, List<string>> failures = [];
		foreach (var validator in validators)
		{
			var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
			foreach (var failure in result.Errors)
			{
				if (!failures.TryGetValue(failure.PropertyName, out var messages))
					failures[failure.PropertyName] = messages = [];
				messages.Add(failure.ErrorMessage);
			}
		}

		return failures.Count > 0 ?
			Outcome<TResponse>.Err(ErrorCategory.Validation,
				failures.ToDictionary(f => f.Key, f => f.Value.ToArray())) :
			await next().ConfigureAwait(false);
	}
}
