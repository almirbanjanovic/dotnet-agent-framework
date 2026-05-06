using Contoso.BlazorUi.Services;
using FluentAssertions;
using Xunit;

namespace Contoso.BlazorUi.Tests;

public class ChatPanelStateTests
{
    [Fact]
    public void StartsClosed()
    {
        var state = new ChatPanelState();

        state.Mode.Should().Be(ChatPanelMode.Closed);
        state.IsClosed.Should().BeTrue();
        state.IsOpen.Should().BeFalse();
        state.IsMinimized.Should().BeFalse();
        state.PulseRequested.Should().BeFalse();
    }

    [Fact]
    public void Open_FromClosed_TransitionsAndRequestsPulse()
    {
        var state = new ChatPanelState();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Open();

        state.IsOpen.Should().BeTrue();
        state.PulseRequested.Should().BeTrue();
        changeCount.Should().Be(1);
    }

    [Fact]
    public void Open_WhenAlreadyOpen_StillRaisesPulseAndChanged()
    {
        // The "Ask the experts" CTA can be clicked again while the
        // panel is already open. We still want a fresh attention pulse
        // so the user gets visual feedback their click registered.
        var state = new ChatPanelState();
        state.Open();
        state.AcknowledgePulse();

        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Open();

        state.IsOpen.Should().BeTrue();
        state.PulseRequested.Should().BeTrue();
        changeCount.Should().Be(1);
    }

    [Fact]
    public void Minimize_FromClosed_OpensInstead()
    {
        // From the FAB-only state there's nothing to minimize, so
        // Minimize() promotes to Open so users always end up with a
        // usable surface.
        var state = new ChatPanelState();

        state.Minimize();

        state.IsOpen.Should().BeTrue();
        state.PulseRequested.Should().BeTrue();
    }

    [Fact]
    public void Minimize_FromOpen_TransitionsAndClearsPulse()
    {
        var state = new ChatPanelState();
        state.Open();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Minimize();

        state.IsMinimized.Should().BeTrue();
        state.IsOpen.Should().BeFalse();
        state.PulseRequested.Should().BeFalse();
        changeCount.Should().Be(1);
    }

    [Fact]
    public void Minimize_WhenAlreadyMinimized_NoOp()
    {
        var state = new ChatPanelState();
        state.Open();
        state.Minimize();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Minimize();

        state.IsMinimized.Should().BeTrue();
        changeCount.Should().Be(0);
    }

    [Fact]
    public void Close_FromOpen_ClearsPulseAndReturnsToClosed()
    {
        var state = new ChatPanelState();
        state.Open();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Close();

        state.IsClosed.Should().BeTrue();
        state.PulseRequested.Should().BeFalse();
        changeCount.Should().Be(1);
    }

    [Fact]
    public void Close_FromMinimized_ReturnsToClosed()
    {
        var state = new ChatPanelState();
        state.Open();
        state.Minimize();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Close();

        state.IsClosed.Should().BeTrue();
        changeCount.Should().Be(1);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_NoOp()
    {
        var state = new ChatPanelState();
        var changeCount = 0;
        state.Changed += () => changeCount++;

        state.Close();

        state.IsClosed.Should().BeTrue();
        changeCount.Should().Be(0);
    }

    [Fact]
    public void Toggle_FromClosed_Opens()
    {
        var state = new ChatPanelState();

        state.Toggle();

        state.IsOpen.Should().BeTrue();
        state.PulseRequested.Should().BeTrue();
    }

    [Fact]
    public void Toggle_FromOpen_Minimizes()
    {
        // Header has explicit Minimize and Close buttons; the FAB only
        // renders when Closed. Toggle is the FAB click handler — but if
        // somewhere in the future a header click hits Toggle, we want
        // it to collapse rather than fully close so the user doesn't
        // lose conversation visibility from a mis-click.
        var state = new ChatPanelState();
        state.Open();

        state.Toggle();

        state.IsMinimized.Should().BeTrue();
    }

    [Fact]
    public void Toggle_FromMinimized_Opens()
    {
        var state = new ChatPanelState();
        state.Open();
        state.Minimize();

        state.Toggle();

        state.IsOpen.Should().BeTrue();
        state.PulseRequested.Should().BeTrue();
    }

    [Fact]
    public void AcknowledgePulse_ClearsFlag()
    {
        var state = new ChatPanelState();
        state.Open();
        state.PulseRequested.Should().BeTrue();

        state.AcknowledgePulse();

        state.PulseRequested.Should().BeFalse();
    }

    [Fact]
    public void AcknowledgePulse_WhenAlreadyClear_IsNoOp()
    {
        var state = new ChatPanelState();

        state.AcknowledgePulse();
        state.AcknowledgePulse();

        state.PulseRequested.Should().BeFalse();
    }
}
