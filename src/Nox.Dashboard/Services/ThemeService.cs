namespace Nox.Dashboard.Services;

public class ThemeService
{
    public string Current { get; private set; } = "light";
    public bool IsDark => Current == "dark";
    public event Action? OnChange;

    public void Set(string theme)
    {
        if (Current == theme) return;
        Current = theme;
        OnChange?.Invoke();
    }

    public void Toggle() => Set(IsDark ? "light" : "dark");
}
