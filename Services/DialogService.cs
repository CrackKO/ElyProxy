namespace ElyProxy.Services;

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Apple-style feedback layer: modal sheets for decisions and toasts for
/// status. Both surfaces are our own controls (<see cref="Controls.SheetHost"/>
/// and <see cref="Controls.ToastHost"/>), so no Fluent dialog leaks into the UI.
/// </summary>
public sealed class DialogService
{
    private Controls.SheetHost? _sheets;
    private Controls.ToastHost? _toasts;

    public static DialogService Current { get; } = new();

    public void Attach(Controls.SheetHost sheets, Controls.ToastHost toasts)
    {
        _sheets = sheets;
        _toasts = toasts;
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryText = "Продолжить",
        string closeText = "Отмена",
        bool destructive = false)
    {
        if (_sheets is null)
        {
            return false;
        }

        return await _sheets.ShowAsync(title, message, primaryText, closeText, destructive);
    }

    public async Task<bool> ShowCustomAsync(
        string title,
        object content,
        string primaryText,
        string closeText = "Отмена",
        bool destructive = false)
    {
        if (_sheets is null)
        {
            return false;
        }

        return await _sheets.ShowAsync(title, content, primaryText, closeText, destructive);
    }

    public async Task AlertAsync(string title, string message, string closeText = "ОК")
    {
        if (_sheets is null)
        {
            return;
        }

        await _sheets.ShowAsync(title, message, string.Empty, closeText);
    }

    public void Notify(string title, string message, NoticeKind kind = NoticeKind.Info) =>
        _toasts?.Show(title, message, kind);
}
