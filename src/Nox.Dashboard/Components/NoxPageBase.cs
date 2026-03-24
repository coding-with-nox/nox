using Microsoft.AspNetCore.Components;
using Nox.Dashboard.Services;

namespace Nox.Dashboard.Components;

public abstract class NoxPageBase : ComponentBase, IDisposable
{
    [Inject] protected LanguageService Loc { get; set; } = default!;

    protected override void OnInitialized()
    {
        Loc.OnChange += OnLanguageChanged;
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        Loc.OnChange -= OnLanguageChanged;
    }
}
