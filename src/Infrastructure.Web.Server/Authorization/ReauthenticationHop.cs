namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     Builds the server-authored re-authentication <c>NextUrl</c>: the sign-in page under the live path base, with
///     a return URL of the landing under that same path base. The one place the shape is spelled.
/// </summary>
static class ReauthenticationHop
{
	/// <summary>The sign-in page, relative to the path base.</summary>
	public const string LoginPath = "/Account/Login";

	/// <summary>Builds the hop for <paramref name="landing" /> under <paramref name="pathBase" />.</summary>
	/// <param name="pathBase">The request's path base; empty at the root.</param>
	/// <param name="landing">The path-base-relative landing the request declares.</param>
	/// <returns>For example <c>/app/Account/Login?returnUrl=%2Fapp%2FAccount%2FManage%2FPasskeys</c>.</returns>
	public static string NextUrl(PathString pathBase, string landing) =>
		pathBase.Add(LoginPath).ToUriComponent() + QueryString.Create("returnUrl", pathBase + landing).ToUriComponent();
}
