using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimBetterStakewall
{
    /// <summary>
    /// Typed access to the non-public Player members the mod needs (checked against Valheim 1.0.21).
    /// If a game update renames one, Harmony throws on first use, naming the missing member.
    /// </summary>
    internal static class GameAccess
    {
        public static readonly AccessTools.FieldRef<Player, int> PlaceRayMask =
            AccessTools.FieldRefAccess<Player, int>("m_placeRayMask");
        public static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        public static readonly AccessTools.FieldRef<Player, GameObject> PlacementMarker =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementMarkerInstance");
        public static readonly AccessTools.FieldRef<Player, float> PlacePressedTime =
            AccessTools.FieldRefAccess<Player, float>("m_placePressedTime");
        public static readonly AccessTools.FieldRef<Player, float> LastToolUseTime =
            AccessTools.FieldRefAccess<Player, float>("m_lastToolUseTime");
        public static readonly AccessTools.FieldRef<Player, bool> BlockRemove =
            AccessTools.FieldRefAccess<Player, bool>("m_blockRemove");
        public static readonly AccessTools.FieldRef<Player, bool> NoPlacementCost =
            AccessTools.FieldRefAccess<Player, bool>("m_noPlacementCost");
        public static readonly AccessTools.FieldRef<Player, PieceTable> BuildPieces =
            AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");
        public static readonly AccessTools.FieldRef<Player, float> PlaceRotationDegrees =
            AccessTools.FieldRefAccess<Player, float>("m_placeRotationDegrees");
        public static readonly AccessTools.FieldRef<Player, int> BuildRemoveDebt =
            AccessTools.FieldRefAccess<Player, int>("m_buildRemoveDebt");

        public static readonly Func<Player, float> GetBuildStamina =
            AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));
        public static readonly Func<Player, ItemDrop.ItemData, float> GetPlaceDurability =
            AccessTools.MethodDelegate<Func<Player, ItemDrop.ItemData, float>>(AccessTools.Method(typeof(Player), "GetPlaceDurability"));
        public static readonly Func<Humanoid, ItemDrop.ItemData> GetRightItem =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(AccessTools.Method(typeof(Humanoid), "GetRightItem"));
    }
}
