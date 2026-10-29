using System.Collections.Generic;
using UnityEngine;

public enum BranchWaypointRole { Approach, RampBase, Launch, Airborne, Landing, Rejoin }

[System.Serializable]
public class BranchWaypoint
{
    public Transform point;
    public BranchWaypointRole role;
    [Tooltip("AI target speed here in m/s. 0 = use the AI's normal speed.")]
    public float targetSpeed;
}

/// <summary>
/// Describes one shortcut: where it splits from / merges into the main racing
/// line, its own AI path, which fuel mix it needs, and where failed cars respawn.
/// Also gives waypoint tracking a "progress on the main line" value while a car
/// is on the shortcut, and validates that no checkpoint is skipped.
/// </summary>
[DisallowMultipleComponent]
public class ShortcutBranch : MonoBehaviour
{
    [Header("Requirement")]
    public string displayName = "Broken Bridge";
    public FuelMixType requiredMix = FuelMixType.SuperFart;

    [Header("Main racing line (the SAME waypoints the AI follows)")]
    [Tooltip("Parent whose children are the main waypoints in driving order.")]
    public Transform mainWaypointRoot;
    [Tooltip("Last main waypoint BEFORE the split.")]
    public int entryMainIndex;
    [Tooltip("First main waypoint AFTER the merge.")]
    public int exitMainIndex;

    [Header("Shortcut path in driving order (filled by BrokenBridgeBuilder)")]
    public List<BranchWaypoint> waypoints = new List<BranchWaypoint>();

    [Header("Missed jump")]
    [Tooltip("On the MAIN track, a little before the split, facing race direction.")]
    public Transform failRespawnPoint;

    [Header("Validation (optional but recommended)")]
    public Transform[] checkpoints;
    public Transform finishLine;

    // ── Main line helpers ────────────────────────────────────────────────────
    public int MainCount => mainWaypointRoot ? mainWaypointRoot.childCount : 0;

    public Transform GetMain(int i)
    {
        int n = MainCount;
        return n == 0 ? null : mainWaypointRoot.GetChild(Wrap(i, n));
    }

    /// <summary>How many waypoints the car still has to pass before it reaches
    /// the split, given the main index it is currently heading towards.</summary>
    public int WaypointsUntilEntry(int headingToMainIndex)
    {
        int n = MainCount;
        return n == 0 ? int.MaxValue : Wrap(entryMainIndex - headingToMainIndex, n);
    }

    /// <summary>Number of main waypoints the shortcut replaces.</summary>
    public int MainIndexSpan
    {
        get { int n = MainCount; return n == 0 ? 0 : Wrap(exitMainIndex - entryMainIndex, n); }
    }

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    // ── Progress on the shortcut ─────────────────────────────────────────────
    /// <summary>0 at the split, 1 at the merge, following the branch path.</summary>
    public float GetBranchFraction(Vector3 worldPos)
    {
        int count = waypoints.Count;
        if (count < 2) return 0f;

        float total = 0f, bestAlong = 0f, bestSqr = float.MaxValue;
        Vector3 prev = waypoints[0].point.position;
        for (int i = 1; i < count; i++)
        {
            Vector3 cur = waypoints[i].point.position;
            Vector3 seg = cur - prev;
            float len = seg.magnitude;
            float t = len > 0.001f ? Mathf.Clamp01(Vector3.Dot(worldPos - prev, seg) / (len * len)) : 0f;
            float sqr = (prev + seg * t - worldPos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; bestAlong = total + len * t; }
            total += len;
            prev = cur;
        }
        return total > 0f ? bestAlong / total : 0f;
    }

    /// <summary>
    /// For race-position code: converts a position on the shortcut into an
    /// equivalent (fractional) main-line waypoint index, interpolated from the
    /// entry index to the exit index. Wrap with Mathf.Repeat(value, MainCount)
    /// if your tracker needs it inside [0, count).
    /// </summary>
    public float GetEquivalentMainProgress(Vector3 worldPos)
    {
        return entryMainIndex + MainIndexSpan * GetBranchFraction(worldPos);
    }

    /// <summary>
    /// For off-track recovery while on the shortcut. Before the gap → back to
    /// the ramp base; after the gap → nearest landing/rejoin waypoint behind the car.
    /// </summary>
    public bool GetSafeRespawnPose(Vector3 worldPos, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (waypoints.Count < 2) return false;

        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            float d = (waypoints[i].point.position - worldPos).sqrMagnitude;
            if (d < best) { best = d; nearest = i; }
        }

        // Walk back to a waypoint that is on solid road (not the lip / mid-air).
        int pick = nearest;
        while (pick > 0 && (waypoints[pick].role == BranchWaypointRole.Launch ||
                            waypoints[pick].role == BranchWaypointRole.Airborne))
            pick--;

        // A missed jump goes to the main-track respawn instead.
        if (waypoints[nearest].role == BranchWaypointRole.Airborne && failRespawnPoint)
        {
            position = failRespawnPoint.position;
            rotation = failRespawnPoint.rotation;
            return true;
        }

        int next = Mathf.Min(pick + 1, waypoints.Count - 1);
        Vector3 dir = waypoints[next].point.position - waypoints[pick].point.position;
        dir.y = 0f;
        position = waypoints[pick].point.position + Vector3.up * 0.5f;
        rotation = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir.normalized, Vector3.up)
                                            : waypoints[pick].point.rotation;
        return true;
    }

    // ── Validation ───────────────────────────────────────────────────────────
    [ContextMenu("Validate Shortcut")]
    void ValidateFromMenu() => Validate();

    public bool Validate()
    {
        bool ok = true;
        int n = MainCount;
        if (n == 0) { Debug.LogError($"[{displayName}] mainWaypointRoot is not set or empty.", this); return false; }
        if (waypoints.Count < 4) { Debug.LogError($"[{displayName}] needs at least 4 branch waypoints.", this); ok = false; }
        foreach (var w in waypoints)
            if (w.point == null) { Debug.LogError($"[{displayName}] a branch waypoint is missing.", this); ok = false; }
        if (!failRespawnPoint) { Debug.LogWarning($"[{displayName}] no failRespawnPoint set.", this); }

        int span = MainIndexSpan;
        if (span <= 0) { Debug.LogError($"[{displayName}] exitMainIndex must come after entryMainIndex.", this); ok = false; }

        if (checkpoints != null)
            foreach (var cp in checkpoints)
                if (cp && IsSkipped(cp.position))
                {
                    Debug.LogError($"[{displayName}] checkpoint '{cp.name}' lies inside the bypassed section. " +
                                   "Move it before the split or after the merge.", cp);
                    ok = false;
                }

        if (finishLine && IsSkipped(finishLine.position))
        {
            Debug.LogError($"[{displayName}] the finish line lies inside the bypassed section.", finishLine);
            ok = false;
        }

        if (ok) Debug.Log($"[{displayName}] OK — bypasses {span} main waypoints, no checkpoint skipped.", this);
        return ok;
    }

    /// <summary>True if the nearest main waypoint to this point is strictly
    /// between the split and the merge (i.e. only the main route passes it).</summary>
    bool IsSkipped(Vector3 pos)
    {
        int n = MainCount, nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (GetMain(i).position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = i; }
        }
        int fromEntry = Wrap(nearest - entryMainIndex, n);
        return fromEntry > 0 && fromEntry < MainIndexSpan;
    }

    // ── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            var w = waypoints[i];
            if (w == null || w.point == null) continue;
            Gizmos.color = RoleColor(w.role);
            Gizmos.DrawSphere(w.point.position, 0.6f);
            if (i > 0 && waypoints[i - 1].point)
                Gizmos.DrawLine(waypoints[i - 1].point.position, w.point.position);
        }

        if (MainCount > 0)
        {
            Gizmos.color = Color.yellow;
            var e = GetMain(entryMainIndex); var x = GetMain(exitMainIndex);
            if (e) Gizmos.DrawWireCube(e.position, Vector3.one * 2f);
            if (x) Gizmos.DrawWireCube(x.position, Vector3.one * 2f);
        }
        if (failRespawnPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(failRespawnPoint.position, 1f);
            Gizmos.DrawRay(failRespawnPoint.position, failRespawnPoint.forward * 4f);
        }
    }

    static Color RoleColor(BranchWaypointRole r)
    {
        switch (r)
        {
            case BranchWaypointRole.Approach: return Color.cyan;
            case BranchWaypointRole.RampBase: return Color.blue;
            case BranchWaypointRole.Launch:   return new Color(1f, 0.5f, 0f);
            case BranchWaypointRole.Airborne: return Color.magenta;
            case BranchWaypointRole.Landing:  return Color.green;
            default:                          return Color.white;
        }
    }
}
