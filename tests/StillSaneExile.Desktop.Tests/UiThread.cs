using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Runs view tests on one long-lived STA thread that owns the app's <see cref="Application"/>, so
/// views find App.xaml's brushes and styles. A process can create only one Application, and its
/// resources belong to the thread that made it, so every view test runs here, one at a time. View
/// model tests keep using <see cref="Sta"/>.</summary>
internal static class UiThread
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly Lazy<Dispatcher> Owner = new(Start);

    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var operation = Owner.Value.InvokeAsync(() =>
        {
            try
            {
                body();
                Sta.Drain();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        if (operation.Wait(Timeout) != DispatcherOperationStatus.Completed)
        {
            throw new TimeoutException($"The view test did not finish within {Timeout.TotalSeconds} s.");
        }

        failure?.Throw();
    }

    private static Dispatcher Start()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                Dispatcher.Run();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();
        failure?.Throw();
        return dispatcher!;
    }
}
