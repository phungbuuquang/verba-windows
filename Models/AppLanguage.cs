namespace verba_windows.Models;

public enum AppLanguage { En, Vi, Ko }

public static class AppLanguageExtensions
{
    public static string ToId(this AppLanguage value) => value switch
    { AppLanguage.Vi => "vi", AppLanguage.Ko => "ko", _ => "en" };

    public static string DisplayName(this AppLanguage value) => value switch
    { AppLanguage.Vi => "Tiếng Việt", AppLanguage.Ko => "한국어", _ => "English" };

    public static AppLanguage Parse(string? value) => value?.ToLowerInvariant() switch
    { "vi" => AppLanguage.Vi, "ko" => AppLanguage.Ko, _ => AppLanguage.En };
}
