using verba_windows.Models;
using verba_windows.Utilities;

namespace verba_windows.Services;

public sealed class AppLanguageStore : ObservableObject
{
    private readonly SettingsStore _settings;
    private AppLanguage _current;

    public AppLanguageStore(SettingsStore settings)
    {
        _settings = settings;
        _current = AppLanguageExtensions.Parse(settings.AppLanguage);
        Strings = new Strings(_current);
    }

    public AppLanguage Current
    {
        get => _current;
        set
        {
            if (!SetProperty(ref _current, value)) return;
            Strings = new Strings(value);
            _settings.SetAppLanguage(value.ToId());
            OnPropertyChanged(nameof(Strings));
        }
    }

    public Strings Strings { get; private set; }
}
