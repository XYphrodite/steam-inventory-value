*[Русский](README.md) · English*

# SteamInvValue — Steam inventory watcher

Watches several Steam inventories and works out what they are worth: prices from the Steam
Community Market and from third-party marketplaces, converted to ₽ / $ / USDT / BTC, with a
history of how the value moves. Profile links live in a config file and are read at startup.

```
src/SteamInvValue.Core   core: config, inventory reading, price providers, FX, history
src/SteamInvValue.Cli    console: list, valuation, history, JSON/CSV export
src/SteamInvValue.Web    local site: inventory list, item cards, value chart
```

## Installation

One PowerShell command — it downloads the latest release, puts it where you say and adds
that folder to `PATH`:

```powershell
irm https://raw.githubusercontent.com/XYphrodite/steam-inventory-value/main/install.ps1 | iex
```

It asks for the folder (default `%LOCALAPPDATA%\Programs\SteamInvValue`) and for what to
install — `cli`, `web` or both. Unattended, with options:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/XYphrodite/steam-inventory-value/main/install.ps1))) `
    -Path 'C:\Tools\steaminv' -Components cli -Quiet
```

The same can be set through `STEAMINV_INSTALL_DIR`, `STEAMINV_COMPONENTS` and
`STEAMINV_VERSION`. Use `-NoPath` to leave `PATH` alone and `-Version v0.1.0` for a specific
release.

## Updating

```powershell
steaminv update
```

The program updates itself: it downloads the fresh release, puts it in place of the old one and
keeps running. Nothing has to be closed and no extra windows appear — a running exe cannot be
overwritten but can be renamed, so the old one moves to `.old` and is deleted on the next start.
The new version takes effect the next time you start the program.

The app does not check for updates on its own — that is a request to api.github.com nobody
asked for. The console **asks once** and remembers the answer in the `checkUpdates` field; the
web UI stays quiet until you tick the box in Settings. Once allowed, the check hits the network
at most once a day and the answer is cached. Per run: `--update-check` and `--no-update-check`.
The current version: `steaminv --version`, and in the web header.

Uninstalling uses the same script:

```powershell
& ([scriptblock]::Create((irm <same url>))) -Uninstall          # files and the PATH entry
& ([scriptblock]::Create((irm <same url>))) -Uninstall -Purge   # settings and history too
```

The installer is written in English, ASCII only: a `.ps1` with Cyrillic text needs a UTF-8
BOM on Windows PowerShell 5.1, and that same BOM breaks `irm | iex`. No token is needed; if
the repository is ever made private again, the installer picks one up from `gh auth token`
or `GITHUB_TOKEN`.

Manual installation works too — the archives are on the
[Releases](https://github.com/XYphrodite/steam-inventory-value/releases) page:

| Archive | What is inside |
|---|---|
| `steaminv-cli-win-x64.zip` | `steaminv.exe` — the console app |
| `steaminv-web-win-x64.zip` | `SteamInvValue.Web.exe` — the local site |

The builds are self-contained: no .NET needed on the machine, just unpack and run. Each
archive holds a single file — the web page is embedded into the assembly as a resource.
There is nothing to install; put it wherever you like, and add that folder to `PATH` to call
`steaminv` from anywhere:

```powershell
[Environment]::SetEnvironmentVariable('PATH', $env:PATH + ';C:\Tools\steaminv', 'User')
```

To uninstall, delete the exe and — if you do not want to keep the settings and history —
the `%LOCALAPPDATA%\SteamInvValue` folder.

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project src/SteamInvValue.Cli -- add https://steamcommunity.com/id/nickname
dotnet run --project src/SteamInvValue.Cli                    # value everything in the config
dotnet run --project src/SteamInvValue.Cli -- list            # what is being watched
dotnet run --project src/SteamInvValue.Cli -- history         # how the value changed
dotnet run --project src/SteamInvValue.Cli -- --check         # are the price sources alive
```

To produce the same executables as the release:

```
dotnet publish src/SteamInvValue.Cli -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/cli
```

Releases build themselves: `git tag v0.1.1 && git push --tags` triggers the
[workflow](.github/workflows/release.yml), which publishes both builds and attaches the
archives to the release.

Web panel:

```
steaminv web              # http://localhost:5188
steaminv web 5300         # on a different port
```

From source — `dotnet run --project src/SteamInvValue.Web`. The port comes from the command
argument or the `STEAMINV_URL` variable. The page shows the
watched inventories with their current value on the left, and on the right the summary tiles,
the history chart, per-marketplace and per-game tables, and a filterable item grid.
Inventories are added and removed right there; settings are edited in the Settings panel and
written back to the same config file.

## Config

`%LOCALAPPDATA%\SteamInvValue\config.json`, created on first run. Use a different path with
the `--config` flag or the `STEAMINV_CONFIG` environment variable.

```json
{
  "profiles": [
    { "id": "76561198000000000", "name": "Main", "input": "https://steamcommunity.com/id/nickname",
      "apps": [730, 753], "enabled": true }
  ],
  "steam": { "enabled": true, "budget": 400, "delayMs": 3500 },
  "language": "english",
  "interfaceLanguage": "en",
  "autoRefreshMinutes": 0,
  "proxy": null,
  "cookie": null
}
```

* `id` — SteamID64; it is also the key for the report and the history, and is filled in
  automatically by `add`.
* `apps` — restrict to these games; `null` or empty means every inventory on the profile.
* `enabled: false` — skip during a bulk run without losing the history.
* `language` — the language of item names requested from Steam (`english` / `russian`).
* `interfaceLanguage` — the language of the app itself, `ru` or `en`; empty means the system
  language. Overridden by `--ui` and `STEAMINV_UI`, and changed in the web Settings panel.
* `autoRefreshMinutes` — in web mode, re-value the inventories no more often than this;
  `0` means manual only.

Results are stored next to the config: `reports/<steamid>.json` holds the last full report
(the web UI opens it instantly without touching Steam), and `history/<steamid>.jsonl` holds
one line per run, which is what the chart and the "change" column are built from.

## One-off valuation

A link that is not in the config is valued as given and stored nowhere:

```
dotnet run --project src/SteamInvValue.Cli -- https://steamcommunity.com/id/somebody --no-steam
dotnet run --project src/SteamInvValue.Cli -- nickname --json report.json --csv items.csv
```

## What the numbers mean

| Number | Meaning |
|---|---|
| **Real money** | third-party marketplaces only; each item priced at whichever one pays the most, after its fee |
| **Steam wallet** | everything sold on the Steam Market, minus its fee. This is an internal balance and cannot be withdrawn |
| **Steam list price** | sum of `lowest_price` — what a buyer pays, before the fee |
| **Maximum overall** | the best of every marketplace at once. It mixes cash and wallet money, so the split is printed underneath |
| **Per marketplace** | what you get if you sell everything a given marketplace accepts there |

Steam is kept apart from the rest on purpose: it pays into an internal wallet rather than in
real money, and adding the two into one number would be lying to yourself.

**Liquidity is shown separately.** Every position reports how many such items were sold on
the Steam Market in the last 24 hours. A price without sales is not money: an item worth $15
that nobody bought all day will not sell at that price. Those positions get their own
"no buyers" line and a checkbox filter in the web panel.

**Unsellable items are not counted.** A price only counts if the marketplace would actually
take the item: third parties need `tradable`, the Steam Market needs `marketable`. Whatever
qualifies for neither is reported on its own "cannot be sold" line and stays out of the
totals. Pass `--count-unsellable` for the old behaviour.

Third-party seller fees are hard-coded per provider (`PayoutRate`): Skinport ~12%,
Waxpeer ~6%, Market.CSGO ~5%. The Steam fee is computed exactly
([SteamFee.cs](src/SteamInvValue.Core/SteamFee.cs)): Steam charges two fees, 5% for itself and
10% for the publisher, and **each has a one-cent minimum**. A flat "minus 15%" is therefore
only right for expensive items: a $0.03 sale pays the seller $0.01, a third of it gone. On an
inventory full of trading cards the difference is real.

## Price sources

| Marketplace | Games | How it is fetched |
|---|---|---|
| Steam Market | everything, trading cards (753/6) included | `priceoverview`, one name per request, harshly rate-limited |
| Skinport | 730, 440, 570, 252490 | public price list for the whole catalogue in one request |
| Waxpeer | 730, 570, 440, 252490 | same |
| Market.CSGO | 730 | same |

Cached under `%LOCALAPPDATA%\SteamInvValue\cache`: marketplace price lists for 30 minutes,
individual Steam prices for 12 hours, and the inventory itself for 30 minutes. That last one
matters: without it every repeated run would re-read the inventory and hit the Steam rate
limit. Force a re-read with `--fresh`.

Prices are only requested for what a marketplace would accept. On a large inventory that is
a several-fold difference: 116 Steam requests instead of 1028, because the rest is
untradable anyway.

## About Steam rate limits

This is the main constraint on the whole idea:

* Steam has no bulk price endpoint — one name per request, and it starts returning 429 after
  a few dozen requests in a row. Hence the defaults: a 3.5 s pause and at most 400 names per
  run (`--steam-budget`, `--steam-delay`), with exponential backoff on 429.
* The pause tunes itself: the configured value is the starting point, it grows on every 429
  and creeps back down after a streak of good answers, bounded between a third and one and a
  half of the start. The value it lands on carries over to the next run. On a calm Steam this
  saves about a quarter of the time; under constant 429s it is roughly a tenth slower than a
  fixed pause — the backoff already paid for the refusal, and extra caution costs on top.
* Whatever does not fit the budget is reported as "not queried". Run it again — the cache
  accumulates between runs and coverage grows.
* The inventory endpoint is rate-limited per IP too, and on some networks (Russian ones in
  particular) anonymous requests to `/inventory/` hit 429 almost immediately. Two ways
  around it:

  ```
  --cookie "<steamLoginSecure from your browser>"   # an authenticated session, far softer limits
  --proxy  http://user:pass@host:port               # or socks5://host:port
  ```

  The same values are read from `STEAMINV_COOKIE` and `STEAMINV_PROXY`, and in the web UI
  they are set in the Settings panel and saved to the config. Grab the `steamLoginSecure` cookie in DevTools → Application →
  Cookies → steamcommunity.com; it is full access to the account, so never show it to anyone.
* For CS2 skins the third-party marketplaces are faster and more complete: `--no-steam`
  returns in seconds. Steam is mostly needed for trading cards and anything that is not CS.

## What is missing

* Buff163 — the most representative marketplace for CS2, but it has no public API and needs
  cookies. Not wired up.
* Steam buy orders (the instant-sell price) — that requires scraping the listing page for
  `item_nameid`, one more request per item on top of the rate limit.
* Instant sell to bots (usually 40–60% of the price) — the marketplaces publish listings,
  not their buyout prices.
* CS2 floats and patterns — prices are looked up by `market_hash_name`, i.e. by name and
  wear. A rare float or pattern is worth more than this report shows.

## Privacy

Everything is computed from public data. If an inventory is set to private, neither this tool
nor anyone else can see it; no Steam API key is needed and none is sent anywhere.
