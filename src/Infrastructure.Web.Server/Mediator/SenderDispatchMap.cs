using System.Collections.Frozen;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// The sender's frozen request-type → dispatch-entry map, built once from every
/// <see cref="ISenderDispatch"/> the realms' generated <c>AddNorse*Handlers()</c> calls registered.
/// A cross-realm duplicate handler — invisible to NORSE010, which is per-assembly — lands here as
/// <c>ToFrozenDictionary</c> throwing <see cref="ArgumentException"/> on the duplicate key at first
/// resolution: the chosen loud startup backstop (2026-07-27 review), priced, not accidental.
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
