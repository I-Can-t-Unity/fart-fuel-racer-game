using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A box-shaped "no wall here" volume. Place one where the shortcut leaves the
/// main track and one where it rejoins. Because it lives outside the generated
/// track, it survives regeneration, and the generator (or WallOpeningCutter)
/// re-opens the gap every time the track is rebuilt.
/// </summary>
[ExecuteAlways]
public class WallOpening : MonoBehaviour
{
    [Tooltip("Size in metres. Z = length along the track, X = across, Y = height.")]
    public Vector3 size = new Vector3(6f, 10f, 24f);

    static readonly List<WallOpening> active = new List<WallOpening>();

    void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    void OnDisable() { active.Remove(this); }

    public bool Contains(Vector3 world)
    {
        Vector3 local = Quaternion.Inverse(transform.rotation) * (world - transform.position);
        Vector3 h = size * 0.5f;
        return Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.y) <= h.y && Mathf.Abs(local.z) <= h.z;
    }

    /// <summary>
    /// Call from the generator for each wall sample/segment position:
    ///   if (WallOpening.IsInsideAny(pos)) continue;   // skip this wall piece
    /// </summary>
    public static bool IsInsideAny(Vector3 world)
    {
        if (active.Count == 0) RefreshFromScene(); // generator may run before OnEnable in edit mode
        for (int i = 0; i < active.Count; i++)
            if (active[i] && active[i].isActiveAndEnabled && active[i].Contains(world)) return true;
        return false;
    }

    public static void RefreshFromScene()
    {
        active.Clear();
#if UNITY_2023_1_OR_NEWER
        active.AddRange(Object.FindObjectsByType<WallOpening>(FindObjectsSortMode.None));
#else
        active.AddRange(Object.FindObjectsOfType<WallOpening>());
#endif
    }

    /// <summary>True if any part of these bounds overlaps any opening
    /// (samples along the longest axis so long wall pieces are caught).</summary>
    public static bool OverlapsAny(Bounds b)
    {
        Vector3 e = b.extents;
        Vector3 axis = e.x >= e.z ? new Vector3(e.x, 0, 0) : new Vector3(0, 0, e.z);
        for (int i = 0; i <= 8; i++)
        {
            Vector3 p = b.center + axis * Mathf.Lerp(-1f, 1f, i / 8f);
            if (IsInsideAny(p)) return true;
        }
        return false;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
