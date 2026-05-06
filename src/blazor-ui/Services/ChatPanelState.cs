namespace Contoso.BlazorUi.Services;

/// <summary>
/// Three-state mode for the floating chat panel.
/// <see cref="Closed"/> = only the FAB is visible.
/// <see cref="Minimized"/> = compact header bar pinned bottom-right; the
/// conversation is preserved so the user can keep browsing without losing
/// context.
/// <see cref="Open"/> = full chat panel is visible.
/// </summary>
public enum ChatPanelMode
{
    Closed,
    Minimized,
    Open
}

/// <summary>
/// Cross-component state for the floating chat widget. Pages call
/// <see cref="Open"/> from CTAs (e.g. the home page "Ask the experts"
/// button) and the <see cref="Shared.ChatBubble"/> component reacts to
/// <see cref="Changed"/>.
/// </summary>
public sealed class ChatPanelState
{
    public ChatPanelMode Mode { get; private set; } = ChatPanelMode.Closed;

    public bool IsOpen => Mode == ChatPanelMode.Open;
    public bool IsMinimized => Mode == ChatPanelMode.Minimized;
    public bool IsClosed => Mode == ChatPanelMode.Closed;

    /// <summary>
    /// Set whenever <see cref="Open"/> is called. The UI runs a brief
    /// "attention" animation while this is true so users notice the
    /// panel even if they were looking at the top of the page when they
    /// clicked the trigger. The UI must call
    /// <see cref="AcknowledgePulse"/> after running the animation.
    /// </summary>
    public bool PulseRequested { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Open the panel. Always raises a pulse so callers (e.g. the hero
    /// CTA, the footer link) get visible feedback even if the panel was
    /// already open.
    /// </summary>
    public void Open()
    {
        var changed = Mode != ChatPanelMode.Open;
        Mode = ChatPanelMode.Open;
        PulseRequested = true;
        if (changed)
        {
            Changed?.Invoke();
        }
        else
        {
            // Same mode but pulse flag changed — still notify so the UI
            // re-runs the attention animation.
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Collapse to the compact header bar without losing conversation
    /// state. From <see cref="Closed"/>, this is treated as
    /// <see cref="Open"/> — the user has nothing to minimize yet, so
    /// they get the full panel.
    /// </summary>
    public void Minimize()
    {
        if (Mode == ChatPanelMode.Closed)
        {
            Open();
            return;
        }

        if (Mode == ChatPanelMode.Minimized)
        {
            return;
        }

        Mode = ChatPanelMode.Minimized;
        PulseRequested = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Close fully — back to the FAB. Conversation history is preserved
    /// inside <see cref="Shared.ChatBubble"/> until the customer changes
    /// or the page reloads.
    /// </summary>
    public void Close()
    {
        if (Mode == ChatPanelMode.Closed)
        {
            return;
        }

        Mode = ChatPanelMode.Closed;
        PulseRequested = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// FAB click handler — open if hidden, otherwise minimize.
    /// </summary>
    public void Toggle()
    {
        if (Mode == ChatPanelMode.Open)
        {
            Minimize();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// Called by the UI after running the attention animation so the
    /// pulse only fires once per <see cref="Open"/> call.
    /// </summary>
    public void AcknowledgePulse()
    {
        if (!PulseRequested)
        {
            return;
        }

        PulseRequested = false;
    }
}
