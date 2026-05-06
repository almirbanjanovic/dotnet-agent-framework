namespace Contoso.BlazorUi.Services;

/// <summary>
/// Cross-component "is the floating chat panel open" state.
/// </summary>
public sealed class ChatPanelState
{
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Toggle()
    {
        IsOpen = !IsOpen;
        Changed?.Invoke();
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Changed?.Invoke();
    }
}
