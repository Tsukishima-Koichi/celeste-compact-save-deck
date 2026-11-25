# Compact Save Deck

**Expandable save decks for Celeste, with custom ordering and search.**

[简体中文](README.zh-CN.md) · [Changelog](docs/更新记录.md) · [Report an issue](https://github.com/Tsukishima-Koichi/celeste-compact-save-deck/issues)

Compact Save Deck is an Everest mod that organizes your saves without replacing Celeste's vertical save selection layout. Ungrouped saves keep their original cards. Grouped saves appear as a stack that expands into a vertical list, with animated transitions.

**Current version: 0.1.9 — experimental.** Tested on Windows with Celeste 1.4.0.0 FNA and Everest 6696-ultra-de194-stable. Other Everest builds and platforms have not been verified.

## Features

- Create, rename and dissolve decks; select members or move saves between decks.
- Reorder ungrouped saves, entire decks and saves within a deck.
- Search every save and deck, including saves inside collapsed decks.
- Identify saves by their name, last map collection / map and slot number.
- Keep Celeste's native save actions, including continuing, creating, renaming and deleting saves.
- Use menus with native fonts, selection highlights and animated transitions.
- English and Simplified Chinese interface text.

Deck organization does not rename save files or change slot numbers. Dissolving a deck keeps its saves.

## Installation

You need an [Everest](https://everestapi.github.io/) installation. The mod manifest requires Everest **1.6696.0 or newer**; the tested build is listed above.

1. Obtain `CompactSaveDeck.zip` from [Releases](https://github.com/Tsukishima-Koichi/celeste-compact-save-deck/releases) if a release has been published, or build it using the instructions below.
2. Close Celeste and place the ZIP in the game's `Mods` directory. You do not need to extract it.
3. Start Celeste and select **CLIMB**.

Keep only one installed copy: either the ZIP or an extracted `Mods/CompactSaveDeck/` folder. GitHub's automatic **Source code** archives are source files, not ready-to-install mod packages.

To disable the mod, use Everest's mod management interface or add the installed package's name to `Mods/blacklist.txt`. Restart the game after changing its enabled state.

## Controls

| Action | Default input |
| --- | --- |
| Manage decks | **Tab** / controller left trigger |
| Search saves and decks | **R** / controller Start |
| Expand a deck | Right arrow / D-pad right |
| Collapse a deck | Left arrow / D-pad left |
| Navigate, confirm, go back | Celeste's existing menu bindings |

Search, management, expand and collapse bindings can be changed in **Mod Options → CompactSaveDeck → keyboard/controller configuration**. The lower-left magnifying glass and card-stack icons show the current search and management bindings for the active input device. If multiple inputs are assigned, the first binding is shown.

### Organize saves

Focus a save or deck and press **Tab** to open its management menu. Select members to create a deck, change membership, rename a deck or dissolve it.

Choose **Move up / down** to reorder the focused item. Move it vertically, then confirm to save or go back to cancel. Saves inside a deck stay within that deck when reordered; an ungrouped save moves past an expanded deck as one block.

Confirming a deck expands or collapses it. Selecting a save opens Celeste's normal save actions. Use the back binding to close a management menu or return to its parent.

In **Move to deck**, decks are listed alphabetically (ignoring letter case). Press the search binding to search deck names; Enter highlights the best match and closes the input. Confirm separately to move the save. Back cancels the search input.

### Search

Press the search binding (default **R** / controller Start) on the save selection page. A text box and a list of all saves and decks appear.

Type a save name, map name, deck name or slot number such as `#9`, then press **Enter**. The best match is highlighted and brought into view; searching does **not** enter a save automatically. A match inside a collapsed deck reveals that deck's contents.

Names match by exact text, prefix, substring, multiple keywords or abbreviation. Equally ranked results retain your custom order. Use **Esc/back** to leave search; Backspace edits the query.

## Data and compatibility

Deck metadata is stored in `compact-save-deck.json` inside the active save directory, with the previous version retained as `compact-save-deck.json.bak`. This respects `EVEREST_SAVEPATH` when configured. Back up these files alongside your saves if you want to preserve your organization.

Grouping and ordering do not rewrite the game's save files. Native save actions, such as deleting a save, still behave normally.

Current limitations:

- Mouse dragging and mouse-driven deck management are not implemented.
- Replacing a save externally with another save at the same slot number retains that slot's deck membership.
- The native save loader still initializes save slots; very large real-world save collections have not been performance-certified.
- Other mods that replace the save selection menu may conflict.
- Everest can log an inlining warning for the `IdlePosition` hook. No related failure has been observed in the tested setup, but compatibility across runtimes is not guaranteed.

## Build from source

Requirements:

- .NET SDK **10.0.201** or a compatible newer .NET 10 feature band, as specified in `global.json`.
- A local Everest Core installation containing the referenced game assemblies.
- PowerShell for the packaging/deployment scripts.

The mod targets **net8.0**. Game assemblies are referenced locally and are not bundled with the package.

From the repository root, replace the example path with your installation:

```powershell
dotnet build CompactSaveDeck.csproj -c Release "-p:CelestePath=C:\Games\Celeste"
dotnet run --project Tests/CompactSaveDeck.Tests.csproj -c Release
.\scripts\Build.ps1 -CelestePath "C:\Games\Celeste"
```

The package is written to `dist/CompactSaveDeck.zip`. To build and deploy an extracted development copy, close Celeste and run:

```powershell
.\scripts\Deploy.ps1 -CelestePath "C:\Games\Celeste"
```

The deployment script refuses to run while Celeste is running. Remove any separately installed ZIP before using an extracted development copy.

## Validation and feedback

The current test suite passes **978 assertions**, covering ordering and membership, metadata recovery, search ranking and animation state. Release compilation succeeds with no compiler warnings. These checks do not replace in-game or cross-mod compatibility testing.

When reporting a problem, include your mod version, Celeste/Everest version, reproduction steps and relevant `log.txt` entries. For menu conflicts, also list other mods that change save selection.

This repository contains the mod source and development notes. Game assets, game assemblies, private test saves and generated build output are excluded.

## License

Original contributions are licensed under **CC BY-NC 4.0**: you may share and modify them for **non-commercial purposes**. When distributing the original or a modified version, credit **Tsukishima-Koichi**, include a link to the license, retain applicable notices, and indicate changes.

See [LICENSE](LICENSE) for the full terms and [NOTICE](NOTICE) for attribution and scope. Celeste, Everest and third-party material retain their respective rights and licenses. This project is source-available under a non-commercial license, rather than an OSI-approved open-source license.
