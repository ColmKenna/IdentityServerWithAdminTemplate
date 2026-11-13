using Microsoft.AspNetCore.Components;
using IdentityServerProject.Mobile.Core.Data;

namespace IdentityServerProject.Mobile.Core.Components;

/// <summary>
///     A component that shows local data and reloads itself when that data changes — after the user's
///     own edit on another screen, or after a sync run applies server changes in the background.
/// </summary>
public abstract class LiveDataComponent : ComponentBase, IDisposable
{
    [Inject] protected LocalDataChanges Changes { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Changes.Changed += OnChanged;
        await LoadAsync();
    }

    protected abstract Task LoadAsync();

    public virtual void Dispose() => Changes.Changed -= OnChanged;

    private void OnChanged(ChangeOrigin origin) => _ = InvokeAsync(ReloadAsync);

    private async Task ReloadAsync()
    {
        try
        {
            await LoadAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }
}
