using System.Configuration;
using System.Data;
using System.Windows;

using Velopack;

namespace verba_windows;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private AppHost.PanelController? _controller;
    private AppHost.TrayIcon? _tray;
    private AppHost.SelectionTranslateService? _selectionTranslate;
    private Services.AppUpdateService? _updates;
    private Services.TranslationLanguageCatalog? _languageCatalog;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _checkingForUpdates;

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

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
        _languageCatalog = new Services.TranslationLanguageCatalog();
        var speech = new Services.SpeechService(Dispatcher);
        var viewModel = new ViewModels.TranslationViewModel(
            new Services.TranslationApiService(), speech, settings, language, tones, _languageCatalog);
        var window = new AppHost.PanelWindow(viewModel);
        var selection = new Services.Win32SelectionCapture();
        _controller = new AppHost.PanelController(window, viewModel, selection, settings, new Services.StartupService());
        _controller.QuitRequested += Quit;
        _updates = new Services.AppUpdateService();
        _tray = new AppHost.TrayIcon(_controller, language, _updates.IsInstalled);
        _tray.QuitRequested += Quit;
        _tray.CheckForUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(true);
        _tray.ApplyUpdateRequested += (_, _) => ApplyUpdateAndRestart();
        try
        {
            _selectionTranslate = new AppHost.SelectionTranslateService(_controller, selection, language);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Selection translation popup unavailable: {ex}");
        }
        System.Diagnostics.Trace.WriteLine($"Tray icon initialized; {_controller.ShortcutText} registered={_controller.HotkeyRegistered}");
        _ = CheckForUpdatesAfterStartupAsync();
        _ = _languageCatalog.RunAsync(_shutdown.Token);
    }

    private async Task CheckForUpdatesAfterStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), _shutdown.Token);
            await CheckForUpdatesAsync(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_checkingForUpdates || _updates is null || _tray is null || !_updates.IsInstalled) return;
        _checkingForUpdates = true;
        _tray.SetCheckingForUpdates();
        try
        {
            var update = await _updates.CheckForUpdatesAsync();
            if (update is null)
            {
                if (userInitiated) _tray.ShowUpToDate();
                else _tray.ResetUpdateStatus();
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            _tray.SetDownloadingUpdate(version);
            await _updates.DownloadUpdatesAsync(update, _shutdown.Token);
            if (!_shutdown.IsCancellationRequested) _tray.SetUpdateReady(version);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Update check failed: {ex}");
            if (userInitiated) _tray.ShowUpdateCheckFailed();
            else _tray.ResetUpdateStatus();
        }
        finally { _checkingForUpdates = false; }
    }

    private void ApplyUpdateAndRestart()
    {
        if (_updates?.BeginApplyAndRestart() != true) return;
        Shutdown();
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
        _shutdown.Cancel();
        _selectionTranslate?.Dispose(); _tray?.Dispose(); _controller?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }
}

