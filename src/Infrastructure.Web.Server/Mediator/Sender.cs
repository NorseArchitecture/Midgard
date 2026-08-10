using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     The hand-rolled sender (spec §2.2): a frozen-dictionary lookup plus the closed-generic fold in
///     <see cref="SenderDispatch{TRequest,TResponse}" />. Scoped so behaviors and handlers resolve from
///     the caller's own scope. No reflection, no assembly scanning — the dispatch map is populated by
///     generated compile-time registrations.
/// </summary>
sealed class Sender(IServiceProvider services, SenderDispatchMap map) : ISender
{
	public ValueTask<Outcome<TResponse>> Send<TResponse>(IRequest<TResponse> request,
		CancellationToken cancellationToken = default)
		where TResponse : notnull =>
		((ISenderDispatch<TResponse>)map.Get(request.GetType())).Dispatch(services, request, cancellationToken);
}
