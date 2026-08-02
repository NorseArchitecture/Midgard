using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// The one message-formatting source for a <see cref="Failure"/> rendered as human-readable detail
/// text. <see cref="XmlReadContext.AddScalarFailure"/> calls this, and Task 4's gRPC <c>ResultRules</c>
/// validation calls the same method, so required-missing wording is byte-identical across the XML,
/// JSON, and gRPC channels by construction, never by copied string. Deliberately
/// <see langword="public"/>, not <see langword="internal sealed"/>: called from a host compilation
/// (a different repo, later task).
/// </summary>
public static class FailureDetail
{
	/// <summary>Renders <paramref name="failure"/> into its channel-agnostic detail text.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="failure"/>'s <see cref="Failure.Reason"/> is not a recognized reason.</exception>
	public static string Render(in Failure failure) =>
		failure.Reason switch
		{
			ParseFailure.Malformed => $"cannot parse '{failure.Input}' as {failure.ExpectedType}",
			ParseFailure.Empty => "required value missing",
			_ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Reason, "Unrecognized ParseFailure reason.")
		};
}
