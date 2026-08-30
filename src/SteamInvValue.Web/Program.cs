using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using SteamInvValue.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Конфиг со списком инвентарей читается один раз при входе в приложение.
var configPath = Environment.GetEnvironmentVariable("STEAMINV_CONFIG");
var config = AppConfig.Load(configPath);
var storage = new Storage();
var jobs = new ConcurrentDictionary<string, Job>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// ---- состояние ---------------------------------------------------------------------------

app.MapGet("/api/state", () => Results.Ok(BuildState()));

object BuildState()
{
    var profiles = config.Profiles.Select(p =>
    {
        var last = storage.History(p.Id, 1).LastOrDefault();
        jobs.TryGetValue(p.Id, out var job);
        return new
        {
            p.Id,
            p.Name,
            p.Input,
            p.Apps,
            p.Enabled,
            Url = $"https://steamcommunity.com/profiles/{p.Id}",
            Status = job?.Status ?? "idle",
            LastRun = last?.At,
            BestRub = last?.BestRub ?? 0m,
            BestUsd = last?.BestUsd ?? 0m,
            Items = last?.Items ?? 0,
            HasReport = storage.ReportTime(p.Id) is not null,
        };
    }).ToList();

    return new
    {
        Profiles = profiles,
        TotalRub = profiles.Sum(p => p.BestRub),
        TotalUsd = profiles.Sum(p => p.BestUsd),
        Settings = new
        {
            config.Steam.Enabled,
            config.Steam.Budget,
            config.Steam.DelayMs,
            config.Language,
            InterfaceLanguage = Loc.Lang,
            config.AutoRefreshMinutes,
            HasProxy = Http.HasProxy,
            HasCookie = Http.HasCookie,
        },
        ConfigPath = config.Path,
        Running = jobs.Values.Count(j => j.Status == "running"),
    };
}

// ---- профили -----------------------------------------------------------------------------

app.MapPost("/api/profiles", async (AddRequest req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Profile))
        return Results.BadRequest(new { error = Loc.Pick("Не указана ссылка на профиль", "No profile link given") });
    try
    {
        var p = await config.AddAsync(req.Profile, req.Name, req.Apps, ct);
        Start(p);
        return Results.Ok(new { p.Id, p.Name });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/profiles/{id}", (string id) =>
{
    var p = config.Find(id);
    if (p is null) return Results.NotFound();
    config.Remove(id);
    storage.Forget(p.Id);
    jobs.TryRemove(p.Id, out _);
    return Results.Ok(new { removed = p.Id });
});

app.MapPost("/api/profiles/{id}/toggle", (string id) =>
{
    var p = config.Find(id);
    if (p is null) return Results.NotFound();
    p.Enabled = !p.Enabled;
    config.Save();
    return Results.Ok(new { p.Id, p.Enabled });
});

app.MapPost("/api/profiles/{id}/refresh", (string id) =>
{
    var p = config.Find(id);
    if (p is null) return Results.NotFound();
    return Results.Ok(new { started = Start(p) });
});

app.MapPost("/api/refresh-all", () =>
{
    var started = config.Profiles.Where(p => p.Enabled).Count(Start);
    return Results.Ok(new { started });
});

// ---- отчёты и история --------------------------------------------------------------------

app.MapGet("/api/report/{id}", (string id) =>
{
    var report = storage.LoadReport(id);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.MapGet("/api/history/{id}", (string id, int? limit) => Results.Ok(storage.History(id, limit ?? 200)));

app.MapGet("/api/jobs/{id}", (string id) =>
    jobs.TryGetValue(id, out var job)
        ? Results.Ok(new { job.Status, job.Error, Log = job.Lines })
        : Results.Ok(new { Status = "idle", Error = (string?)null, Log = Array.Empty<string>() }));

// ---- настройки ---------------------------------------------------------------------------

app.MapPut("/api/settings", (SettingsRequest req) =>
{
    if (req.SteamEnabled is not null) config.Steam.Enabled = req.SteamEnabled.Value;
    if (req.SteamBudget is not null) config.Steam.Budget = Math.Max(0, req.SteamBudget.Value);
    if (req.SteamDelayMs is not null) config.Steam.DelayMs = Math.Max(0, req.SteamDelayMs.Value);
    if (!string.IsNullOrWhiteSpace(req.Language)) config.Language = req.Language;
    if (!string.IsNullOrWhiteSpace(req.InterfaceLanguage)) config.InterfaceLanguage = req.InterfaceLanguage;
    if (req.AutoRefreshMinutes is not null) config.AutoRefreshMinutes = Math.Max(0, req.AutoRefreshMinutes.Value);
    if (req.Proxy is not null) config.Proxy = string.IsNullOrWhiteSpace(req.Proxy) ? null : req.Proxy;
    if (req.Cookie is not null) config.Cookie = string.IsNullOrWhiteSpace(req.Cookie) ? null : req.Cookie;
    config.Save();
    config.Apply();
    return Results.Ok(BuildState());
});

// Прокси картинок Steam, чтобы страница не зависела от внешних блокировок.
app.MapGet("/img/{*path}", async (string path, CancellationToken ct) =>
{
    var resp = await Http.Client.GetAsync(
        $"https://community.cloudflare.steamstatic.com/economy/image/{path}", ct);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
    return Results.File(bytes, resp.Content.Headers.ContentType?.ToString() ?? "image/png");
});

// ---- запуск оценки -----------------------------------------------------------------------

bool Start(TrackedProfile p)
{
    var job = jobs.GetOrAdd(p.Id, _ => new Job());
    lock (job)
    {
        if (job.Status == "running") return false;
        job.Reset();
    }

    _ = Task.Run(async () =>
    {
        try
        {
            var report = await new Valuator(log: job.Log)
                .ValuateAsync(string.IsNullOrEmpty(p.Input) ? p.Id : p.Input, config.OptionsFor(p));
            storage.SaveReport(p.Id, report);
            p.LastRun = report.GeneratedAt;
            config.Save();
            job.Status = "done";
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.Status = "error";
            job.Log($"Ошибка: {ex.Message}");
        }
    });
    return true;
}

// Автообновление: раз в config.AutoRefreshMinutes обходим включённые профили.
var timer = new Timer(_ =>
{
    if (config.AutoRefreshMinutes <= 0) return;
    foreach (var p in config.Profiles.Where(p => p.Enabled))
    {
        var last = storage.History(p.Id, 1).LastOrDefault();
        if (last is not null && DateTimeOffset.Now - last.At < TimeSpan.FromMinutes(config.AutoRefreshMinutes)) continue;
        Start(p);
    }
}, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1));

var url = Environment.GetEnvironmentVariable("STEAMINV_URL") ?? "http://localhost:5188";
app.Urls.Add(url);
Console.WriteLine(Loc.Pick($"Конфиг: {config.Path}", $"Config: {config.Path}"));
Console.WriteLine(Loc.Pick($"Инвентарей под наблюдением: {config.Profiles.Count}",
                           $"Inventories watched: {config.Profiles.Count}"));
Console.WriteLine(Loc.Pick($"Открой {url}", $"Open {url}"));
app.Run();
GC.KeepAlive(timer);

sealed record AddRequest(string Profile, string? Name, int[]? Apps);

sealed record SettingsRequest(
    bool? SteamEnabled, int? SteamBudget, int? SteamDelayMs,
    string? Language, string? InterfaceLanguage, int? AutoRefreshMinutes, string? Proxy, string? Cookie);

sealed class Job
{
    private readonly List<string> _lines = [];
    public string Status { get; set; } = "idle";
    public string? Error { get; set; }
    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToArray(); } }

    public void Reset()
    {
        lock (_lines) _lines.Clear();
        Status = "running";
        Error = null;
    }

    public void Log(string m)
    {
        lock (_lines)
        {
            _lines.Add(m);
            if (_lines.Count > 500) _lines.RemoveAt(0);
        }
    }
}
