using Microsoft.Win32;
using System.IO;

namespace verba_windows.Services;

public interface IStartupService
{
    bool IsEnabled { get; }
    bool TrySetEnabled(bool enabled);
}

public interface IStartupRegistrationStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

public sealed class StartupService : IStartupService
{
    private readonly IStartupRegistrationStore _store;
    private readonly string _command;

    public StartupService(IStartupRegistrationStore? store = null, string? executablePath = null)
    {
        _store = store ?? new RegistryStartupRegistrationStore();
        var path = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable.");
        _command = $"\"{Path.GetFullPath(path)}\" --startup";
    }

    public bool IsEnabled
    {
        get
        {
            try { return !string.IsNullOrWhiteSpace(_store.Read()); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Could not read startup registration: {ex}");
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            if (enabled) _store.Write(_command); else _store.Delete();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Could not update startup registration: {ex}");
            return false;
        }
    }

    private sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Verba";

        public string? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) as string;
        }

        public void Write(string command)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
                ?? throw new InvalidOperationException("The Windows startup registry key is unavailable.");
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }

        public void Delete()
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            key?.DeleteValue(ValueName, false);
        }
    }
}
