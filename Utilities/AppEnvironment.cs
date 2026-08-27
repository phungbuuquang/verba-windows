using System.Reflection;
using System.Runtime.InteropServices;

namespace verba_windows.Utilities;

/// <summary>Static facts about this build and machine, as sent to the installation registry.</summary>
public static class AppEnvironment
{
    public const string Platform = "windows";

    /// <summary>Informational version without any build metadata suffix, for example "1.4.0".</summary>
    public static string AppVersion { get; } = ReadAppVersion();

    /// <summary>Fourth component of the file version, or the revision when the installer stamps one.</summary>
    public static string BuildNumber { get; } = ReadBuildNumber();

    /// <summary>Windows version as major.minor.build, for example "10.0.26200".</summary>
    public static string OsVersion { get; } = Environment.OSVersion.Version.ToString(3);

    public static string Architecture { get; } = RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        var other => other.ToString().ToLowerInvariant()
    };

    private static string ReadAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppEnvironment).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit>" source-revision suffix the SDK appends.
            var plus = informational.IndexOf('+');
            var trimmed = plus >= 0 ? informational[..plus] : informational;
            if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
        }
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string ReadBuildNumber()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppEnvironment).Assembly;
        var revision = assembly.GetName().Version?.Revision ?? 0;
        return revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
