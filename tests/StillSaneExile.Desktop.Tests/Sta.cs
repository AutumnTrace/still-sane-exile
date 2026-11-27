using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Runs a test body the way the app's UI thread runs: on an STA thread with its own WPF
/// dispatcher. xunit's threads are MTA, and WPF types, DispatcherTimer and the HUD's
/// <c>BeginInvoke</c> calls need a dispatcher that someone pumps.</summary>
internal static class Sta
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
                Drain();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(Timeout))
        {
            throw new TimeoutException($"The test body did not finish within {Timeout.TotalSeconds} s.");
        }

        failure?.Throw();
    }

    /// <summary>Runs everything queued on this thread's dispatcher, including work that the queued
    /// work queues in turn (an <c>await</c> resuming, say), then returns.</summary>
    public static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
