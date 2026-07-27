using System.Linq.Expressions;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// Type-erased <c>Failed</c>-envelope factory for the client decoder (spec §2.1): one compiled
/// delegate per closed <see cref="Outcome{T}"/>, built once in the static initializer — one-time
/// wiring, never touched on the success path. <see cref="CanCreate"/> is <see langword="false"/>
/// for every non-<c>Outcome</c> response type, which is how the interceptor passes those through.
/// Internal — only the interceptor (same assembly) and tests (IVT) touch it.
/// </summary>
static class OutcomeFactory<TResponse>
{
	static readonly Func<Problem, TResponse>? _factory = Build();

	/// <summary>Whether <typeparamref name="TResponse"/> is a closed <see cref="Outcome{T}"/>.</summary>
	public static bool CanCreate =>
		_factory is not null;

	/// <summary>Envelopes the decoded problem as the failure case of <typeparamref name="TResponse"/>.</summary>
	public static TResponse CreateErr(Problem problem) =>
		_factory is not null ?
			_factory(problem) :
			throw new InvalidOperationException($"{typeof(TResponse).Name} is not an Outcome<T>.");

	static Func<Problem, TResponse>? Build()
	{
		if (typeof(TResponse) is not { IsGenericType: true } type || type.GetGenericTypeDefinition() != typeof(Outcome<>))
			return null;

		var problem = Expression.Parameter(typeof(Problem), "problem");
		var failed = Expression.New(typeof(Failed).GetConstructor([typeof(Problem)])!, problem);
		var outcome = Expression.New(type.GetConstructor([typeof(Failed)])!, failed);
		return Expression.Lambda<Func<Problem, TResponse>>(outcome, problem).Compile();
	}
}
