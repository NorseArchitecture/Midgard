using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The one message-formatting source for a <see cref="Failure" /> rendered as human-readable detail
///     text. <see cref="XmlReadContext.AddScalarFailure" /> calls this, and Task 4's gRPC <c>ResultRules</c>
///     validation calls the same method, so required-missing wording is byte-identical across the XML,
///     JSON, and gRPC channels by construction, never by copied string. Deliberately
///     <see langword="public" />, not <see langword="internal sealed" />: called from a host compilation
///     (a different repo, later task).
/// </summary>
public static class FailureDetail
{
	/// <summary>Renders <paramref name="failure" /> into its channel-agnostic detail text.</summary>
	/// <remarks>
	///     A <see cref="ParseFailure.Malformed" /> failure carrying a non-null <see cref="Failure.Detail" />
	///     — the JSON flags array reader's "did you mean" suggestion
	///     (<see cref="EnumLexical.ParseFlags{TEnum}" />) is the only current source — appends it after an
	///     em dash; every failure constructed without a <see cref="Failure.Detail" /> renders exactly as
	///     before, byte-for-byte. A <see cref="ParseFailure.Duplicate" /> failure never says "cannot parse"
	///     — the token parsed fine; it repeated where each token may appear only once.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="failure" />'s <see cref="Failure.Reason" /> is not a
	///     recognized reason.
	/// </exception>
	public static string Render(in Failure failure) =>
		failure.Reason switch
		{
			ParseFailure.Malformed => failure.Detail is null ?
				$"cannot parse '{failure.Input}' as {failure.ExpectedType}" :
				$"cannot parse '{failure.Input}' as {failure.ExpectedType} — {failure.Detail}",
			ParseFailure.Empty => "required value missing",
			ParseFailure.Duplicate => $"duplicate value '{failure.Input}'",
			_ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Reason,
				"Unrecognized ParseFailure reason.")
		};
}
