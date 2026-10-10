using System.Windows;

namespace PrivacyScreen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (PrivacyCursorGuard.TryRun(args)) return 0;

        // Keep multiple launches from overlapping overlays and cursor restoration.
        using var instance = new Mutex(true, @"Local\StandalonePrivacyScreen", out bool created);
        if (!created) return 0;
        try
        {
            return new PrivacyApplication().Run();
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}

internal sealed class PrivacyApplication : Application
{
    private PrivacyOverlayManager? _privacy;
    private bool _exiting;

    internal PrivacyApplication()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Finish(e.Exception.Message);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _privacy = new PrivacyOverlayManager(Dispatcher);
            _privacy.Changed += error =>
            {
                if (!_privacy.IsEnabled) Finish(error);
            };
            _privacy.Enable();
        }
        catch (Exception ex)
        {
            Finish(ex.Message);
        }
    }

    private void Finish(string? error)
    {
        if (_exiting) return;
        _exiting = true;
        _privacy?.Dispose();
        _privacy = null;
        if (error != null)
            MessageBox.Show(error, "Privacy Screen", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(error == null ? 0 : 1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _privacy?.Dispose();
        _privacy = null;
        base.OnExit(e);
    }
}
