namespace PeopleCore.Web.Services;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>One toast in flight. <see cref="Id"/> is what the provider cancels its timer by.</summary>
public sealed record ToastMessage(Guid Id, string Message, ToastType Type, TimeSpan Duration);

/// <summary>
/// Raises transient notifications from anywhere in the app. A single <c>ToastProvider</c> in the
/// layout subscribes and does the rendering, so callers do not need a reference to any UI.
/// </summary>
/// <remarks>
/// Registered scoped, which in a WebAssembly app means one instance for the lifetime of the tab.
/// </remarks>
public sealed class ToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    /// <summary>Errors stay up longer: they usually carry something the user has to read and act on.</summary>
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(8);

    public event Action<ToastMessage>? OnShow;

    public event Action<Guid>? OnRemove;

    public void ShowInfo(string message, TimeSpan? duration = null) =>
        Show(message, ToastType.Info, duration);

    public void ShowSuccess(string message, TimeSpan? duration = null) =>
        Show(message, ToastType.Success, duration);

    public void ShowWarning(string message, TimeSpan? duration = null) =>
        Show(message, ToastType.Warning, duration);

    public void ShowError(string message, TimeSpan? duration = null) =>
        Show(message, ToastType.Error, duration ?? ErrorDuration);

    /// <summary>Dismisses a toast early — for a progress message being superseded by its result.</summary>
    public void Remove(Guid id) => OnRemove?.Invoke(id);

    public Guid Show(string message, ToastType type = ToastType.Info, TimeSpan? duration = null)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, type, duration ?? DefaultDuration);
        OnShow?.Invoke(toast);
        return toast.Id;
    }
}
