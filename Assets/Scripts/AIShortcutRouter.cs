using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add to each AI car next to your existing AI controller. It does NOT drive
/// the car; it tells your controller where to steer while the AI is using a
/// shortcut, then hands control back at the right main waypoint.
///
/// In your AI controller (see SETUP.md for the full snippet):
///   if (router.TryGetOverride(currentWaypoint, out var cmd)) { steer to cmd.target ... }
///   else { normal waypoint following ... }
///   router.RejoinedMainLine  += i => currentWaypoint = i;
///   router.RecoveredToMainLine += i => currentWaypoint = i;
/// </summary>
public class AIShortcutRouter : MonoBehaviour
{
    public enum State { MainLine, Committed, OnBranch, Airborne, Landing }

    public struct SteerCommand
    {
        public Vector3 target;      // world point to steer towards
        public float targetSpeed;   // m/s, 0 = use your normal speed
        public bool lockSteering;   // true in the air: keep wheels straight
        public bool fullThrottle;   // true on the ramp run-up
    }

    [Header("References")]
    public Rigidbody rb;
    [Tooltip("Leave empty to auto-find every ShortcutBranch in the scene.")]
    public List<ShortcutBranch> branches = new List<ShortcutBranch>();
    [Tooltip("The player's car, used to decide whether to gamble on the shortcut.")]
    public Transform rival;

    [Header("Decision")]
    [Tooltip("Decide this many main waypoints before the split.")]
    public int decideWaypointsBefore = 3;
    [Range(0f, 1f)] public float takeChanceWhenBehind = 1f;
    [Range(0f, 1f)] public float takeChanceWhenAhead = 0.5f;
    [Tooltip("Don't burn the matching mix as a boost if a shortcut is this close.")]
    public int holdMixLookahead = 10;

    [Header("Following")]
    public float reachRadius = 5f;
    [Tooltip("How far past the lip to aim on the ramp, to arrive dead straight.")]
    public float rampAimDistance = 40f;
    public float rampSpeed = 30f;
    public float landingSpeed = 18f;

    [Header("Ground check")]
    public float groundRayLength = 1.6f;
    public LayerMask groundMask = ~0;

    public State CurrentState { get; private set; } = State.MainLine;
    public ShortcutBranch ActiveBranch { get; private set; }

    /// <summary>Main waypoint index the AI should head to after the merge.</summary>
    public event System.Action<int> RejoinedMainLine;
    /// <summary>Main waypoint index to resume at after a missed jump respawn.</summary>
    public event System.Action<int> RecoveredToMainLine;

    int branchIndex;
    ShortcutBranch decidedFor;   // branch we already rolled the dice on this lap
    IFuelMixHolder fuel;

    void Awake()
    {
        if (!rb) rb = GetComponentInParent<Rigidbody>();
        fuel = ShortcutTracker.FindOnCar<IFuelMixHolder>(rb);
        if (branches.Count == 0)
        {
#if UNITY_2023_1_OR_NEWER
            branches.AddRange(Object.FindObjectsByType<ShortcutBranch>(FindObjectsSortMode.None));
#else
            branches.AddRange(Object.FindObjectsOfType<ShortcutBranch>());
#endif
        }
    }

    void OnEnable()
    {
        ShortcutTracker.Failed += OnFailed;
        ShortcutTracker.Completed += OnCompleted;
    }

    void OnDisable()
    {
        ShortcutTracker.Failed -= OnFailed;
        ShortcutTracker.Completed -= OnCompleted;
    }

    /// <summary>Ask this from the AI's fuel logic before spending a mix as a boost.</summary>
    public bool ShouldHoldMix(int headingToMainIndex)
    {
        if (fuel == null || fuel.HeldMix == FuelMixType.None) return false;
        if (CurrentState == State.Committed) return true;
        foreach (var b in branches)
            if (b && b.requiredMix == fuel.HeldMix && b.WaypointsUntilEntry(headingToMainIndex) <= holdMixLookahead)
                return true;
        return false;
    }

    /// <param name="headingToMainIndex">Index of the main waypoint your AI is currently driving towards.</param>
    public bool TryGetOverride(int headingToMainIndex, out SteerCommand cmd)
    {
        cmd = default;
        switch (CurrentState)
        {
            case State.MainLine:
                Decide(headingToMainIndex);
                return false;

            case State.Committed:
                return UpdateCommitted(headingToMainIndex, ref cmd);

            default:
                return UpdateOnBranch(ref cmd);
        }
    }

    // ── States ───────────────────────────────────────────────────────────────
    void Decide(int heading)
    {
        foreach (var b in branches)
        {
            if (!b || b.waypoints.Count < 2) continue;
            int until = b.WaypointsUntilEntry(heading);

            if (until > decideWaypointsBefore + 1) { if (decidedFor == b) decidedFor = null; continue; }
            if (decidedFor == b) continue;   // already decided for this pass
            decidedFor = b;

            bool hasMix = fuel != null && fuel.HeldMix == b.requiredMix;
            if (!hasMix) continue;

            float chance = IsBehindRival() ? takeChanceWhenBehind : takeChanceWhenAhead;
            if (Random.value <= chance)
            {
                ActiveBranch = b;
                CurrentState = State.Committed;
                return;
            }
        }
    }

    bool UpdateCommitted(int heading, ref SteerCommand cmd)
    {
        var b = ActiveBranch;
        // Already through the entry gate (it spent the mix) → follow the branch.
        if (ShortcutTracker.TryGetBranch(rb, out var onB) && onB == b)
        {
            CurrentState = State.OnBranch;
            SnapToNearestAhead();
            return UpdateOnBranch(ref cmd);
        }
        // Lost the mix before the split (dropped / used / stolen) → stay on main line.
        if (fuel == null || fuel.HeldMix != b.requiredMix) { ResetToMain(); return false; }

        int until = b.WaypointsUntilEntry(heading);
        Transform entry = b.GetMain(b.entryMainIndex);
        bool atSplit = until == 0 && entry && Vector3.Distance(rb.position, entry.position) < reachRadius * 2f;
        bool passedSplit = until == b.MainCount - 1; // already heading to entry+1

        if (!atSplit && !passedSplit) return false;   // keep following the main line for now

        CurrentState = State.OnBranch;
        SnapToNearestAhead();
        return UpdateOnBranch(ref cmd);
    }

    bool UpdateOnBranch(ref SteerCommand cmd)
    {
        var b = ActiveBranch;
        var wps = b.waypoints;
        bool grounded = IsGrounded();

        // Air / landing transitions
        if (CurrentState == State.OnBranch && !grounded && PastLaunch()) CurrentState = State.Airborne;
        if (CurrentState == State.Airborne && grounded) { CurrentState = State.Landing; SkipToFirst(BranchWaypointRole.Landing); }

        // Advance along the branch
        while (branchIndex < wps.Count && Reached(branchIndex)) branchIndex++;

        if (branchIndex >= wps.Count)
        {
            // Exit gate normally fires first; this is the fallback.
            int resume = b.exitMainIndex;
            ResetToMain();
            RejoinedMainLine?.Invoke(resume);
            return false;
        }

        var wp = wps[branchIndex];
        cmd.target = wp.point.position;
        cmd.targetSpeed = wp.targetSpeed;

        switch (CurrentState == State.Airborne ? BranchWaypointRole.Airborne : wp.role)
        {
            case BranchWaypointRole.RampBase:
            case BranchWaypointRole.Launch:
                // Aim far past the lip along the ramp axis → arrive straight.
                Vector3 lip = FindRole(BranchWaypointRole.Launch, out var l) ? l.point.position : wp.point.position;
                Vector3 baseP = FindRole(BranchWaypointRole.RampBase, out var rbw) ? rbw.point.position : rb.position;
                Vector3 axis = lip - baseP; axis.y = 0f;
                if (axis.sqrMagnitude > 0.01f) cmd.target = lip + axis.normalized * rampAimDistance;
                cmd.targetSpeed = rampSpeed;
                cmd.fullThrottle = true;
                break;

            case BranchWaypointRole.Airborne:
                cmd.lockSteering = true;   // no steering mid-air
                cmd.fullThrottle = false;
                break;

            case BranchWaypointRole.Landing:
                if (cmd.targetSpeed <= 0f) cmd.targetSpeed = landingSpeed;
                break;
        }
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    bool Reached(int i)
    {
        var wps = ActiveBranch.waypoints;
        Vector3 p = wps[i].point.position;
        Vector3 to = p - rb.position; to.y = 0f;
        if (wps[i].role == BranchWaypointRole.Airborne) return CurrentState != State.OnBranch; // passed once airborne/landed
        if (to.magnitude < reachRadius) return true;
        // Passed it without getting close (e.g. drifted wide): compare with next segment direction
        if (i + 1 < wps.Count)
        {
            Vector3 seg = wps[i + 1].point.position - p; seg.y = 0f;
            return Vector3.Dot(rb.position - p, seg) > 0f && to.magnitude < reachRadius * 3f;
        }
        return false;
    }

    bool PastLaunch()
    {
        var wps = ActiveBranch.waypoints;
        for (int i = 0; i < branchIndex && i < wps.Count; i++)
            if (wps[i].role == BranchWaypointRole.Launch) return true;
        // also count "currently targeting the airborne point"
        return branchIndex < wps.Count && wps[branchIndex].role == BranchWaypointRole.Airborne;
    }

    /// <summary>Target the nearest branch waypoint that is still ahead of the car.</summary>
    void SnapToNearestAhead()
    {
        var wps = ActiveBranch.waypoints;
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < wps.Count; i++)
        {
            if (wps[i].role != BranchWaypointRole.Approach && wps[i].role != BranchWaypointRole.RampBase) break;
            float d = (wps[i].point.position - rb.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best + 1 < wps.Count)
        {
            Vector3 seg = wps[best + 1].point.position - wps[best].point.position;
            if (Vector3.Dot(rb.position - wps[best].point.position, seg) > 0f) best++;
        }
        branchIndex = best;
    }

    void SkipToFirst(BranchWaypointRole role)
    {
        var wps = ActiveBranch.waypoints;
        for (int i = branchIndex; i < wps.Count; i++)
            if (wps[i].role == role) { branchIndex = i; return; }
    }

    bool FindRole(BranchWaypointRole role, out BranchWaypoint found)
    {
        foreach (var w in ActiveBranch.waypoints)
            if (w.role == role && w.point) { found = w; return true; }
        found = null;
        return false;
    }

    bool IsGrounded()
    {
        return Physics.Raycast(rb.worldCenterOfMass, Vector3.down, groundRayLength,
                               groundMask, QueryTriggerInteraction.Ignore);
    }

    bool IsBehindRival()
    {
        if (!rival) return true;
        // Cheap test: is the rival in front of us along our heading?
        Vector3 fwd = rb.transform.forward;
        return Vector3.Dot(rival.position - rb.position, fwd) > 0f;
    }

    void ResetToMain()
    {
        CurrentState = State.MainLine;
        ActiveBranch = null;
        branchIndex = 0;
    }

    void OnCompleted(Rigidbody car, ShortcutBranch b)
    {
        if (car != rb || b != ActiveBranch) return;
        ResetToMain();
        RejoinedMainLine?.Invoke(b.exitMainIndex);
    }

    void OnFailed(Rigidbody car, ShortcutBranch b)
    {
        if (car != rb) return;
        ResetToMain();
        decidedFor = b;               // don't immediately retry (the mix is spent anyway)
        RecoveredToMainLine?.Invoke(b.entryMainIndex);
    }

    void OnDrawGizmosSelected()
    {
        if (ActiveBranch == null || branchIndex >= ActiveBranch.waypoints.Count || !rb) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(rb.position, ActiveBranch.waypoints[branchIndex].point.position);
    }
}
