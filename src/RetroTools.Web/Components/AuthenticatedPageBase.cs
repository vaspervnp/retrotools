using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using RetroTools.Web.Configuration;

namespace RetroTools.Web.Components;

/// <summary>
/// Βάση για σελίδες που χρειάζονται την ταυτότητα του χρήστη.
/// </summary>
/// <remarks>
/// Η ταυτότητα έρχεται από το <see cref="AuthenticationState"/> και όχι από
/// <c>IHttpContextAccessor</c>: στον Blazor Server το <c>HttpContext</c> είναι
/// διαθέσιμο μόνο κατά την αρχική απόδοση και μετά είναι null, οπότε ο accessor
/// θα επέστρεφε σιωπηλά «ανώνυμος» μόλις ξεκινούσε η διαδραστικότητα.
/// </remarks>
public abstract class AuthenticatedPageBase : ComponentBase
{
    [CascadingParameter]
    protected Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    protected Guid? CurrentUserId { get; private set; }

    protected bool IsAuthenticated
    {
        get { return CurrentUserId.HasValue; }
    }

    protected override async Task OnInitializedAsync()
    {
        CurrentUserId = await ResolveUserIdAsync();
        await OnAuthenticatedInitializedAsync();
    }

    /// <summary>Καλείται αφού είναι γνωστός ο χρήστης.</summary>
    protected virtual Task OnAuthenticatedInitializedAsync()
    {
        return Task.CompletedTask;
    }

    private async Task<Guid?> ResolveUserIdAsync()
    {
        if (AuthenticationStateTask == null)
        {
            return null;
        }

        var state = await AuthenticationStateTask;
        var value = state.User.FindFirst(HttpContextCurrentUser.UserIdClaimType)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
