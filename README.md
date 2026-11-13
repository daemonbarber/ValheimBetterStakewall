# BetterStakewall

Click and drag with the hammer to build a whole run of stakewalls. Each piece is set to the ground so its lower edge is level with the terrain: nothing floats, and only the uphill edge is buried.

## Features

- **Drag to build:** press and hold place, drag, and let go to build the whole row at once. You can walk while holding to make long runs.
- **Corners:** right-click while dragging to fix a corner and keep going in a new direction.
- **Terrain-hugging:** every piece sits level with the ground on its low edge, so walls follow slopes without gaps underneath.
- **Snaps to existing walls:** start on the end of an existing stakewall and the new row continues from it, with the same side facing out.
- **Fill modes:** choose how a section ends when it isn't a whole number of pieces long.
- **All or nothing:** the row is only built if every piece can be placed, so you never end up with half a wall.
- **Vanilla costs:** materials, stamina, tool wear, skill gain and player stats are applied per piece, the same as building them one at a time.
- Works with the regular stakewall and the Dvergr stakewall. Other walls can be added in the config.

## Installation

Install with a mod manager, or extract the zip into `BepInEx/plugins/BetterStakewall/`. Requires BepInEx (built against Valheim 1.0.21 / BepInEx 5.4.23).

## Controls (hammer, stakewall selected)

| Action | Input |
|---|---|
| Start a wall | Press and hold **place** (LMB / gamepad place). The start snaps to the end of an existing stakewall if one is nearby. |
| Stretch | Drag the cursor. You can walk while holding to make long runs. |
| Which side faces out | Set when you press. If you start on the end of an existing stakewall, the new row matches that wall's sides. Otherwise it follows the ghost's rotation (scroll before pressing to turn it). The same side stays outward around corners. |
| Flip sides | **Scroll** while holding |
| Add a corner | **Right-click** while holding. The build menu doesn't open during a drag. |
| Change fill mode | **Q / E** while holding (vanilla's snap-point keys, taken over during a drag). The choice is saved. |
| Remove last corner / cancel | **Backspace** (`UndoCornerKey`). With no corners, this cancels the drag. |
| Build | Release **place** |
| Single piece | Click without dragging (vanilla behaviour) |

The whole row is built only if every check passes: ground under every piece, outside no-build zones, ward access, a workbench in range, enough materials for every piece, enough tool durability, and enough stamina for one swing. If any check fails, nothing is built and the reason is shown on screen.

### Fill modes (`FillMode`)

These control what happens when a straight section isn't a whole number of pieces long. Pieces are normally spaced at the vanilla snap distance (2 m for stakewalls).

| Mode | Behaviour |
|---|---|
| `NearestWhole` (default) | Vanilla spacing everywhere. The section ends at the nearest whole piece to the cursor, within about 1 m. |
| `OverlapLast` | Vanilla spacing, except the last piece is pulled back over its neighbour so the section ends exactly at the cursor. |
| `OverlapEvenly` | Every piece overlaps by the same amount, so the section ends exactly at the cursor. |

### Grounding rule

For each piece, the ground height is sampled just inside its left and right edges (`EdgeInset`). The piece's base goes at the **lower** of the two heights, minus `GroundSink`. That small extra sink makes sure it counts as grounded for structural support.

## Configuration

Settings are in `BepInEx/config/com.valheimbetterstakewall.cfg`, created on first launch.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turns click-and-drag stretching on or off. |
| `EnabledPieces` | `stake_wall, piece_dvergr_stake_wall` | Which pieces can be stretched. |
| `ProfileOverrides` | *(empty)* | Manual geometry for pieces the mod measures wrongly (see below). |
| `UndoCornerKey` | `Backspace` | Removes the last corner while dragging, or cancels with none. |
| `DragThreshold` | `1` | How far (m) the cursor must move before a press becomes a drag. |
| `MaxPieces` | `64` | Most pieces in one drag (1–512). |
| `FillMode` | `NearestWhole` | How sections end (see above). Also changed with Q/E while dragging. |
| `Overlap` | `0` | Fraction of a piece's width that neighbours overlap (0–0.5). |
| `SnapRadius` | `0.6` | Path points this close (m) to an existing wall's snap point snap onto it. |
| `EdgeInset` | `0.1` | How far in (m) from each edge the ground is sampled. |
| `GroundSink` | `0.05` | Extra depth (m) the low edge is pushed into the ground. |
| `DebugLogging` | `false` | Logs stretch planning details. |

### Adding other wall types

Add the prefab names (or piece name tokens) to `EnabledPieces`, comma-separated. Each piece's width and bottom offset are measured from its snap points and mesh. With `DebugLogging` on, the result is written to the log (`Wall profile ...`). If the measurement is wrong, override it:

```
ProfileOverrides = stake_wall:2:0;some_other_wall:4:-0.1
```

## Compatibility

- Client-side: only the player building needs it. The pieces it places are ordinary pieces, so other players and servers don't need the mod.
- May conflict with other mods that take over hammer placement input (`Player.UpdatePlacement`).

## Building from source

Requires the .NET SDK and the .NET Framework 4.7.1 reference assemblies.

```
dotnet build -c Release
```

The build copies the DLL to `<Valheim>/BepInEx/plugins/ValheimBetterStakewall/`. If Valheim is installed elsewhere, pass `-p:ValheimDir=...`. To build without copying, pass `-p:DeployToGame=false`.

To make the release zip (Thunderstore layout, written to `dist/`):

```
powershell -ExecutionPolicy Bypass -File package.ps1
```

### Code layout

- `src/Plugin.cs`: BepInEx entry point and config
- `src/StretchController.cs`: input state machine, preview ghosts, validation, commit
- `src/PathPlanner.cs`: turns path points into pieces and applies the grounding rule (no game state)
- `src/WallProfiles.cs`: per-prefab geometry, measured automatically or overridden in config
- `src/GameAccess.cs`: typed accessors for the non-public `Player` members used
- `src/Patches.cs`: Harmony hooks on `Player.UpdatePlacement` (input) and `Player.UpdatePlacementGhost` (preview)
