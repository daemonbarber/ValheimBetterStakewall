using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimBetterStakewall
{
    /// <summary>
    /// Geometry needed to tile a wall piece along a path. Measured from the prefab
    /// at runtime, so new wall types only need adding to the EnabledPieces config
    /// (plus a ProfileOverrides entry if auto-measurement gets them wrong).
    /// </summary>
    internal sealed class WallProfile
    {
        public string PrefabName;

        /// <summary>Tiling width along the wall direction, in metres.</summary>
        public float Width;

        /// <summary>True if the piece's width runs along its local X axis; false for local Z.</summary>
        public bool WidthAlongLocalX = true;

        /// <summary>Local Y of the lowest point of the mesh, relative to the pivot.</summary>
        public float BottomOffset;

        public string Source;

        /// <summary>The piece's face direction in local space: the horizontal axis that is not the width axis.</summary>
        public Vector3 LocalNormal => WidthAlongLocalX ? Vector3.forward : Vector3.right;

        /// <summary>World-space face direction of a placed piece (or ghost) with this profile, flattened.</summary>
        public Vector3 WorldNormal(Transform t)
        {
            var n = t.rotation * LocalNormal;
            n.y = 0f;
            return n.sqrMagnitude < 1e-6f ? Vector3.zero : n.normalized;
        }

        /// <summary>World rotation whose face direction (<see cref="LocalNormal"/>) is <paramref name="normal"/>; the width axis then lies along the wall.</summary>
        public Quaternion RotationForNormal(Vector3 normal)
        {
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.forward;
            normal.Normalize();
            // LookRotation maps local +Z to its forward argument and local +X to up x forward.
            // Face on local Z: forward = normal. Face on local X: we need up x forward = normal,
            // so forward = normal x up.
            return WidthAlongLocalX
                ? Quaternion.LookRotation(normal, Vector3.up)
                : Quaternion.LookRotation(Vector3.Cross(normal, Vector3.up), Vector3.up);
        }

        public override string ToString() =>
            $"{PrefabName}: width={Width:0.###} axis={(WidthAlongLocalX ? "X" : "Z")} bottom={BottomOffset:0.###} ({Source})";
    }

    internal static class WallProfiles
    {
        private static readonly Dictionary<string, WallProfile> Cache = new Dictionary<string, WallProfile>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _enabled;
        private static Dictionary<string, (float width, float bottom)> _overrides;

        public static void Clear()
        {
            Cache.Clear();
            _enabled = null;
            _overrides = null;
        }

        public static bool IsEnabled(string prefabName)
        {
            if (_enabled == null)
            {
                _enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in Plugin.EnabledPieces.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    _enabled.Add(s.Trim());
            }
            return _enabled.Contains(prefabName);
        }

        /// <summary>A piece matches by prefab name (stake_wall) or by its name token, with or without '$' (piece_stakewall).</summary>
        public static bool IsEnabled(Piece piece)
        {
            if (!piece) return false;
            if (IsEnabled(Utils.GetPrefabName(piece.gameObject))) return true;
            string token = piece.m_name ?? "";
            return token.Length > 0 && (IsEnabled(token) || IsEnabled(token.TrimStart('$')));
        }

        /// <summary>Returns the profile for a piece prefab, or null if the piece is not enabled for stretching.</summary>
        public static WallProfile Get(Piece prefabPiece)
        {
            if (!prefabPiece) return null;
            string name = Utils.GetPrefabName(prefabPiece.gameObject);
            if (!IsEnabled(prefabPiece)) return null;
            if (Cache.TryGetValue(name, out var cached)) return cached;

            var profile = Measure(prefabPiece, name);
            ApplyOverride(profile);
            if (profile.Width < 0.1f)
            {
                Plugin.Log.LogWarning($"Could not measure width of {name}; add a ProfileOverrides entry. Using 2m.");
                profile.Width = 2f;
            }
            Cache[name] = profile;
            Plugin.Log.LogInfo("Wall profile " + profile);
            return profile;
        }

        private static WallProfile Measure(Piece piece, string name)
        {
            var root = piece.transform;
            var profile = new WallProfile { PrefabName = name };

            // Mesh bounds in the piece's local space give the bottom offset and a fallback width.
            bool haveBounds = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var mf in piece.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (!mesh) continue;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    haveBounds = true;
                }
            }
            if (haveBounds) profile.BottomOffset = min.y;

            // Snap points define how wall pieces tile, so they are the best width source.
            var snaps = new List<Transform>();
            piece.GetSnapPoints(snaps);
            if (snaps.Count >= 2)
            {
                Vector3 sMin = Vector3.positiveInfinity, sMax = Vector3.negativeInfinity;
                foreach (var t in snaps)
                {
                    var local = root.InverseTransformPoint(t.position);
                    sMin = Vector3.Min(sMin, local);
                    sMax = Vector3.Max(sMax, local);
                }
                float ex = sMax.x - sMin.x, ez = sMax.z - sMin.z;
                if (Mathf.Max(ex, ez) > 0.1f)
                {
                    profile.WidthAlongLocalX = ex >= ez;
                    profile.Width = Mathf.Max(ex, ez);
                    profile.Source = $"{snaps.Count} snap points";
                    return profile;
                }
            }

            if (haveBounds)
            {
                float ex = max.x - min.x, ez = max.z - min.z;
                profile.WidthAlongLocalX = ex >= ez;
                profile.Width = Mathf.Max(ex, ez);
                profile.Source = "mesh bounds";
            }
            else
            {
                profile.Source = "none";
            }
            return profile;
        }

        private static void ApplyOverride(WallProfile profile)
        {
            if (_overrides == null)
            {
                _overrides = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in Plugin.ProfileOverrides.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = entry.Split(':');
                    if (parts.Length < 2) continue;
                    if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w)) continue;
                    float bottom = float.NaN;
                    if (parts.Length >= 3) float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bottom);
                    _overrides[parts[0].Trim()] = (w, bottom);
                }
            }
            if (_overrides.TryGetValue(profile.PrefabName, out var o))
            {
                profile.Width = o.width;
                if (!float.IsNaN(o.bottom)) profile.BottomOffset = o.bottom;
                profile.Source += " + config override";
            }
        }
    }
}
