using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimBetterStakewall
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.valheimbetterstakewall";
        public const string Name = "ValheimBetterStakewall";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> EnabledPieces;
        internal static ConfigEntry<string> ProfileOverrides;

        // Stretch behaviour
        internal static ConfigEntry<float> DragThreshold;
        internal static ConfigEntry<int> MaxPieces;
        internal static ConfigEntry<float> Overlap;
        internal static ConfigEntry<FillMode> Fill;        internal static ConfigEntry<float> SnapRadius;

        // Grounding
        internal static ConfigEntry<float> EdgeInset;
        internal static ConfigEntry<float> GroundSink;

        // Controls
        internal static ConfigEntry<KeyCode> UndoCornerKey;

        // Debug
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Turns click-and-drag wall stretching on or off.");
            EnabledPieces = Config.Bind("General", "EnabledPieces", "stake_wall, piece_dvergr_stake_wall",
                "Comma-separated pieces that support click-and-drag stretching. Accepts prefab names (stake_wall) " +
                "or piece name tokens (piece_stakewall). With DebugLogging on, pressing place logs the selected piece's names.");
            ProfileOverrides = Config.Bind("General", "ProfileOverrides", "",
                "Optional manual geometry for pieces whose auto-measurement is wrong. " +
                "Format: prefab:width:bottomOffset, separated by ';'. Example: stake_wall:2:0");

            DragThreshold = Config.Bind("Stretch", "DragThreshold", 1.0f,
                "Metres the cursor must move from the press point before a drag becomes a stretch. Shorter presses place a single piece the vanilla way.");
            MaxPieces = Config.Bind("Stretch", "MaxPieces", 64,
                new ConfigDescription("Maximum pieces placed in one stretch.", new AcceptableValueRange<int>(1, 512)));
            Fill = Config.Bind("Stretch", "FillMode", FillMode.NearestWhole,
                "How each straight section is filled. NearestWhole: vanilla spacing, ends at the nearest whole piece. " +
                "OverlapLast: vanilla spacing, the last piece overlaps to end exactly at the cursor. " +
                "OverlapEvenly: all pieces overlap equally to end exactly at the cursor. Can also be cycled while dragging.");
            Overlap = Config.Bind("Stretch", "Overlap", 0f,
                new ConfigDescription("Fraction of a piece width that neighbours overlap. 0 = vanilla snap spacing (pieces exactly one width apart).",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            SnapRadius = Config.Bind("Stretch", "SnapRadius", 0.6f,
                "Path points within this distance of a snap point on an existing wall of a supported type snap to it.");

            EdgeInset = Config.Bind("Grounding", "EdgeInset", 0.1f,
                "Distance inward from each piece edge where ground height is sampled (accounts for the round log edges).");
            GroundSink = Config.Bind("Grounding", "GroundSink", 0.05f,
                "Extra distance the low edge is pushed into the ground so the piece counts as grounded for structural support.");

            UndoCornerKey = Config.Bind("Controls", "UndoCornerKey", KeyCode.Backspace,
                "While dragging: removes the last corner. With no corners, cancels the drag.");
            DebugLogging = Config.Bind("Debug", "DebugLogging", false, "Logs stretch planning details.");

            EnabledPieces.SettingChanged += (_, __) => WallProfiles.Clear();
            ProfileOverrides.SettingChanged += (_, __) => WallProfiles.Clear();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded.");
        }

        private void OnDestroy()
        {
            StretchController.Cancel();
            StretchHud.Destroy();
            _harmony?.UnpatchSelf();
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
