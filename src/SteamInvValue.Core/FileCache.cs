using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamInvValue.Core;

/// <summary>Файловый кэш ответов: прайс-листы площадок и точечные цены Steam.</summary>
public sealed class FileCache
{
    private readonly string _dir;

    public FileCache(string? dir = null)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamInvValue", "cache");
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var safe = new string(key.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return Path.Combine(_dir, $"{safe}_{hash}.json");
    }

    public T? Get<T>(string key, TimeSpan ttl)
    {
        var p = PathFor(key);
        if (!File.Exists(p)) return default;
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(p) > ttl) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(p)); }
        catch { return default; }
    }

    public void Set<T>(string key, T value)
    {
        try { File.WriteAllText(PathFor(key), JsonSerializer.Serialize(value)); }
        catch { /* кэш не критичен */ }
    }

    public async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var cached = Get<T>(key, ttl);
        if (cached is not null) return cached;
        var fresh = await factory();
        if (fresh is not null) Set(key, fresh);
        return fresh;
    }
}
