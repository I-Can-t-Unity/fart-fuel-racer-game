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
///
/// AICarWaypointDriver adds this at runtime. Its AI follows LoopTrackGenerator's
/// centre-line points, not a waypoint root, so when a branch has no Main Waypoint
/// Root the router works in centre-line points instead ("track mode"): the split
/// and merge are found from the branch itself, and every index it takes in or
/// hands out is a centre-line index.
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
    // Old: when ahead the AI took the shortcut only half the time, so a Super Fart saved
    // for the bridge was often carried past it.
    // [Range(0f, 1f)] public float takeChanceWhenAhead = 0.5f;
    [Tooltip("Chance of taking the shortcut when holding its mix and ahead of the player. Without the mix it is never taken.")]
    [Range(0f, 1f)] public float takeChanceWhenAhead = 1f;
    [Tooltip("Don't burn the matching mix as a boost if a shortcut is this close.")]
    public int holdMixLookahead = 10;

    [Header("Track mode (branch without a Main Waypoint Root)")]
    [Tooltip("The track the AI follows. Leave empty to find it.")]
    public LoopTrackGenerator track;
    [Tooltip("Decide this many centre-line points before the split (about 4.3 m each on Bridge Circuit).")]
    public int decidePointsBefore = 6;

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

    // Track mode: each branch's split, merge and missed-jump respawn as centre-line indices.
    struct TrackSpan { public int entry, exit, respawn; }
    readonly Dictionary<ShortcutBranch, TrackSpan> trackSpans = new Dictionary<ShortcutBranch, TrackSpan>();

    /// <summary>True from committing to the shortcut until rejoining or failing.</summary>
    public bool IsUsingShortcut => CurrentState != State.MainLine;
    /// <summary>True once the car has left the main line for the branch (not just committed).</summary>
    public bool IsFollowingBranch => CurrentState != State.MainLine && CurrentState != State.Committed;

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
            // Old: main waypoints only.
            // if (b && b.requiredMix == fuel.HeldMix && b.WaypointsUntilEntry(headingToMainIndex) <= holdMixLookahead)
            if (b && b.requiredMix == fuel.HeldMix && UntilEntry(b, headingToMainIndex) <= holdMixLookahead)
                return true;
        return false;
    }

    /// <summary>Gives up on the shortcut before the entry gate (lost mix, stuck).
    /// Returns the main index to resume at, or -1 if not on a shortcut.</summary>
    public int AbortShortcut()
    {
        var b = ActiveBranch;
        if (b == null) return -1;
        ResetToMain();
        decidedFor = b;
        return RespawnIndex(b);
    }

    /// <summary>Main index nearest the branch's fail respawn point.</summary>
    public int RespawnIndex(ShortcutBranch b) => b == null ? -1 : TrackMode(b) ? Span(b).respawn : b.entryMainIndex;

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
            // Old: main waypoints only.
            // int until = b.WaypointsUntilEntry(heading);
            // if (until > decideWaypointsBefore + 1) { if (decidedFor == b) decidedFor = null; continue; }
            int until = UntilEntry(b, heading);
            int window = TrackMode(b) ? decidePointsBefore : decideWaypointsBefore;
            if (until > window + 1) { if (decidedFor == b) decidedFor = null; continue; }
            if (decidedFor == b) continue;   // already decided for this pass
            // Old: marked decided before checking the mix, so a mix made inside the window was never used.
            // decidedFor = b;

            // Only ever choose the branch while holding the mix it needs; otherwise stay on the main route.
            bool hasMix = fuel != null && fuel.HeldMix == b.requiredMix;
            if (!hasMix) continue;
            decidedFor = b;

            float chance = IsBehindRival() ? takeChanceWhenBehind : takeChanceWhenAhead;
            if (Random.value <= chance)
            {
                ActiveBranch = b;
                CurrentState = State.Committed;
                Debug.Log($"[AI] Holding {b.requiredMix}: taking the {b.displayName}.", this);
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

        // Old: main waypoints only.
        // int until = b.WaypointsUntilEntry(heading);
        // Transform entry = b.GetMain(b.entryMainIndex);
        // bool atSplit = until == 0 && entry && Vector3.Distance(rb.position, entry.position) < reachRadius * 2f;
        // bool passedSplit = until == b.MainCount - 1; // already heading to entry+1
        int until = UntilEntry(b, heading);
        bool atSplit, passedSplit;
        if (TrackMode(b))
        {
            // Centre-line points are only a few metres apart, so allow one either side.
            Vector3 split = track.GetPoint(Span(b).entry);
            atSplit = until <= 1 && FlatDistance(rb.position, split) < reachRadius * 2f;
            passedSplit = until >= track.centerline.Count - 2;
        }
        else
        {
            Transform entry = b.GetMain(b.entryMainIndex);
            atSplit = until == 0 && entry && Vector3.Distance(rb.position, entry.position) < reachRadius * 2f;
            passedSplit = until == b.MainCount - 1; // already heading to entry+1
        }

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

        // Recheck before the entry gate, which is where the mix is spent: without it the gate
        // would turn the car away and it would fall short, so give up now instead.
        if (CurrentState == State.OnBranch && !ShortcutTracker.IsOnBranch(rb) && BeforeGate()
            && (fuel == null || fuel.HeldMix != b.requiredMix))
        {
            int resume = AbortShortcut();
            if (resume >= 0) RecoveredToMainLine?.Invoke(resume);
            return false;
        }

        // Air / landing transitions
        if (CurrentState == State.OnBranch && !grounded && PastLaunch()) CurrentState = State.Airborne;
        if (CurrentState == State.Airborne && grounded) { CurrentState = State.Landing; SkipToFirst(BranchWaypointRole.Landing); }

        // Advance along the branch
        while (branchIndex < wps.Count && Reached(branchIndex)) branchIndex++;

        if (branchIndex >= wps.Count)
        {
            // Exit gate normally fires first; this is the fallback.
            // Old: int resume = b.exitMainIndex;
            int resume = ExitIndex(b);
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

    // Still on the approach or ramp run-up, so the entry gate is ahead.
    bool BeforeGate()
    {
        var wps = ActiveBranch.waypoints;
        return branchIndex < wps.Count && (wps[branchIndex].role == BranchWaypointRole.Approach
                                           || wps[branchIndex].role == BranchWaypointRole.RampBase);
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
        // Old: RejoinedMainLine?.Invoke(b.exitMainIndex);
        RejoinedMainLine?.Invoke(ExitIndex(b));
    }

    void OnFailed(Rigidbody car, ShortcutBranch b)
    {
        if (car != rb) return;
        ResetToMain();
        decidedFor = b;               // don't immediately retry (the mix is spent anyway)
        // Old: RecoveredToMainLine?.Invoke(b.entryMainIndex);
        RecoveredToMainLine?.Invoke(RespawnIndex(b));
    }

    // ── Track mode ───────────────────────────────────────────────────────────
    bool TrackMode(ShortcutBranch b)
    {
        if (b.MainCount > 0) return false;
        if (!track) track = Object.FindFirstObjectByType<LoopTrackGenerator>();
        return track && track.centerline.Count > 0 && b.waypoints.Count >= 2;
    }

    TrackSpan Span(ShortcutBranch b)
    {
        if (trackSpans.TryGetValue(b, out var span)) return span;
        // Split = the builder's entry anchor (else the branch's first point), merge = its exit
        // anchor (else the branch's last point), missed jump = the fail respawn point.
        var builder = b.GetComponent<BrokenBridgeBuilder>();
        Vector3 entry = builder && builder.entryAnchor ? builder.entryAnchor.position : b.waypoints[0].point.position;
        Vector3 exit = builder && builder.exitAnchor ? builder.exitAnchor.position : b.waypoints[b.waypoints.Count - 1].point.position;
        span.entry = NearestTrackIndex(entry);
        span.exit = NearestTrackIndex(exit);
        span.respawn = b.failRespawnPoint ? NearestTrackIndex(b.failRespawnPoint.position) : span.entry;
        trackSpans[b] = span;
        return span;
    }

    int NearestTrackIndex(Vector3 p)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < track.centerline.Count; i++)
        {
            float d = FlatDistance(track.GetPoint(i), p);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Main points still to pass before the split (centre-line points in track mode).</summary>
    public int UntilEntry(ShortcutBranch b, int heading)
    {
        if (!b) return int.MaxValue;
        if (!TrackMode(b)) return b.WaypointsUntilEntry(heading);
        int n = track.centerline.Count;
        return ((Span(b).entry - heading) % n + n) % n;
    }

    int ExitIndex(ShortcutBranch b) => TrackMode(b) ? Span(b).exit : b.exitMainIndex;

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    void OnDrawGizmosSelected()
    {
        if (ActiveBranch == null || branchIndex >= ActiveBranch.waypoints.Count || !rb) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(rb.position, ActiveBranch.waypoints[branchIndex].point.position);
    }
}
