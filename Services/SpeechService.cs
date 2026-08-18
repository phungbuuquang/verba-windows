using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace verba_windows.Services;

public sealed class SpeechService(Dispatcher dispatcher) : ISpeechService
{
    private readonly object _gate = new();
    private dynamic? _voice;
    private Guid? _activeId;
    private bool _disposed;
    private readonly ConcurrentDictionary<string, bool> _voiceAvailability = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<bool>? SpeakingChanged;

    public bool HasMatchingVoice(string languageId)
    {
        return _voiceAvailability.GetOrAdd(languageId, id =>
        {
            dynamic? voice = null;
            try { voice = CreateVoice(); return FindVoice(voice, Normalize(id)) is not null; }
            catch { return false; }
            finally { try { if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice); } catch { } }
        });
    }

    public void Speak(string text, string languageId)
    {
        if (string.IsNullOrWhiteSpace(text) || _disposed) return;
        Stop();
        var id = Guid.NewGuid();
        lock (_gate) _activeId = id;
        SpeakingChanged?.Invoke(this, true);

        _ = Task.Run(() =>
        {
            try
            {
                dynamic voice = CreateVoice();
                var token = FindVoice(voice, Normalize(languageId));
                if (token is not null) voice.Voice = token;
                lock (_gate)
                {
                    if (_activeId != id) return;
                    _voice = voice;
                }
                voice.Speak(text, 0);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Speech failed: {ex}"); }
            finally
            {
                dispatcher.BeginInvoke(() => Finish(id));
            }
        });
    }

    public void Stop()
    {
        dynamic? voice;
        lock (_gate)
        {
            _activeId = null;
            voice = _voice;
            _voice = null;
        }
        try { voice?.Speak("", 2); } catch { }
        SpeakingChanged?.Invoke(this, false);
    }

    private void Finish(Guid id)
    {
        lock (_gate)
        {
            if (_activeId != id) return;
            _activeId = null;
            _voice = null;
        }
        SpeakingChanged?.Invoke(this, false);
    }

    private static dynamic CreateVoice()
    {
        var type = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("Windows Speech API is unavailable.");
        return Activator.CreateInstance(type)!;
    }

    private static dynamic? FindVoice(dynamic voice, string languageId)
    {
        var target = CultureInfo.GetCultureInfo(languageId);
        dynamic tokens = voice.GetVoices("", "");
        dynamic? baseMatch = null;
        dynamic? startsWithMatch = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            dynamic token = tokens.Item(i);
            var culture = TokenCulture(token);
            if (culture is null) continue;
            if (culture.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)) return token;
            if (culture.Name.Equals(target.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                baseMatch ??= token;
            else if (culture.Name.StartsWith(target.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase))
            {
                startsWithMatch ??= token;
            }
        }
        return baseMatch ?? startsWithMatch;
    }

    private static CultureInfo? TokenCulture(dynamic token)
    {
        try
        {
            var value = (string)token.GetAttribute("Language");
            var first = value.Split(';')[0];
            return CultureInfo.GetCultureInfo(Convert.ToInt32(first, 16));
        }
        catch { return null; }
    }

    private static string Normalize(string id) => id switch { "zh-Hans" => "zh-CN", "zh-Hant" => "zh-TW", _ => id };

    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
