namespace SteamInvValue.Core;

/// <summary>
/// Есть ли на машине общий .NET нужной версии.
///
/// От этого зависит, какую сборку тянуть при обновлении: со встроенным .NET она весит десятки
/// мегабайт, без него — меньше мегабайта. Проверяем каталогами, а не запуском <c>dotnet
/// --list-runtimes</c>: лишний процесс ради ответа, который лежит на диске, ни к чему.
/// </summary>
public static class DotnetRuntime
{
    /// <summary>Базовый рантайм — нужен консоли.</summary>
    public static bool HasCore(int major = 10) => Has("Microsoft.NETCore.App", major);

    /// <summary>Рантайм ASP.NET — нужен веб-панели.</summary>
    public static bool HasAspNet(int major = 10) => Has("Microsoft.AspNetCore.App", major);

    private static bool Has(string name, int major)
    {
        foreach (var root in Roots())
        {
            var dir = Path.Combine(root, "shared", name);
            if (!Directory.Exists(dir)) continue;

            foreach (var version in Directory.EnumerateDirectories(dir))
                if (Version.TryParse(Path.GetFileName(version), out var parsed) && parsed.Major >= major)
                    return true;
        }

        return false;
    }

    private static IEnumerable<string> Roots()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } explicitRoot)
            yield return explicitRoot;

        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path)) yield return Path.Combine(path, "dotnet");
        }
    }
}
