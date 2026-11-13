using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimBetterStakewall
{
    /// <summary>
    /// Click-and-drag wall placement:
    ///   press place         -> start point (snaps to the end of an existing wall)
    ///   drag                -> preview fills the path
    ///   right-click         -> fix a corner at the cursor (multi-point path)
    ///   UndoCornerKey       -> drop the last corner, or cancel if there are none
    ///   release             -> build the whole row, or nothing if any check fails
    /// A press released before DragThreshold is passed falls back to vanilla single placement.
    /// </summary>
    internal static class StretchController
    {
        private enum State { Idle, Pressed, Stretching }

        private static State _state = State.Idle;
        private static Piece _prefab;
        private static WallProfile _profile;
        private static readonly List<Vector3> _corners = new List<Vector3>();   // fixed path points
        private static Vector3 _cursor;
        private static Vector3 _referenceNormal;   // which way the wall's face points, fixed at press time
        private static readonly List<Vector3> _pathScratch = new List<Vector3>();
        private static readonly List<PlannedPiece> _plan = new List<PlannedPiece>();
        private static readonly List<bool> _pieceOk = new List<bool>();
        private static readonly List<GameObject> _ghosts = new List<GameObject>();
        private static string _hudCost = "";
        private static string _hudError = "";
        private static bool _suppressRemoveUntilRelease;

        private static readonly List<Transform> _snapPoints = new List<Transform>();
        private static readonly List<Piece> _snapPieces = new List<Piece>();

        public static bool IsActive => _state != State.Idle;

        /// <summary>True while the mod itself is calling PlacePiece (so debug hooks can tell vanilla placements apart).</summary>
        public static bool IsCommitting { get; private set; }

        /// <summary>True only while preview clones are being instantiated (lets the WearNTear.Awake patch skip them).</summary>
        public static bool CreatingPreview { get; private set; }

        // ------------------------------------------------------------------ input (Update)

        public static bool OnUpdatePlacement(Player player, bool takeInput)
        {
            if (_state == State.Idle)
            {
                GuardRemoveAfterCorner(player);
                if (!Plugin.Enabled.Value || !CanStart(player, takeInput)) return true;

                var piece = player.GetSelectedPiece();
                string prefabName = piece ? Utils.GetPrefabName(piece.gameObject) : "<none>";
                var profile = WallProfiles.Get(piece);
                if (profile == null)
                {
                    Plugin.Debug($"Place pressed: selected prefab '{prefabName}' ({(piece ? piece.m_name : "")}) is not in EnabledPieces '{Plugin.EnabledPieces.Value}'.");
                    return true;
                }
                if (!TryGetCursor(player, piece, out var start))
                {
                    Plugin.Debug($"Place pressed on '{prefabName}' but the cursor ray found no point within reach.");
                    return true;   // let vanilla report the problem
                }

                _prefab = piece;
                _profile = profile;
                _corners.Clear();
                Piece anchor;
                if (TryGetGhostSnap(player, out var snapped, out var snappedTo))
                {
                    // Vanilla already snapped the ghost onto another piece: start exactly at that joint.
                    _corners.Add(snapped);
                    anchor = WallProfiles.IsEnabled(snappedTo) ? snappedTo : null;
                }
                else
                {
                    _corners.Add(Snap(start, out anchor));
                }
                _cursor = start;
                _referenceNormal = AnchorNormal(player, anchor);
                _state = State.Pressed;
                Plugin.Debug($"Stretch start at {_corners[0]}, face {_referenceNormal} from {(anchor ? "existing " + anchor.name : "ghost")}");
                return false;
            }

            if (!ContextStillValid(player, takeInput))
            {
                Cancel();
                return true;
            }

            // Right-click is the "BuildMenu" button; vanilla reads it in UpdateBuildGuiInput, which runs inside
            // UpdatePlacement and is therefore skipped while dragging, so the menu stays shut. On a gamepad the
            // remove button adds the corner instead.
            if (ZInput.GetButtonDown("BuildMenu") || ZInput.GetButtonDown("JoyRemove"))
                AddCorner();
            // A remove press during a drag (gamepad corner, or a stray middle-click) must not remove a piece
            // when it is released after the drag ends.
            if (ZInput.GetButtonDown("Remove") || ZInput.GetButtonDown("JoyRemove"))
                _suppressRemoveUntilRelease = true;
            if (_suppressRemoveUntilRelease) GameAccess.BlockRemove(player) = true;

            // Vanilla rotation input is skipped while dragging, so the scroll wheel flips the face side instead.
            if (Mathf.Abs(ZInput.GetMouseScrollWheel()) > 0.01f) _referenceNormal = -_referenceNormal;

            // Q/E (vanilla snap-point cycling, blocked while dragging) step through fill modes instead.
            if (ZInput.GetButtonDown("TabRight") || ZInput.GetButtonDown("JoyNextSnap"))
                CycleFillMode(+1);
            if (ZInput.GetButtonDown("TabLeft") || ZInput.GetButtonDown("JoyPrevSnap"))
                CycleFillMode(-1);

            if (ZInput.GetKeyDown(Plugin.UndoCornerKey.Value, false))
            {
                if (_corners.Count > 1) _corners.RemoveAt(_corners.Count - 1);
                else { Cancel(); return false; }
            }

            if (PlaceHeld()) return false;

            // Released.
            if (_state == State.Pressed)
            {
                // Never stretched: hand the click back to vanilla so it places one piece now.
                End();
                GameAccess.PlacePressedTime(player) = Time.time;
                return true;
            }

            Commit(player);
            End();
            return false;
        }

        private static void CycleFillMode(int delta)
        {
            const int modes = 3;
            // Setting the ConfigEntry also saves it, so the choice sticks between sessions.
            Plugin.Fill.Value = (FillMode)((((int)Plugin.Fill.Value + delta) % modes + modes) % modes);
        }

        private static bool CanStart(Player player, bool takeInput)
        {
            if (!(ZInput.GetButtonDown("Attack") || ZInput.GetButtonDown("JoyPlace"))) return false;
            if (!player.InPlaceMode()) return false;   // not holding a build tool: stay silent

            string blocked =
                !takeInput ? "player is not taking input" :
                player.IsDead() ? "player is dead" :
                Hud.IsPieceSelectionVisible() ? "build menu is open" :
                Hud.InRadial() ? "radial menu is open" :
                ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltKeys") ? "AltPlace is held" :
                null;
            if (blocked != null) Plugin.Debug("Place pressed but stretch not started: " + blocked);
            return blocked == null;
        }

        private static bool ContextStillValid(Player player, bool takeInput)
        {
            return takeInput
                && player.InPlaceMode()
                && !player.IsDead()
                && player.GetSelectedPiece() == _prefab
                && !Hud.IsPieceSelectionVisible()
                && !Menu.IsVisible()
                && !InventoryGui.IsVisible();
        }

        private static bool PlaceHeld() => ZInput.GetButton("Attack") || ZInput.GetButton("JoyPlace");

        // A remove press made during a drag must not later act as "remove piece" in vanilla.
        private static void GuardRemoveAfterCorner(Player player)
        {
            if (!_suppressRemoveUntilRelease) return;
            GameAccess.BlockRemove(player) = true;
            bool held = ZInput.GetButton("Remove") || ZInput.GetButton("JoyRemove");
            bool released = ZInput.GetButtonUp("Remove") || ZInput.GetButtonUp("JoyRemove");
            if (!held && !released) _suppressRemoveUntilRelease = false;
        }

        private static void AddCorner()
        {
            var p = Snap(_cursor);
            if (Vector3.Distance(PathPlanner.Flat(p), PathPlanner.Flat(_corners[_corners.Count - 1])) < 0.5f)
            {
                Plugin.Debug($"Corner ignored: {p} is within 0.5m of the previous corner.");
                return;
            }
            _corners.Add(p);
            _state = State.Stretching;
            Plugin.Debug($"Corner {_corners.Count - 1} at {p}");
        }

        // ------------------------------------------------------------------ preview (LateUpdate)

        public static void OnLateUpdate(Player player)
        {
            if (_state == State.Idle) return;

            if (TryGetCursor(player, _prefab, out var c)) _cursor = c;
            if (_state == State.Pressed &&
                Vector3.Distance(PathPlanner.Flat(_cursor), PathPlanner.Flat(_corners[0])) >= Plugin.DragThreshold.Value)
            {
                _state = State.Stretching;
            }
            if (_state != State.Stretching) return;

            var ghost = GameAccess.PlacementGhost(player);
            if (ghost) ghost.SetActive(false);
            var marker = GameAccess.PlacementMarker(player);
            if (marker) marker.SetActive(false);

            Evaluate(player, flashWard: false);
            SyncGhosts(ghost);
            StretchHud.Show(_hudCost, FillModeLabel(Plugin.Fill.Value), _hudError);
        }

        private static void BuildPath()
        {
            _pathScratch.Clear();
            _pathScratch.AddRange(_corners);
            _pathScratch.Add(Snap(_cursor));
        }

        /// <summary>Plans the row and runs every placement check. Fills the HUD strings and per-piece validity.</summary>
        private static bool Evaluate(Player player, bool flashWard)
        {
            BuildPath();
            float angleStep = GameAccess.PlaceRotationDegrees(player);
            if (angleStep <= 0f) angleStep = 22.5f;
            bool withinLimit = PathPlanner.Plan(_pathScratch, _profile, _referenceNormal, Plugin.Fill.Value, angleStep, SampleGround,
                Plugin.Overlap.Value, Plugin.EdgeInset.Value, Plugin.GroundSink.Value,
                Plugin.MaxPieces.Value, _plan);

            _pieceOk.Clear();
            string error = null;
            bool flashed = false;
            foreach (var p in _plan)
            {
                string why = CheckPiece(p, flashWard && !flashed);
                if (why != null && why == "$msg_privatezone") flashed = true;
                _pieceOk.Add(why == null);
                if (error == null) error = why;
            }

            if (!withinLimit) error = $"Too long: max {Plugin.MaxPieces.Value} pieces";
            if (_plan.Count == 0) error = error ?? "Path too short";

            string costError = CheckCost(player, _plan.Count);
            if (error == null) error = costError;

            _hudError = error == null ? "" : Localization.instance.Localize(error);
            return error == null;
        }

        private static string CheckPiece(PlannedPiece p, bool flashWard)
        {
            if (!p.HasGround) return "No ground";
            if (Location.IsInsideNoBuildLocation(p.Position)) return "$msg_nobuildzone";
            if (!PrivateArea.CheckAccess(p.Position, 0f, flashWard, false)) return "$msg_privatezone";
            if (_prefab.m_craftingStation &&
                !CraftingStation.HaveBuildStationInRange(_prefab.m_craftingStation.m_name, p.Position))
                return "$msg_missingstation";
            return null;
        }

        private static bool CostApplies(Player player) =>
            !GameAccess.NoPlacementCost(player) &&
            !(ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(_prefab.FreeBuildKey()));

        /// <summary>Checks materials, tool durability and stamina for <paramref name="count"/> pieces. Also builds the HUD cost line.</summary>
        private static string CheckCost(Player player, int count)
        {
            var sb = new StringBuilder();
            sb.Append(Localization.instance.Localize(_prefab.m_name)).Append(" × ").Append(count);
            string error = null;

            if (CostApplies(player))
            {
                var inv = player.GetInventory();
                foreach (var req in _prefab.m_resources)
                {
                    if (!req.m_resItem) continue;
                    int need = req.GetAmount(0) * count;
                    if (need <= 0) continue;
                    string itemName = req.m_resItem.m_itemData.m_shared.m_name;
                    int have = inv.CountItems(itemName, -1, true);
                    string loc = Localization.instance.Localize(itemName);
                    sb.Append("   ").Append(need).Append(' ').Append(loc).Append(" (").Append(have).Append(')');
                    if (have < need && error == null) error = $"Not enough {loc}: need {need}, have {have}";
                }
            }

            var tool = GameAccess.GetRightItem(player);
            if (tool != null)
            {
                if (!player.HaveStamina(tool.m_shared.m_attack.m_attackStamina) && error == null)
                    error = "Not enough stamina";
                if (tool.m_shared.m_useDurability)
                {
                    float wear = GameAccess.GetPlaceDurability(player, tool) * Game.m_durabilityRate * count;
                    if (tool.m_durability < wear && error == null)
                        error = $"{Localization.instance.Localize(tool.m_shared.m_name)} would break before finishing";
                }
            }

            _hudCost = sb.ToString();
            return error;
        }

        private static bool SampleGround(Vector3 p, out float height)
        {
            if (Heightmap.GetHeight(p, out height)) return true;
            return ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(p, out height);
        }

        // ------------------------------------------------------------------ cursor & snapping

        private static bool TryGetCursor(Player player, Piece piece, out Vector3 point)
        {
            point = default;
            var cam = GameCamera.instance;
            if (!cam) return false;
            var t = cam.transform;
            if (!Physics.Raycast(t.position, t.forward, out var hit, 50f, GameAccess.PlaceRayMask(player))) return false;
            if (!hit.collider || hit.collider.attachedRigidbody) return false;
            float reach = player.m_maxPlaceDistance + (piece ? piece.m_extraPlacementDistance : 0);
            if (Vector3.Distance(player.m_eye.position, hit.point) > reach) return false;
            point = hit.point;
            return true;
        }

        /// <summary>
        /// Face direction for the new row: an existing wall the start snapped to wins, so a continued wall
        /// keeps its sides; otherwise the vanilla ghost's rotation as the player last set it.
        /// </summary>
        private static Vector3 AnchorNormal(Player player, Piece anchor)
        {
            if (anchor)
            {
                var anchorProfile = WallProfiles.Get(anchor) ?? _profile;
                var n = anchorProfile.WorldNormal(anchor.transform);
                if (n != Vector3.zero) return n;
            }
            var ghost = GameAccess.PlacementGhost(player);
            return ghost ? _profile.WorldNormal(ghost.transform) : Vector3.zero;
        }

        private static readonly List<Transform> _ghostSnaps = new List<Transform>();

        /// <summary>
        /// Detects whether vanilla has snapped the placement ghost onto another piece (auto or manual snap
        /// point): one of the ghost's snap points then sits exactly on a snap point of an existing piece.
        /// Returns that shared point and the piece it belongs to.
        /// </summary>
        private static bool TryGetGhostSnap(Player player, out Vector3 point, out Piece other)
        {
            point = default;
            other = null;
            var ghost = GameAccess.PlacementGhost(player);
            if (!ghost || !ghost.activeInHierarchy) return false;
            var ghostPiece = ghost.GetComponent<Piece>();
            if (!ghostPiece) return false;

            _ghostSnaps.Clear();
            ghostPiece.GetSnapPoints(_ghostSnaps);
            if (_ghostSnaps.Count == 0) return false;

            _snapPoints.Clear();
            _snapPieces.Clear();
            Piece.GetSnapPoints(ghost.transform.position, _profile.Width + 2f, _snapPoints, _snapPieces);

            const float tolerance = 0.05f;
            float best = tolerance;
            foreach (var gs in _ghostSnaps)
            {
                if (!gs) continue;
                foreach (var sp in _snapPoints)
                {
                    if (!sp || sp.IsChildOf(ghost.transform)) continue;
                    float d = Vector3.Distance(gs.position, sp.position);
                    if (d >= best) continue;
                    var owner = sp.GetComponentInParent<Piece>();
                    if (!owner || owner.gameObject == ghost) continue;
                    best = d;
                    point = sp.position;
                    other = owner;
                }
            }
            if (other) Plugin.Debug($"Ghost is snapped to {other.name} at {point}");
            return other;
        }

        private static Vector3 Snap(Vector3 p) => Snap(p, out _);

        /// <summary>Snaps a path point onto the nearest snap point of an existing supported wall piece.</summary>
        private static Vector3 Snap(Vector3 p, out Piece snappedTo)
        {
            snappedTo = null;
            float radius = Plugin.SnapRadius.Value;
            if (radius <= 0f) return p;
            _snapPoints.Clear();
            _snapPieces.Clear();
            Piece.GetSnapPoints(p, radius + 1f, _snapPoints, _snapPieces);

            Vector3 flat = PathPlanner.Flat(p);
            float best = radius;
            Vector3 result = p;
            foreach (var sp in _snapPoints)
            {
                if (!sp) continue;
                var owner = sp.GetComponentInParent<Piece>();
                if (!owner || !WallProfiles.IsEnabled(owner)) continue;
                float d = Vector3.Distance(flat, PathPlanner.Flat(sp.position));
                if (d < best)
                {
                    best = d;
                    result = new Vector3(sp.position.x, p.y, sp.position.z);
                    snappedTo = owner;
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ commit

        private static void Commit(Player player)
        {
            if (!Evaluate(player, flashWard: true))
            {
                player.Message(MessageHud.MessageType.Center, _hudError);
                return;
            }

            int count = _plan.Count;
            var inv = player.GetInventory();
            bool cheated = (inv.ItemCheated(_prefab.m_resources, -1, true) || player.NoCostCheat())
                           && !PlayerProfile.s_bypassCheatChecks;

            // Per piece, mirror vanilla's TryPlacePiece (log line and player stats) before PlacePiece. Only the
            // first piece swings the tool (doAttack), so a drag plays one build animation.
            var profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
            IsCommitting = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    ZLog.Log("Placed " + _prefab.gameObject.name);
                    if (Game.instance) Game.instance.IncrementPlayerStat(PlayerStatType.Builds, 1f, cheated);
                    profile?.IncrementStatBuildPiecePlaced(_prefab.m_name, 1f, cheated);
                    player.PlacePiece(_prefab, _plan[i].Position, _plan[i].Rotation, i == 0, cheated);
                }
            }
            finally { IsCommitting = false; }

            // The rest mirrors what vanilla UpdatePlacement does after a successful TryPlacePiece.
            if (Hud.instance && Hud.instance.m_buildUi) Hud.instance.m_buildUi.AddRecentPiece(_prefab);

            if (CostApplies(player))
                player.ConsumeResources(_prefab.m_resources, 0, -1, count);

            player.UseStamina(GameAccess.GetBuildStamina(player) * count);

            var table = GameAccess.BuildPieces(player);
            if (table && table.m_skill != Skills.SkillType.None)
            {
                bool crafting = table.m_skill == Skills.SkillType.Crafting;
                for (int i = 0; i < count; i++)
                {
                    if (crafting && Game.instance) Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPieces, 1f, false);
                    ref int debt = ref GameAccess.BuildRemoveDebt(player);
                    if (debt > 0) { debt--; continue; }
                    player.RaiseSkill(table.m_skill, 1f);
                    if (crafting && Game.instance) Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPiecesNoDebt, 1f, false);
                }
            }

            var tool = GameAccess.GetRightItem(player);
            if (tool != null)
            {
                if (tool.m_shared.m_useDurability)
                {
                    float wear = GameAccess.GetPlaceDurability(player, tool) * Game.m_durabilityRate * count;
                    tool.m_durability = Mathf.Max(0f, tool.m_durability - wear);
                }
                tool.m_shared.m_buildEffect.Create(player.transform.position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
            }

            GameAccess.LastToolUseTime(player) = Time.time;
            Plugin.Debug($"Placed {count} × {_profile.PrefabName} along {_corners.Count} point path ({PathPlanner.PathLength(_pathScratch):0.0}m).");
        }

        // ------------------------------------------------------------------ ghosts

        private static void SyncGhosts(GameObject template)
        {
            while (_ghosts.Count > _plan.Count)
            {
                Object.Destroy(_ghosts[_ghosts.Count - 1]);
                _ghosts.RemoveAt(_ghosts.Count - 1);
            }
            if (!template && _ghosts.Count < _plan.Count) return;

            if (_ghosts.Count < _plan.Count)
            {
                // Clone the way vanilla builds its ghost: from an active object while ZNetView init is disabled,
                // so Awake (e.g. WearNTear's) runs during Instantiate in ghost mode. Cloning the hidden ghost and
                // activating later runs Awake without that flag and throws.
                bool wasActive = template.activeSelf;
                bool prevInit = ZNetView.m_forceDisableInit;
                if (!wasActive) template.SetActive(true);
                ZNetView.m_forceDisableInit = true;
                CreatingPreview = true;
                try
                {
                    while (_ghosts.Count < _plan.Count)
                    {
                        var g = Object.Instantiate(template);
                        g.name = "BetterStakewall_Preview";
                        // A preview only needs its meshes; drop the structural component before it can Start.
                        foreach (var wnt in g.GetComponentsInChildren<WearNTear>(true))
                            Object.DestroyImmediate(wnt);
                        // The template ghost carries a MaterialManNotifier (added when vanilla tints it), which the
                        // clone copies without being registered in MaterialMan; destroying it would then log
                        // "Can't unregister renderer". A set/reset round-trip registers the clone under its own id.
                        var clonePiece = g.GetComponent<Piece>();
                        if (clonePiece)
                        {
                            clonePiece.SetInvalidPlacementHeightlight(true);
                            clonePiece.SetInvalidPlacementHeightlight(false);
                        }
                        _ghosts.Add(g);
                    }
                }
                finally
                {
                    CreatingPreview = false;
                    ZNetView.m_forceDisableInit = prevInit;
                    if (!wasActive) template.SetActive(false);
                }
            }

            bool allOk = _hudError.Length == 0;
            for (int i = 0; i < _plan.Count; i++)
            {
                var g = _ghosts[i];
                if (!g) continue;
                if (!g.activeSelf) g.SetActive(true);
                g.transform.SetPositionAndRotation(_plan[i].Position, _plan[i].Rotation);
                var piece = g.GetComponent<Piece>();
                if (piece) piece.SetInvalidPlacementHeightlight(!(allOk && _pieceOk[i]));
            }
        }

        private static void DestroyGhosts()
        {
            foreach (var g in _ghosts) if (g) Object.Destroy(g);
            _ghosts.Clear();
        }

        // ------------------------------------------------------------------ lifecycle & HUD

        private static void End()
        {
            DestroyGhosts();
            _state = State.Idle;
            _prefab = null;
            _profile = null;
            _corners.Clear();
            _plan.Clear();
            _hudCost = _hudError = "";
            StretchHud.Hide();
            var player = Player.m_localPlayer;
            if (player)
            {
                var ghost = GameAccess.PlacementGhost(player);
                if (ghost) ghost.SetActive(true);
            }
        }

        public static void Cancel()
        {
            if (_state == State.Idle) return;
            Plugin.Debug("Stretch cancelled");
            End();
        }

        private static string FillModeLabel(FillMode m) => m switch
        {
            FillMode.OverlapLast => "Overlap last piece",
            FillMode.OverlapEvenly => "Overlap evenly",
            _ => "Nearest whole piece",
        };
    }
}
