using System.Configuration;
using System.Data;
using System.Windows;

namespace verba_windows;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private AppHost.PanelController? _controller;
    private AppHost.TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        DispatcherUnhandledException += (_, args) => System.Diagnostics.Trace.WriteLine($"Unhandled UI exception: {args.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => System.Diagnostics.Trace.WriteLine($"Unhandled exception: {args.ExceptionObject}");
        System.Diagnostics.Trace.WriteLine($"App startup PID={Environment.ProcessId}");
        _mutex = new Mutex(true, "Local\\verba.Windows.SingleInstance", out var firstInstance);
        System.Diagnostics.Trace.WriteLine($"Single instance owner={firstInstance}");
        if (!firstInstance) { Shutdown(); return; }
        base.OnStartup(e);

        var settings = new Services.SettingsStore();
        var language = new Services.AppLanguageStore(settings);
        var tones = new Services.CustomToneStore(settings);
        var speech = new Services.SpeechService(Dispatcher);
        var viewModel = new ViewModels.TranslationViewModel(new Services.TranslationApiService(), speech, settings, language, tones);
        var window = new AppHost.PanelWindow(viewModel);
        _controller = new AppHost.PanelController(window, viewModel, new Services.Win32SelectionCapture(), settings);
        _controller.QuitRequested += Quit;
        _tray = new AppHost.TrayIcon(_controller);
        _tray.QuitRequested += Quit;
        System.Diagnostics.Trace.WriteLine($"Tray icon initialized; {_controller.ShortcutText} registered={_controller.HotkeyRegistered}");
    }

    private static void ConfigureLogging()
    {
        try
        {
            var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "verba");
            System.IO.Directory.CreateDirectory(directory);
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(System.IO.Path.Combine(directory, "verba.log")));
            System.Diagnostics.Trace.AutoFlush = true;
        }
        catch { }
    }

    private void Quit(object? sender, EventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose(); _controller?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

