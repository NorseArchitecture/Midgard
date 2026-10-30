namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     The freshness window <see cref="RecentAuthenticationHandler" /> enforces, bound to
///     <see cref="SectionName" /> and validated on start by
///     <see cref="Authentication.AuthenticationBuilderExtensions.AddNorseAuthentication" />: positive, and no longer
///     than one day.
/// </summary>
sealed class RecentAuthenticationOptions
{
	/// <summary>The configuration section the options bind to.</summary>
	public const string SectionName = "Norse:Authentication";

	/// <summary>How old a principal's authentication time may be and still count as recent.</summary>
	public TimeSpan RecentWindow { get; set; } = TimeSpan.FromMinutes(10);

	/// <summary>Whether <see cref="RecentWindow" /> is positive and no longer than one day.</summary>
	public bool IsValid => RecentWindow > TimeSpan.Zero && RecentWindow <= TimeSpan.FromDays(1);
}
