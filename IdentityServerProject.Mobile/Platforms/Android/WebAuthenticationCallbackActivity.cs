using Android.App;
using Android.Content;
using Android.Content.PM;
using IdentityServerProject.Mobile.Core;

namespace IdentityServerProject.Mobile;

/// <summary>
///     Receives the <c>com.example.identityserverproject://callback</c> redirect when the system browser finishes signing the
///     user in, and passes it back to the pending WebAuthenticator call. The scheme comes from the same constant
///     the OIDC client uses, so the two cannot drift apart.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = ClientDefaults.RedirectScheme)]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
