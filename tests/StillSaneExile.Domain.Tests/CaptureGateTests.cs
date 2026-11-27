using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Domain.Tests;

public class CaptureGateTests
{
    private const string Peek = "item-peek";

    [Fact]
    public void Ten_second_hold_with_autorepeat_reads_once()
    {
        var gate = new CaptureGate();
        var first = gate.KeyDown(Peek, injected: false, allowedForeground: true);
        Assert.Equal(GateAction.Read, first.Action);

        // Windows auto-repeat ≈ 30 Hz for 10 s.
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, false, true).Action);
        }

        Assert.Equal(GateAction.Hide, gate.KeyUp(Peek, false).Action);
        Assert.Equal(1, gate.Counters.Reads);
        Assert.Equal(300, gate.Counters.AutoRepeatIgnored);
        Assert.Equal(0, gate.Counters.GameActionsSent);
    }

    [Fact]
    public void Release_before_the_result_arrives_cannot_resurrect_it()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        gate.KeyUp(Peek, false);
        Assert.False(gate.TryAccept(Peek, read.Generation));
        Assert.Equal(1, gate.Counters.StaleResultsDropped);
    }

    [Fact]
    public void Focus_loss_while_held_clears_and_autorepeat_does_not_read_again()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        Assert.Equal(GateAction.Hide, gate.Invalidate("focus lost").Action);
        Assert.False(gate.TryAccept(Peek, read.Generation));

        Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, false, true).Action);
        gate.KeyUp(Peek, false);
        Assert.Equal(GateAction.Read, gate.KeyDown(Peek, false, true).Action);
        Assert.Equal(2, gate.Counters.Reads);
    }

    [Fact]
    public void Synthetic_input_never_reads()
    {
        var gate = new CaptureGate();
        Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, injected: true, allowedForeground: true).Action);
        Assert.Equal(0, gate.Counters.Reads);
        Assert.Equal(1, gate.Counters.InjectedIgnored);
    }

    [Fact]
    public void Wrong_foreground_never_reads()
    {
        var gate = new CaptureGate();
        Assert.Equal(GateAction.Ignore, gate.KeyDown(Peek, false, allowedForeground: false).Action);
        Assert.Equal(0, gate.Counters.Reads);
    }

    [Fact]
    public void Current_result_is_accepted_once_shown()
    {
        var gate = new CaptureGate();
        var read = gate.KeyDown(Peek, false, true);
        Assert.True(gate.TryAccept(Peek, read.Generation));
        Assert.True(gate.IsShowing(Peek));
    }

    [Fact]
    public void Toggle_mode_keeps_snapshot_until_the_next_press()
    {
        var gate = new CaptureGate();
        gate.Configure(Peek, HoldMode.Toggle);

        var read = gate.KeyDown(Peek, false, true);
        Assert.Equal(GateAction.Ignore, gate.KeyUp(Peek, false).Action);
        Assert.True(gate.TryAccept(Peek, read.Generation));

        Assert.Equal(GateAction.Hide, gate.KeyDown(Peek, false, true).Action);
        gate.KeyUp(Peek, false);
        Assert.False(gate.IsShowing(Peek));
        Assert.Equal(1, gate.Counters.Reads);
    }

    [Fact]
    public void A_newer_press_supersedes_an_older_pending_result()
    {
        var gate = new CaptureGate();
        var first = gate.KeyDown(Peek, false, true);
        gate.KeyUp(Peek, false);
        var second = gate.KeyDown(Peek, false, true);
        Assert.False(gate.TryAccept(Peek, first.Generation));
        Assert.True(gate.TryAccept(Peek, second.Generation));
    }
}
