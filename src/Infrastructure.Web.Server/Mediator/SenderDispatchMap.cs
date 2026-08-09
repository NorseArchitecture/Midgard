using System.Collections.Frozen;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     The sender's frozen request-type → dispatch-entry map, built once from every
///     <see cref="ISenderDispatch" /> the realms' generated <c>AddNorse*Handlers()</c> calls registered.
///     Correction (2026-08-07): this map's own <c>ToFrozenDictionary</c> throwing
///     <see cref="ArgumentException" /> on a duplicate key is <em>not</em> the platform's loud backstop
///     for a cross-realm duplicate handler — it never reliably was, once Asgard's
///     <c>RegistrationEmitter</c> started registering <see cref="ISenderDispatch" /> via
///     <c>TryAddEnumerable</c> (idempotency fix for the same-realm double-registration case). That
///     idempotency dedupes by implementation type, and <c>SenderDispatch&lt;TRequest,TResponse&gt;</c>
///     is byte-identical regardless of which realm's generated code registered it — so a genuine
///     cross-realm conflict (two different realms each declaring a handler for the same request type)
///     collapses to a single <see cref="ISenderDispatch" /> entry the same way a harmless duplicate
///     registration does, and this map never sees the duplicate key to throw on. Structurally, by the
///     time this map is built, there is only ever at most one entry per request type — so the throw
///     below is defense-in-depth against that invariant somehow not holding, not the primary detection
///     mechanism. The actual loud backstop for a cross-realm handler conflict lives in Asgard's
///     <c>SenderDispatch&lt;TRequest,TResponse&gt;.Dispatch</c>, the one place that still resolves
///     <c>IRequestHandler&lt;TRequest,TResponse&gt;</c> as <c>IEnumerable</c> and can actually see more
///     than one distinct handler implementation answering the same request type.
/// </summary>
sealed class SenderDispatchMap(IEnumerable<ISenderDispatch> entries)
{
	readonly FrozenDictionary<Type, ISenderDispatch> _map =
		entries.ToFrozenDictionary(entry => entry.RequestType);

	public ISenderDispatch Get(Type requestType) =>
		_map.TryGetValue(requestType, out var entry) ?
			entry :
			throw new InvalidOperationException(
				$"No handler is registered for request type {requestType.Name}. Is the owning realm's generated AddNorse*Handlers() call missing from the composition root?");
}
