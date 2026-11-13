using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBetterStakewall
{
    /// <summary>How a straight leg is filled when its length isn't a whole number of pieces.</summary>
    public enum FillMode
    {
        /// <summary>Vanilla spacing; the leg ends at the nearest whole piece to the target point.</summary>
        NearestWhole,
        /// <summary>Vanilla spacing; the last piece overlaps its neighbour so the leg ends exactly on the target.</summary>
        OverlapLast,
        /// <summary>All pieces overlap equally so the leg ends exactly on the target.</summary>
        OverlapEvenly,
    }

    internal struct PlannedPiece
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 EdgeA;
        public Vector3 EdgeB;
        public bool HasGround;
    }

    /// <summary>
    /// Turns a multi-point path into a list of grounded wall pieces. Has no game state,
    /// so the maths can be reasoned about (and tested) on its own.
    /// </summary>
    internal static class PathPlanner
    {
        public delegate bool GroundSampler(Vector3 worldXZ, out float height);

        /// <param name="points">Path vertices in order. Only X/Z are used.</param>
        /// <param name="referenceNormal">World face direction of the anchor piece. Decides which side of the path the
        /// wall's face is on; that side is kept around corners. Zero falls back to the left of travel.</param>
        /// <returns>False if the path needs more than <paramref name="maxPieces"/> pieces (the list then holds the first maxPieces).</returns>
        public static bool Plan(
            IList<Vector3> points,
            WallProfile profile,
            Vector3 referenceNormal,
            FillMode fillMode,
            float angleStep,
            GroundSampler ground,
            float overlap,
            float edgeInset,
            float groundSink,
            int maxPieces,
            List<PlannedPiece> result)
        {
            result.Clear();
            float width = profile.Width;
            // Vanilla snapping puts neighbours exactly one width apart; overlap (default 0) tightens that.
            float step = width * (1f - Mathf.Clamp(overlap, 0f, 0.9f));
            float halfSample = Mathf.Max(0f, width * 0.5f - edgeInset);
            float side = FaceSide(points, PathPlanner.Flat(referenceNormal), angleStep);

            // Each leg starts where the previous one actually ended, so pieces stay edge to edge at corners.
            Vector3 a = Flat(points[0]);
            for (int leg = 0; leg + 1 < points.Count; leg++)
            {
                Vector3 b = Flat(points[leg + 1]);
                Vector3 delta = b - a;
                if (delta.magnitude < 0.01f) continue;
                // Keep vanilla's rotation steps: snap the leg direction to the nearest angleStep (world yaw),
                // then measure the leg along that snapped line.
                Vector3 dir = SnapDirection(delta, angleStep);
                float len = Vector3.Dot(delta, dir);
                if (len < 0.01f) continue;

                // A leg shorter than one piece still gets one whole piece.
                float fillLen = Mathf.Max(len, step);
                int count;
                float legEnd;
                switch (fillMode)
                {
                    case FillMode.OverlapLast:
                    case FillMode.OverlapEvenly:
                        // Enough pieces to reach the target point exactly.
                        count = Mathf.Max(1, Mathf.CeilToInt(fillLen / step - 1e-3f));
                        legEnd = fillLen;
                        break;
                    default:
                        // Fixed spacing, whole pieces: ends within half a piece of the target point.
                        count = Mathf.Max(1, Mathf.RoundToInt(len / step));
                        legEnd = step * count;
                        break;
                }
                // Even mode: first piece's near edge stays on the leg start, last piece's far edge on the leg end,
                // and the centres in between are spread evenly (each piece is still a full width).
                float evenStep = count > 1 ? (fillLen - step) / (count - 1) : 0f;
                Quaternion rot = profile.RotationForNormal(LeftOf(dir) * side);

                for (int i = 0; i < count; i++)
                {
                    if (result.Count >= maxPieces) return false;

                    float along;
                    switch (fillMode)
                    {
                        case FillMode.OverlapEvenly:
                            along = step * 0.5f + evenStep * i;
                            break;
                        case FillMode.OverlapLast:
                            // Vanilla spacing, except the last piece is pulled back so it ends on the target.
                            along = i == count - 1 ? fillLen - step * 0.5f : step * (i + 0.5f);
                            break;
                        default:
                            along = step * (i + 0.5f);
                            break;
                    }
                    Vector3 center = a + dir * along;
                    var piece = new PlannedPiece
                    {
                        Rotation = rot,
                        EdgeA = center - dir * halfSample,
                        EdgeB = center + dir * halfSample,
                    };

                    bool okA = ground(piece.EdgeA, out float hA);
                    bool okB = ground(piece.EdgeB, out float hB);
                    piece.HasGround = okA && okB;

                    // Edge grounding: the lower edge sits on the ground (no floating) and
                    // the higher edge is partly buried.
                    float baseY = piece.HasGround ? Mathf.Min(hA, hB) - groundSink : center.y;
                    piece.EdgeA.y = okA ? hA : baseY;
                    piece.EdgeB.y = okB ? hB : baseY;
                    piece.Position = new Vector3(center.x, baseY - profile.BottomOffset, center.z);
                    result.Add(piece);
                }
                a += dir * legEnd;
            }
            return true;
        }

        /// <summary>Flattened, normalised direction rounded to the nearest multiple of <paramref name="stepDegrees"/> of world yaw (0 or less = no snapping).</summary>
        public static Vector3 SnapDirection(Vector3 delta, float stepDegrees)
        {
            delta.y = 0f;
            if (stepDegrees <= 0f) return delta.normalized;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            yaw = Mathf.Round(yaw / stepDegrees) * stepDegrees;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        /// <summary>Horizontal direction 90° to the left of travel direction <paramref name="dir"/>.</summary>
        public static Vector3 LeftOf(Vector3 dir) => Vector3.Cross(dir, Vector3.up);

        /// <summary>+1 if the reference face points left of the first leg's travel direction, -1 if right.</summary>
        private static float FaceSide(IList<Vector3> points, Vector3 referenceNormal, float angleStep)
        {
            if (referenceNormal.sqrMagnitude < 1e-6f) return 1f;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 d = Flat(points[i + 1]) - Flat(points[i]);
                if (d.sqrMagnitude < 1e-4f) continue;
                float dot = Vector3.Dot(referenceNormal.normalized, LeftOf(SnapDirection(d, angleStep)));
                // Dragging nearly along the face direction is ambiguous; prefer left so it stays stable.
                return dot >= -0.05f ? 1f : -1f;
            }
            return 1f;
        }

        public static float PathLength(IList<Vector3> points)
        {
            float total = 0f;
            for (int i = 0; i + 1 < points.Count; i++) total += Vector3.Distance(Flat(points[i]), Flat(points[i + 1]));
            return total;
        }

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
