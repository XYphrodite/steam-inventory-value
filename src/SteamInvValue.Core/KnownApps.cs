namespace SteamInvValue.Core;

/// <summary>Инвентари, которые встречаются чаще всего — используются как запасной путь,
/// если страница профиля не отдала g_rgAppContextData (например, Steam режет запросы).</summary>
public static class KnownApps
{
    public static readonly (int AppId, string Name, string ContextId)[] All =
    [
        (753, "Steam (карточки, фоны, эмодзи)", "6"),
        (730, "Counter-Strike 2", "2"),
        (570, "Dota 2", "2"),
        (440, "Team Fortress 2", "2"),
        (252490, "Rust", "2"),
        (578080, "PUBG", "2"),
        (232090, "Killing Floor 2", "2"),
        (304930, "Unturned", "2"),
        (218620, "PAYDAY 2", "2"),
        (322330, "Don't Starve Together", "2"),
        (291550, "Brawlhalla", "2"),
        (238460, "BattleBlock Theater", "2"),
    ];
}
