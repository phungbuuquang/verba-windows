using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace verba_windows.Services;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private SettingsData _data;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "verba", "settings.json");
        _data = Load();
    }

    public string? DeviceId { get { lock (_gate) return _data.DeviceId; } }
    public string AppLanguage { get { lock (_gate) return _data.AppLanguage ?? "en"; } }
    public string Shortcut { get { lock (_gate) return _data.Shortcut ?? "Ctrl+Shift+V"; } }
    public IReadOnlyList<Models.CustomTone> CustomTones { get { lock (_gate) return _data.CustomTones?.ToList() ?? []; } }
    public (double Left, double Top)? PanelPosition
    {
        get { lock (_gate) return _data.PanelLeft is double l && _data.PanelTop is double t ? (l, t) : null; }
    }

    public string GetOrCreateDeviceId()
    {
        lock (_gate)
        {
            if (Guid.TryParse(_data.DeviceId, out _)) return _data.DeviceId!;
            _data.DeviceId = Guid.NewGuid().ToString();
            SaveUnsafe();
            return _data.DeviceId;
        }
    }

    public void SetAppLanguage(string id) => Update(d => d.AppLanguage = id);
    public void SetShortcut(string shortcut) => Update(d => d.Shortcut = shortcut);
    public void SetCustomTones(IReadOnlyList<Models.CustomTone> tones) => Update(d => d.CustomTones = tones.ToList());
    public void SetPanelPosition(double left, double top) => Update(d => { d.PanelLeft = left; d.PanelTop = top; });

    private void Update(Action<SettingsData> change)
    {
        lock (_gate) { change(_data); SaveUnsafe(); }
    }

    private SettingsData Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path), JsonOptions) ?? new();
        }
        catch { return new(); }
    }

    private void SaveUnsafe()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_data, JsonOptions));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Settings save failed: {ex}"); }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private sealed class SettingsData
    {
        [JsonPropertyName("verba.deviceId")]
        public string? DeviceId { get; set; }
        [JsonPropertyName("verba.appLanguage")]
        public string? AppLanguage { get; set; } = "en";
        [JsonPropertyName("verba.shortcut")]
        public string? Shortcut { get; set; } = "Ctrl+Shift+V";
        [JsonPropertyName("verba.customTones")]
        public List<Models.CustomTone>? CustomTones { get; set; } = [];
        [JsonPropertyName("panel.left")]
        public double? PanelLeft { get; set; }
        [JsonPropertyName("panel.top")]
        public double? PanelTop { get; set; }
    }
}
