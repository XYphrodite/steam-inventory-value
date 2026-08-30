using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInvValue.Core;

/// <summary>Инвентарь, за которым следим.</summary>
public sealed class TrackedProfile
{
    /// <summary>SteamID64 — он же идентификатор в истории и отчётах.</summary>
    public string Id { get; set; } = "";
    /// <summary>Как показывать в списке.</summary>
    public string Name { get; set; } = "";
    /// <summary>То, что ввёл пользователь: ссылка, ник или SteamID.</summary>
    public string Input { get; set; } = "";
    /// <summary>Ограничение по играм; пусто — все инвентари профиля.</summary>
    public int[]? Apps { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastRun { get; set; }
}

public sealed class SteamOptions
{
    /// <summary>Спрашивать ли Steam Market (медленно, но это единственный источник для карточек).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Сколько имён максимум опросить за один запуск.</summary>
    public int Budget { get; set; } = 400;
    /// <summary>Пауза между запросами к Steam Market, мс.</summary>
    public int DelayMs { get; set; } = 3500;
}

public sealed class AppConfig
{
    public List<TrackedProfile> Profiles { get; set; } = [];
    public SteamOptions Steam { get; set; } = new();
    /// <summary>Язык названий предметов, который запрашивается у Steam: english / russian.</summary>
    public string Language { get; set; } = "english";
    /// <summary>Язык интерфейса приложения: ru / en. Пусто — берётся язык системы.</summary>
    public string? InterfaceLanguage { get; set; }
    /// <summary>Автообновление в веб-режиме, минут. 0 — не обновлять само.</summary>
    public int AutoRefreshMinutes { get; set; }
    /// <summary>http://user:pass@host:port или socks5://host:port</summary>
    public string? Proxy { get; set; }
    /// <summary>steamLoginSecure авторизованной сессии — снимает лимит 429 на инвентарь.</summary>
    public string? Cookie { get; set; }

    [JsonIgnore] public string Path { get; private set; } = "";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>%LOCALAPPDATA%\SteamInvValue\config.json, либо путь из STEAMINV_CONFIG.</summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("STEAMINV_CONFIG") is { Length: > 0 } p
            ? p
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamInvValue", "config.json");

    /// <summary>Читает конфиг при старте; если файла нет — создаёт с настройками по умолчанию.</summary>
    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        AppConfig cfg;

        if (File.Exists(path))
        {
            try { cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new AppConfig(); }
            catch (Exception ex) { throw new InvalidOperationException(S.ConfigUnreadable(path, ex.Message)); }
        }
        else
        {
            cfg = new AppConfig();
            cfg.Path = path;
            cfg.Save();
        }

        cfg.Path = path;
        cfg.Apply();
        return cfg;
    }

    /// <summary>
    /// Применяет настройки процесса: язык интерфейса, прокси и cookie для Steam.
    /// Вызывается при входе в приложение и после правки настроек.
    /// </summary>
    public void Apply()
    {
        InterfaceLanguage = Loc.Normalize(InterfaceLanguage);
        Loc.Lang = InterfaceLanguage;
        Http.Configure(Proxy, Cookie);
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));
    }

    public TrackedProfile? Find(string key) =>
        Profiles.FirstOrDefault(p =>
            p.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            p.Input.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Добавляет профиль, разрешив ссылку в SteamID64. Повторное добавление обновляет запись.</summary>
    public async Task<TrackedProfile> AddAsync(string input, string? name = null, int[]? apps = null,
        CancellationToken ct = default)
    {
        var resolved = await SteamIdResolver.ResolveAsync(input, ct);
        var existing = Profiles.FirstOrDefault(p => p.Id == resolved.SteamId64);

        var profile = existing ?? new TrackedProfile { Id = resolved.SteamId64 };
        profile.Input = input;
        profile.Name = name ?? (string.IsNullOrWhiteSpace(profile.Name)
            ? resolved.PersonaName ?? resolved.SteamId64
            : profile.Name);
        if (apps is not null) profile.Apps = apps.Length > 0 ? apps : null;

        if (existing is null) Profiles.Add(profile);
        Save();
        return profile;
    }

    public bool Remove(string key)
    {
        var p = Find(key);
        if (p is null) return false;
        Profiles.Remove(p);
        Save();
        return true;
    }

    /// <summary>Настройки оценки для конкретного профиля.</summary>
    public ValuationOptions OptionsFor(TrackedProfile p) => new()
    {
        OnlyApps = p.Apps,
        UseSteam = Steam.Enabled,
        SteamBudget = Steam.Budget,
        SteamDelayMs = Steam.DelayMs,
        Language = Language,
    };
}
