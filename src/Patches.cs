using HarmonyLib;
using UnityEngine;

namespace ValheimBetterStakewall
{
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class Player_UpdatePlacement_Patch
    {
        // Runs in Player.Update. Returning false skips vanilla placement input for this frame.
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Player __instance, bool takeInput)
        {
            if (__instance != Player.m_localPlayer) return true;
            return StretchController.OnUpdatePlacement(__instance, takeInput);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class Player_UpdatePlacementGhost_Patch
    {
        // While dragging, skip the vanilla ghost update: the ghost is replaced by the stretch preview, and this
        // is where vanilla reads Q/E (TabLeft/TabRight) to cycle snap points, which the drag uses for fill mode.
        // Harmony still runs the postfix below when this returns false.
        private static bool Prefix(Player __instance)
        {
            return __instance != Player.m_localPlayer || !StretchController.IsActive;
        }

        // Runs in Player.LateUpdate after the vanilla ghost has moved: refresh the stretch preview.
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            StretchController.OnLateUpdate(__instance);
        }
    }

    /// <summary>
    /// Debug aid: with DebugLogging on, logs each vanilla-placed supported wall piece and its offset from the
    /// previous one, to compare vanilla snap spacing with the measured profile width.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class Player_PlacePiece_DebugPatch
    {
        private static bool _havePrev;
        private static Vector3 _prevPos;
        private static Quaternion _prevRot;
        private static string _prevName;

        private static void Postfix(Player __instance, Piece piece, Vector3 pos, Quaternion rot)
        {
            if (!Plugin.DebugLogging.Value || __instance != Player.m_localPlayer) return;
            if (StretchController.IsCommitting || !WallProfiles.IsEnabled(piece)) return;

            string name = Utils.GetPrefabName(piece.gameObject);
            var profile = WallProfiles.Get(piece);
            string msg = $"Vanilla placed {name} at {pos.ToString("F3")} yaw {rot.eulerAngles.y:0.0}";

            if (_havePrev && _prevName == name)
            {
                Vector3 d = pos - _prevPos;
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                Vector3 widthAxis = _prevRot * (profile != null && !profile.WidthAlongLocalX ? Vector3.forward : Vector3.right);
                Vector3 faceAxis = _prevRot * (profile != null ? profile.LocalNormal : Vector3.forward);
                msg += $" | from previous: horizontal {flat.magnitude:0.000}m, along wall {Vector3.Dot(d, widthAxis):0.000}m, " +
                       $"across {Vector3.Dot(d, faceAxis):0.000}m, vertical {d.y:0.000}m, yaw change {Quaternion.Angle(_prevRot, rot):0.0}°" +
                       $" | profile width {(profile != null ? profile.Width : 0f):0.000}m";
            }
            Plugin.Log.LogInfo(msg);

            _havePrev = true;
            _prevPos = pos;
            _prevRot = rot;
            _prevName = name;
        }
    }

    /// <summary>
    /// Preview pieces are cloned from the vanilla placement ghost, which has no ZNetView, so WearNTear.Awake
    /// would throw. Skip it only while the mod is creating previews. WearNTear.OnDestroy is a no-op while
    /// m_myIndex is still -1, so the un-awoken component can then be removed safely.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "Awake")]
    internal static class WearNTear_Awake_Patch
    {
        private static bool Prefix() => !StretchController.CreatingPreview;
    }

    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class Player_OnDestroy_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) StretchController.Cancel();
        }
    }
}
