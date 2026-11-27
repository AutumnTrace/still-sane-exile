using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Domain.Tests;

/// <summary>Hold mode with pinning: moving onto the card while holding keeps it after release.</summary>
public class CaptureGatePinTests
{
    private const string Peek = "item-peek";

    [Fact]
    public void Release_after_pinning_keeps_the_result_without_another_read()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        Assert.True(gate.TryAccept(Peek, read.Generation));

        Assert.True(gate.Pin(Peek));
        var up = gate.KeyUp(Peek, false);

        Assert.Equal(GateAction.Keep, up.Action);
        Assert.Equal(read.Generation, up.Generation);
        Assert.True(gate.IsShowing(Peek));
        Assert.True(gate.IsPinned(Peek));
        Assert.False(gate.IsHeld(Peek));
        Assert.Equal(1, gate.Counters.Reads);
        Assert.Equal(0, gate.Counters.Hides);
    }

    [Fact]
    public void Release_without_pinning_still_hides()
    {
        var gate = new CaptureGate();
        gate.KeyDown(Peek, false, true);

        Assert.Equal(GateAction.Hide, gate.KeyUp(Peek, false).Action);
        Assert.False(gate.IsShowing(Peek));
    }

    [Fact]
    public void Only_a_held_key_with_something_showing_can_pin()
    {
        var gate = new CaptureGate();
        Assert.False(gate.Pin(Peek)); // nothing pressed

        gate.KeyDown(Peek, false, true);
        gate.KeyUp(Peek, false);
        Assert.False(gate.Pin(Peek)); // released and hidden

        gate.KeyDown(Peek, false, allowedForeground: false);
        Assert.False(gate.Pin(Peek)); // held, but nothing was read

        var toggle = new CaptureGate();
        toggle.Configure(Peek, HoldMode.Toggle);
        toggle.KeyDown(Peek, false, true);
        Assert.False(toggle.Pin(Peek)); // the accessibility mode stays as it was
        Assert.Equal(GateAction.Ignore, toggle.KeyUp(Peek, false).Action);
    }

    [Fact]
    public void The_next_press_replaces_a_pinned_result_with_one_read_and_starts_unpinned()
    {
        var gate = new CaptureGate();
        var first = gate.KeyDown(Peek, false, true);
        gate.Pin(Peek);
        gate.KeyUp(Peek, false);

        var second = gate.KeyDown(Peek, false, true);
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, false, true).Action);
        }

        Assert.Equal(GateAction.Read, second.Action);
        Assert.False(gate.IsPinned(Peek));
        Assert.False(gate.TryAccept(Peek, first.Generation));
        Assert.True(gate.TryAccept(Peek, second.Generation));
        Assert.Equal(GateAction.Hide, gate.KeyUp(Peek, false).Action); // not moved onto this time
        Assert.Equal(2, gate.Counters.Reads);
        Assert.Equal(30, gate.Counters.AutoRepeatIgnored);
    }

    [Fact]
    public void Closing_a_pinned_result_hides_it_and_a_late_result_cannot_return()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        gate.Pin(Peek);
        gate.KeyUp(Peek, false);

        Assert.Equal(GateAction.Hide, gate.Dismiss(Peek, "closed").Action);
        Assert.False(gate.IsShowing(Peek));
        Assert.False(gate.IsPinned(Peek));
        Assert.False(gate.TryAccept(Peek, read.Generation));
        Assert.Equal(GateAction.Ignore, gate.Dismiss(Peek, "closed").Action);
        Assert.Equal(1, gate.Counters.Hides);
    }

    [Fact]
    public void A_press_while_another_window_is_in_front_leaves_the_pinned_result()
    {
        var gate = new CaptureGate();
        gate.KeyDown(Peek, false, true);
        gate.Pin(Peek);
        gate.KeyUp(Peek, false);

        Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, false, allowedForeground: false).Action);
        Assert.Equal(GateAction.Keep, gate.KeyUp(Peek, false).Action);
        Assert.True(gate.IsPinned(Peek));
        Assert.Equal(1, gate.Counters.Reads);
    }

    [Fact]
    public void Synthetic_events_neither_read_nor_release_a_pinned_result()
    {
        var gate = new CaptureGate();
        gate.KeyDown(Peek, false, true);
        gate.Pin(Peek);

        Assert.Equal(GateAction.Ignore, gate.KeyUp(Peek, injected: true).Action);
        Assert.True(gate.IsHeld(Peek));
        gate.KeyUp(Peek, false);
        Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, injected: true, allowedForeground: true).Action);

        Assert.True(gate.IsPinned(Peek));
        Assert.Equal(1, gate.Counters.Reads);
        Assert.Equal(1, gate.Counters.InjectedIgnored);
    }

    [Fact]
    public void Focus_loss_clears_a_pinned_result()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        gate.Pin(Peek);
        gate.KeyUp(Peek, false);

        Assert.Equal(GateAction.Hide, gate.Invalidate("focus lost").Action);
        Assert.False(gate.IsPinned(Peek));
        Assert.False(gate.TryAccept(Peek, read.Generation));
    }
}
