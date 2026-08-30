using System.Reflection;
using System.Text.Json;

namespace SteamInvValue.Core;

/// <summary>Что нашлось в GitHub-релизах.</summary>
public sealed record UpdateInfo(string Current, string Latest, string Url, bool IsNewer);

/// <summary>
/// Версия сборки и проверка обновлений. Запрос уходит на api.github.com, поэтому делается
/// только с явного согласия пользователя (<see cref="AppConfig.CheckUpdates"/>) и не чаще
/// раза в сутки — ответ лежит в том же файловом кэше.
/// </summary>
public static class Updater
{
    public const string Repo = "XYphrodite/steam-inventory-value";
    public const string InstallScript =
        "https://raw.githubusercontent.com/XYphrodite/steam-inventory-value/main/install.ps1";

    /// <summary>Версия из атрибутов сборки; в сборке из исходников без тега — 0.0.0-dev.</summary>
    public static string CurrentVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "0.0.0";
        // SourceLink дописывает "+<хеш коммита>" — в глаза пользователю это не нужно.
        var plus = raw.IndexOf('+');
        var version = plus > 0 ? raw[..plus] : raw;
        return version == "1.0.0" ? "0.0.0-dev" : version;
    }

    /// <summary>Каталог, из которого запущена программа — туда же ставит обновление.</summary>
    public static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static async Task<UpdateInfo?> CheckAsync(FileCache cache, CancellationToken ct = default)
    {
        var latest = await cache.GetOrAddAsync("update_latest", TimeSpan.FromHours(24), async () =>
        {
            try
            {
                var json = await Http.Client.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/releases/latest", ct);
                using var doc = JsonDocument.Parse(json);
                return new LatestRelease(
                    doc.RootElement.GetProperty("tag_name").GetString() ?? "",
                    doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "");
            }
            catch
            {
                return new LatestRelease("", ""); // сеть недоступна — молчим, это не ошибка запуска
            }
        });

        if (string.IsNullOrEmpty(latest.Tag)) return null;
        return new UpdateInfo(CurrentVersion, latest.Tag, latest.Url, IsNewer(latest.Tag, CurrentVersion));
    }

    /// <summary>Сравнивает "v0.2.0" с "0.1.0"; на непонятных строках честно отвечает «не новее».</summary>
    internal static bool IsNewer(string latestTag, string current)
    {
        static Version? Parse(string s)
        {
            s = s.TrimStart('v', 'V');
            var cut = s.IndexOfAny(['-', '+']);
            if (cut > 0) s = s[..cut];
            return Version.TryParse(s, out var v) ? v : null;
        }

        var l = Parse(latestTag);
        var c = Parse(current);
        return l is not null && c is not null && l > c;
    }

    private sealed record LatestRelease(string Tag, string Url);
}
