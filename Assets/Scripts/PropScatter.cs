using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Places scenery prefabs (trees, rocks, tyres...) for you, as normal prefab copies you can
/// move or delete afterwards. Put it on an empty object, pick a Mode, drag in some prefabs,
/// then click the ⋮ menu → "Scatter". Not happy? Change Seed and Scatter again.
///
///   Grove      a natural-looking clump around this object (dense in the middle, thinner
///              at the edges). Move the object to move the clump.
///   Track Edge a row along the road, just outside the walls, between two points of the lap
///              (0 = start line, 0.5 = halfway, 1 = back at the start).
///
/// Nothing is ever placed on the road: anything too close to it is skipped.
/// </summary>
public class PropScatter : MonoBehaviour
{
    public enum Mode { Grove, TrackEdge }
    public enum Side { Left, Right, Both }

    public Mode mode = Mode.Grove;
    [Tooltip("What to place. A prefab listed twice is picked twice as often.")]
    public GameObject[] prefabs;
    [Tooltip("Your LoopTrack (found automatically if left empty).")]
    public LoopTrackGenerator track;
    [Tooltip("Change this for a different random arrangement.")]
    public int seed = 1;

    [Header("Look")]
    public Vector2 sizeRange = new Vector2(0.8f, 1.3f);
    [Tooltip("Grove: random turn. Track Edge: off = each prop faces the road (good for grandstands).")]
    public bool randomRotation = true;
    [Tooltip("Keep props at least this far apart, so they don't grow into each other.")]
    public float minSpacing = 4f;
    [Tooltip("Track Edge only: line props up evenly, all the same size, all facing the road. Use for tyre walls and grandstands.")]
    public bool neatRow = false;
    [Tooltip("Grove only: no trees this close to the start line, so the start area stays open for grandstands.")]
    public float keepClearOfStart = 70f;

    [Header("Grove")]
    public int count = 12;
    public float radius = 20f;

    [Header("Track Edge")]
    [Range(0f, 1f)] public float from = 0f;
    [Range(0f, 1f)] public float to = 0.2f;
    public Side side = Side.Both;
    [Tooltip("Metres between props along the road.")]
    public float spacing = 10f;
    [Tooltip("Gap between the wall and the row.")]
    public float distanceFromWall = 4f;
    [Tooltip("Extra random depth, so the row isn't a perfect line.")]
    public float depth = 4f;

    [Header("Road safety")]
    [Tooltip("Never closer than this to the outside of a wall.")]
    public float roadClearance = 3f;

    void Reset() { track = FindFirstObjectByType<LoopTrackGenerator>(); }

    float WallOuter => track == null ? 0f
        : track.roadWidth * .5f + track.curbWidth + track.wallGap + track.wallThickness;

    List<Vector3> Center()
    {
        var pts = new List<Vector3>();
        if (track == null || track.centerline == null) return pts;
        foreach (var c in track.centerline) pts.Add(track.transform.TransformPoint(c));
        return pts;
    }

    // True if p (on the ground) is clear of every part of the road.
    static bool Clear(Vector3 p, List<Vector3> center, float limit)
    {
        float lim2 = limit * limit;
        foreach (var c in center)
        {
            float dx = c.x - p.x, dz = c.z - p.z;
            if (dx * dx + dz * dz < lim2) return false;
        }
        return true;
    }

    static bool Spaced(Vector3 p, List<Vector3> placed, float min)
    {
        foreach (var q in placed)
            if ((new Vector2(p.x - q.x, p.z - q.z)).sqrMagnitude < min * min) return false;
        return true;
    }

#if UNITY_EDITOR
    [ContextMenu("Scatter")]
    void Scatter()
    {
        if (prefabs == null || prefabs.Length == 0) { Debug.LogError("Drag at least one prefab into Prefabs.", this); return; }
        if (track == null) track = FindFirstObjectByType<LoopTrackGenerator>();
        var center = Center();
        if (center.Count == 0) { Debug.LogError("No track found. Click LoopTrack → ⋮ → Regenerate Track first.", this); return; }

        ClearProps();
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        float limit = WallOuter + roadClearance;
        float groundY = transform.position.y;
        var placed = new List<Vector3>();

        if (mode == Mode.Grove)
        {
            int tries = count * 30;
            for (int t = 0; t < tries && placed.Count < count; t++)
            {
                // Dense in the middle, thinner at the edge.
                float a = R() * Mathf.PI * 2f, d = radius * Mathf.Pow(R(), .7f);
                Vector3 p = new Vector3(transform.position.x + Mathf.Cos(a) * d, groundY, transform.position.z + Mathf.Sin(a) * d);
                if (!Clear(p, center, limit) || !Spaced(p, placed, minSpacing)) continue;
                Vector3 st = center[Mathf.Clamp(track.startIndex, 0, center.Count - 1)];
                if ((new Vector2(p.x - st.x, p.z - st.z)).magnitude < keepClearOfStart) continue;
                placed.Add(p);
                Place(Pick(rng), p, Quaternion.Euler(0, R() * 360f, 0), Mathf.Lerp(sizeRange.x, sizeRange.y, R()));
            }
            Debug.Log($"Grove: placed {placed.Count} of {count}.", this);
            return;
        }

        // Track Edge
        int n = center.Count;
        int i0 = Mathf.RoundToInt(from * n), i1 = Mathf.RoundToInt(to * n);
        if (i1 < i0) i1 += n;
        float travelled = 0f, next = 0f;
        for (int k = i0; k <= i1; k++)
        {
            Vector3 c = center[k % n], c1 = center[(k + 1) % n];
            Vector3 fwd = c1 - c; fwd.y = 0;
            float step = fwd.magnitude;
            if (travelled >= next)
            {
                next = travelled + spacing;
                Vector3 f = fwd.normalized, s = new Vector3(f.z, 0, -f.x); // s = right of the road
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (side == Side.Left && sgn > 0) continue;
                    if (side == Side.Right && sgn < 0) continue;
                    float off = WallOuter + distanceFromWall + (neatRow ? 0f : R() * depth);
                    Vector3 p = c + s * (sgn * off) + (neatRow ? Vector3.zero : f * ((R() - .5f) * spacing * .5f));
                    p.y = groundY;
                    if (!Clear(p, center, limit) || !Spaced(p, placed, minSpacing)) continue;
                    placed.Add(p);
                    Quaternion rot = randomRotation && !neatRow
                        ? Quaternion.Euler(0, R() * 360f, 0)
                        : Quaternion.LookRotation(-s * sgn, Vector3.up);   // face the road
                    Place(Pick(rng), p, rot, neatRow ? 1f : Mathf.Lerp(sizeRange.x, sizeRange.y, R()));
                }
            }
            travelled += step;
        }
        Debug.Log($"Track Edge: placed {placed.Count} props.", this);
    }

    GameObject Pick(System.Random rng) => prefabs[rng.Next(prefabs.Length)];

    void Place(GameObject prefab, Vector3 pos, Quaternion rot, float size)
    {
        if (prefab == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
        Undo.RegisterCreatedObjectUndo(go, "Scatter props");
        go.transform.SetPositionAndRotation(pos + Vector3.up * prefab.transform.position.y, rot * prefab.transform.rotation);
        go.transform.localScale = prefab.transform.localScale * size;
    }

    [ContextMenu("Clear")]
    void ClearProps()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);
    }

    // Shows the area in the Scene view: a circle for a grove, a line along the road for an edge.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.3f, 1f, .3f, .9f);
        if (mode == Mode.Grove)
        {
            Handles.color = Gizmos.color;
            Handles.DrawWireDisc(transform.position, Vector3.up, radius);
            return;
        }
        var center = Center();
        int n = center.Count;
        if (n == 0) return;
        int i0 = Mathf.RoundToInt(from * n), i1 = Mathf.RoundToInt(to * n);
        if (i1 < i0) i1 += n;
        for (int k = i0; k < i1; k++)
            Gizmos.DrawLine(center[k % n] + Vector3.up * 3, center[(k + 1) % n] + Vector3.up * 3);
        Gizmos.DrawSphere(center[i0 % n] + Vector3.up * 3, 2f);
    }
#endif
}
