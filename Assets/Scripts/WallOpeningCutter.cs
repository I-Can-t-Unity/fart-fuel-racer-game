using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cuts holes in the track walls wherever an orange WallOpening box is.
///
/// It does not need to know what your walls are called: inside each opening
/// it removes only the parts that STICK UP above the road (walls, wall tops,
/// barriers) and leaves the flat road surface alone. Works for one big wall
/// mesh, many wall pieces, or invisible box-collider walls.
///
/// Runs when you press Play, one frame after start, so the track generator
/// has already rebuilt the track. Nothing in the saved scene is changed.
/// </summary>
public class WallOpeningCutter : MonoBehaviour
{
    [Tooltip("Search the whole scene for walls (recommended). If off, only searches under Walls Root.")]
    public bool scanWholeScene = true;
    public Transform wallsRoot;

    [Tooltip("Optional. Only cut objects/meshes whose name contains this. Leave EMPTY normally.")]
    public string onlyNamesContaining = "";

    [Tooltip("Anything higher than this above the road counts as wall and is removed inside an opening.")]
    public float wallMinHeight = 0.3f;

    [Tooltip("Road surface height (LoopTrackGenerator → Surface Y).")]
    public float roadSurfaceY = 0.12f;

    public bool cutOnPlay = true;

    IEnumerator Start()
    {
        if (!cutOnPlay) yield break;
        yield return null;   // let the generator finish
        yield return null;
        Cut();
    }

    [ContextMenu("Cut Wall Openings Now (changes the scene)")]
    void CutFromMenu() => Cut();

    public int Cut()
    {
        WallOpening.RefreshFromScene();
        int changed = 0;
        var done = new Dictionary<Mesh, Mesh>();

        foreach (var mf in FindAll<MeshFilter>())
        {
            if (!Allowed(mf.gameObject) || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
            Mesh src = mf.sharedMesh;
            Mesh cut = GetCut(src, mf.transform, done, out int removed);
            if (removed == 0) continue;
            mf.sharedMesh = cut;
            changed++;
            Debug.Log($"WallOpeningCutter: opened {removed} wall triangles in '{mf.gameObject.name}'.", mf);
        }

        foreach (var mc in FindAll<MeshCollider>())
        {
            if (!Allowed(mc.gameObject) || !mc.sharedMesh || !mc.sharedMesh.isReadable) continue;
            Mesh src = mc.sharedMesh;
            Mesh cut = GetCut(src, mc.transform, done, out int removed);
            if (cut == src) continue;
            mc.sharedMesh = null;
            mc.sharedMesh = cut;
            changed++;
        }

        foreach (var col in FindAll<Collider>())
        {
            if (col is MeshCollider || col.isTrigger || !col.enabled || !Allowed(col.gameObject)) continue;
            Bounds b = col.bounds;
            if (b.max.y < roadSurfaceY + wallMinHeight) continue;          // flat, not a wall
            if (WallOpening.OverlapsAny(b)) { col.enabled = false; changed++; }
        }

        if (changed == 0)
            Debug.LogWarning("WallOpeningCutter: nothing was cut. Check that the orange WallOpening boxes " +
                             "(Shortcut_BrokenBridge → _Generated) cover the wall where the shortcut leaves/joins.", this);
        else
            Debug.Log($"WallOpeningCutter: done, {changed} object(s) opened.", this);
        return changed;
    }

    IEnumerable<T> FindAll<T>() where T : Component
    {
        if (!scanWholeScene && wallsRoot) return wallsRoot.GetComponentsInChildren<T>(true);
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }

    bool Allowed(GameObject go)
    {
        if (go.GetComponentInParent<PreserveOnRegenerate>() != null) return false; // the shortcut itself
        if (go.GetComponentInParent<Rigidbody>() != null) return false;            // cars, props
        foreach (var c in go.GetComponents<Collider>()) if (c.isTrigger) return false; // pickups, gates
        if (go.GetComponent<WallOpening>() != null) return false;
        if (!string.IsNullOrEmpty(onlyNamesContaining) &&
            go.name.IndexOf(onlyNamesContaining, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    Mesh GetCut(Mesh src, Transform t, Dictionary<Mesh, Mesh> done, out int removed)
    {
        removed = 0;
        if (done.TryGetValue(src, out Mesh cached)) { removed = cached == src ? 0 : 1; return cached; }

        var verts = src.vertices;
        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) world[i] = t.TransformPoint(verts[i]);

        float wallY = roadSurfaceY + wallMinHeight;
        var newTris = new List<int>[src.subMeshCount];
        for (int s = 0; s < src.subMeshCount; s++)
        {
            int[] tris = src.GetTriangles(s);
            var keep = new List<int>(tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = world[tris[i]], b = world[tris[i + 1]], c = world[tris[i + 2]];
                bool high = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) > wallY;   // sticks up = wall
                bool inside = WallOpening.IsInsideAny((a + b + c) / 3f)
                              || WallOpening.IsInsideAny(a) || WallOpening.IsInsideAny(b) || WallOpening.IsInsideAny(c);
                if (high && inside) { removed++; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            newTris[s] = keep;
        }

        if (removed == 0) { done[src] = src; return src; }

        Mesh dst = Object.Instantiate(src);
        dst.name = src.name + " (opened)";
        for (int s = 0; s < newTris.Length; s++) dst.SetTriangles(newTris[s], s);
        dst.RecalculateBounds();
        done[src] = dst;
        return dst;
    }

    /// <summary>Optional: call at the end of the track generator's Generate().</summary>
    public static void CutAll()
    {
#if UNITY_2023_1_OR_NEWER
        var cutters = Object.FindObjectsByType<WallOpeningCutter>(FindObjectsSortMode.None);
#else
        var cutters = Object.FindObjectsOfType<WallOpeningCutter>();
#endif
        foreach (var c in cutters) c.Cut();
    }
}
