namespace verba_windows.Services;

public interface ISpeechService : IDisposable
{
    event EventHandler<bool>? SpeakingChanged;
    bool HasMatchingVoice(string languageId);
    void Speak(string text, string languageId);
    void Stop();
}
