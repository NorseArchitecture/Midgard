using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     Blocking, keyed once-registration against a shared <see cref="RuntimeTypeModel" /> — the guard every
///     wire-model registration site must go through instead of a hand-rolled check-then-act or flag-first
///     guard, both of which let a concurrent caller observe a half-built model
///     (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c> and its follow-up,
///     <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>).
/// </summary>
public static class WireModelRegistrationGuard
{
	static readonly ConditionalWeakTable<RuntimeTypeModel, ConcurrentDictionary<Type, Lazy<bool>>> _guards = [];

	extension(RuntimeTypeModel model)
	{
		/// <summary>
		///     Runs <paramref name="register" /> exactly once for the (<paramref name="model" />,
		///     <paramref name="key" />) pair, blocking any concurrent caller until it completes rather than
		///     letting it observe a half-registered model. <paramref name="key" /> identifies what's being
		///     registered — the registrant's own type for a whole-model bootstrap
		///     (<c>typeof(IdentifierSerializers)</c>), or the payload type for a single surrogate
		///     (<c>typeof(Outcome&lt;ParityReport&gt;)</c>). A throwing <paramref name="register" /> has its
		///     exception cached and rethrown to every subsequent caller for that pair, never silently
		///     swallowed.
		/// </summary>
		/// <param name="key">Identifies the registration; independent keys on the same model never block each other.</param>
		/// <param name="register">
		///     Runs exactly once, the first time this (model, key) pair is touched. Must not, directly or
		///     transitively, call <see cref="EnsureRegistered" /> again for the same (model, key) pair —
		///     <see cref="Lazy{T}" /> treats that as recursive initialization and throws
		///     <see cref="InvalidOperationException" />, which is then cached and rethrown to every
		///     subsequent caller for that pair, same as any other factory exception.
		/// </param>
		public void EnsureRegistered(Type key, Action register)
		{
			ArgumentNullException.ThrowIfNull(model);
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(register);
			var perModel = _guards.GetValue(model, static _ => new ConcurrentDictionary<Type, Lazy<bool>>());
			_ = perModel.GetOrAdd(key, _ => new Lazy<bool>(() =>
			{
				register();
				return true;
			}, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
		}
	}
}
