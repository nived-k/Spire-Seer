# Spire Seer

An in-game drafting coach for **Slay the Spire 2**. It scores your options from community runs of your game version: about 370,000 runs for v0.107.1 (main branch) and 144,000 for the v0.111 beta. It picks the matching set automatically.

- **Card rewards:** every card on offer plus SKIP, compared against the average reward screen at the same point. When you're committed to an archetype, a card's score also counts how it does inside that archetype.
- **Shops:** the best set of purchases for the gold you have, card removal included.
- **Ancient / relic choices** and **events** (ranked for your current HP).
- **Deck panel:** your archetype and themes, plus warnings when you spread too thin, bloat the deck, miss AoE/draw/scaling, or have too little HP for the next elite or boss.

Numbers are **lift**: the win rate of runs that made that choice minus the average run at the same point. They are associations from community data, not guarantees.

Display only. It does not change gameplay (`affects_gameplay: false`), uses no network, collects nothing and needs no account.

## Install
- **Steam Workshop:** [subscribe here](https://steamcommunity.com/sharedfiles/filedetails/?id=3838857040), then enable it under Settings → Mod Settings. If you also have a manual copy in `<game folder>/mods/SpireSeer/`, delete it: the game disables the Workshop copy when both exist.
- **Manual:** put `SpireSeer.dll` and `SpireSeer.json` in `<game folder>/mods/SpireSeer/`.

Controls: **F9** shows or hides the panel, **Shift+F9** changes the text size, and you drag the panel to move it. Position and size are saved in the game's user data folder (`SpireSeer.settings.json`).

Characters without community data yet (currently the Fool) show "no data yet" instead of advice.

## Build
You need the .NET 9 SDK. The game's assemblies are not in this repo, so copy `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` from `<game>/data_sts2_<platform>/` to a folder outside the repo.
```
cp local.props.example local.props   # set Sts2RefsDir (and optionally Sts2ModsDir to deploy on build)
dotnet build -c Release              # -> dist/SpireSeer/{SpireSeer.dll, SpireSeer.json}
dotnet test tests/SpireSeer.Tests    # coach logic vs reference outputs (no game needed)
```

## Layout
- `src/Core/`: the coach logic in pure C# with no game references, unit-tested against `tests/golden/`.
- `src/Game/`: reads the run and the screens from the game (Harmony patches). This is the only part a game patch can break.
- `src/UI/`: the overlay panel.
- `data/`: the community library (aggregated statistics only), one file per character and game version (`<character>.<version>.json`), embedded in the dll. At runtime the mod uses the newest version that isn't newer than the game.
- `workshop/`: Steam Workshop metadata for Mega Crit's `ModUploader` (`mod_id.txt` = Workshop item 3838857040; keep it so uploads update the same item).

## Credits
- Community run data and game data: [Spire Codex](https://spire-codex.com), a fan-made database. Spire Codex is not affiliated with Mega Crit.
- Slay the Spire 2 © Mega Crit. This is an unofficial fan mod.

## Licence
MIT. See [LICENSE](LICENSE).
