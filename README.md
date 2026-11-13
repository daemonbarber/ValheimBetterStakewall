# ValheimBetterStakewall

A BepInEx mod for Valheim (built against 1.0.21 / BepInEx 5.4.23). Click and drag to build a whole run of stakewalls. Each piece is set to the ground so its lower edge is level with the terrain: nothing floats, and only the uphill edge is buried.

## Controls (hammer, Stakewall selected)

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

On release, the mod charges vanilla costs for every piece placed: materials, stamina, Building skill gain and hammer wear.

## Fill modes (`FillMode`)

These control what happens when a straight section isn't a whole number of pieces long. Pieces are normally spaced at the vanilla snap distance (2 m for stakewalls).

| Mode | Behaviour |
|---|---|
| `NearestWhole` (default) | Vanilla spacing everywhere. The section ends at the nearest whole piece to the cursor, within about 1 m. |
| `OverlapLast` | Vanilla spacing, except the last piece is pulled back over its neighbour so the section ends exactly at the cursor. |
| `OverlapEvenly` | Every piece overlaps by the same amount, so the section ends exactly at the cursor. |

## Grounding rule

For each piece, the ground height is sampled just inside its left and right edges (`EdgeInset`). The piece's base goes at the **lower** of the two heights, minus `GroundSink`. That small extra sink makes sure it counts as grounded for structural support.

## Adding other wall types

Add the prefab names (or piece name tokens) to `EnabledPieces`, comma-separated, in `BepInEx/config/com.valheimbetterstakewall.cfg`. Each piece's width and bottom offset are measured from its snap points and mesh, and the result is written to the log (`Wall profile ...`). If the measurement is wrong, override it:

```
ProfileOverrides = stake_wall:2:0;some_other_wall:4:-0.1
```

## Building

Requires the .NET SDK and the .NET Framework 4.7.1 reference assemblies.

```
dotnet build -c Release
```

The build copies the DLL to `<Valheim>/BepInEx/plugins/ValheimBetterStakewall/`. If Valheim is installed elsewhere, pass `-p:ValheimDir=...`. To build without copying, pass `-p:DeployToGame=false`.

## Code layout

- `src/Plugin.cs`: BepInEx entry point and config
- `src/StretchController.cs`: input state machine, preview ghosts, validation, commit
- `src/PathPlanner.cs`: turns path points into pieces and applies the grounding rule (no game state)
- `src/WallProfiles.cs`: per-prefab geometry, measured automatically or overridden in config
- `src/GameAccess.cs`: typed accessors for the non-public `Player` members used
- `src/Patches.cs`: Harmony hooks on `Player.UpdatePlacement` (input) and `Player.UpdatePlacementGhost` (preview)
