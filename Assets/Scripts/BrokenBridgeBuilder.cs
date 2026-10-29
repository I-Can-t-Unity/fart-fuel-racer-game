using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Builds the whole Super Fart shortcut from 4 anchors you place in the scene:
///
///   EntryAnchor  — on the main track centreline where the branch splits off.
///                  Blue arrow (forward) = race direction.
///   LaunchLip    — where the ramp ends (only X/Z matter; height is set for you).
///   FarEdge      — the broken edge on the other side of the gap (X/Z only).
///   ExitAnchor   — on the main track centreline where the branch merges back.
///                  Blue arrow = race direction.
///
/// Right-click the component → "Build Shortcut". Re-run any time; it replaces
/// only its own "_Generated" child, and that child is protected from the
/// track generator by PreserveOnRegenerate.
///
/// Generates: entry road (curve) → run-up ramp → GAP → landing ramp → exit
/// road (curve), side rails on the raised parts, pillars, broken-edge debris,
/// a water strip + fail zone in the gap, entry/exit gates, launch pad,
/// landing target, wall openings, respawn point, and the AI waypoints.
/// </summary>
public class BrokenBridgeBuilder : MonoBehaviour
{
    [Header("Anchors")]
    public Transform entryAnchor;
    public Transform launchLipAnchor;
    public Transform farEdgeAnchor;
    public Transform exitAnchor;

    [Header("Auto placement (step 1: Snap Anchors To Track)")]
    [Tooltip("The object with LoopTrackGenerator on it (LoopTrack).")]
    public Transform trackObject;
    [Range(0.2f, 0.6f)] public float lipFraction = 0.42f;
    [Range(0.4f, 0.8f)] public float farEdgeFraction = 0.58f;

    [Header("Road")]
    [Tooltip("Width of the SHORTCUT road. Narrower than the main road is fine.")]
    public float roadWidth = 12f;
    [Tooltip("Width of the MAIN track (LoopTrackGenerator → Road Width). Sizes the wall openings.")]
    public float mainRoadWidth = 20f;
    public float deckThickness = 0.6f;
    [Range(0.15f, 0.7f)] public float curveTangent = 0.5f;
    [Range(0.15f, 0.7f)] public float exitCurveTangent = 0.35f;
    public int curveSamples = 20;

    [Header("Jump")]
    [Tooltip("Height of the lip above the track.")]
    public float bridgeHeight = 3.5f;
    [Tooltip("Length of the run-up ramp. 3.5 m high over 12 m ≈ 16°.")]
    public float rampLength = 12f;
    [Tooltip("Landing edge sits this much lower than the lip (easier landing).")]
    public float landingDrop = 0.75f;
    public float landingRampLength = 16f;
    [Tooltip("Height of your car's pivot above the road (for the landing target).")]
    public float carPivotHeight = 0.5f;

    [Header("Rails")]
    public float railHeight = 1.0f;
    public float railThickness = 0.4f;

    [Header("Materials (optional)")]
    public Material roadMaterial;
    public Material railMaterial;
    public Material hazardMaterial;
    public Material waterMaterial;

    [Header("Main line (passed to ShortcutBranch)")]
    public Transform mainWaypointRoot;
    public int entryMainIndex;
    public int exitMainIndex;
    public FuelMixType requiredMix = FuelMixType.SuperFart;

    const string RootName = "_Generated";

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 1: you only drag Entry and Exit roughly onto the road. This puts
    // them exactly on the road centre, at road height, facing along the track,
    // and places Lip + FarEdge in the infield between them.
    [ContextMenu("1. Snap Anchors To Track")]
    public void SnapAnchors()
    {
        if (!entryAnchor || !launchLipAnchor || !farEdgeAnchor || !exitAnchor)
        {
            Debug.LogError("Snap: assign all 4 anchors first.", this);
            return;
        }
#if UNITY_EDITOR
        Undo.RecordObjects(new Object[] { entryAnchor, launchLipAnchor, farEdgeAnchor, exitAnchor }, "Snap Anchors");
#endif
        Physics.SyncTransforms();
        var pts = ReadCenterline();
        if (pts != null && pts.Count >= 8)
        {
            int n = pts.Count;
            int ie = Nearest(pts, entryAnchor.position);
            int ix = Nearest(pts, exitAnchor.position);
            // Race direction = the way that gets from Entry to Exit through the
            // part being bypassed (the shorter way round).
            int steps = ((ix - ie) % n + n) % n;
            int sign = steps <= n / 2 ? 1 : -1;
            PlaceOnTrack(entryAnchor, pts, ie, sign);
            PlaceOnTrack(exitAnchor, pts, ix, sign);
        }
        else
        {
            Debug.LogWarning("Snap: couldn't read the track centreline. Assign 'Track Object' (LoopTrack). " +
                             "Only heights were snapped; rotate Entry/Exit by hand.", this);
            entryAnchor.position = OnRoad(entryAnchor.position);
            exitAnchor.position = OnRoad(exitAnchor.position);
        }

        Vector3 a = entryAnchor.position, b = exitAnchor.position;
        launchLipAnchor.position = Vector3.Lerp(a, b, lipFraction);
        farEdgeAnchor.position = Vector3.Lerp(a, b, farEdgeFraction);

        Debug.Log("Snap done. Check: both yellow arrows on Entry/Exit must point the way the cars drive. " +
                  "If they point backwards, you swapped Entry and Exit — swap their positions and snap again. " +
                  "Next: right-click → 2. Build Shortcut.", this);
    }

    List<Vector3> ReadCenterline()
    {
        if (!trackObject) return null;
        List<Vector3> best = null;
        int bestScore = -1;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var comp in trackObject.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;
            for (var type = comp.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                foreach (var f in type.GetFields(F))
                    Consider(f.Name, SafeGet(() => f.GetValue(comp)), ref best, ref bestScore);
                foreach (var p in type.GetProperties(F))
                    if (p.CanRead && p.GetIndexParameters().Length == 0)
                        Consider(p.Name, SafeGet(() => p.GetValue(comp)), ref best, ref bestScore);
            }
        }
        if (best == null) return null;

        var world = new List<Vector3>(best.Count);
        foreach (var v in best) world.Add(trackObject.TransformPoint(v));
        return world;
    }

    static object SafeGet(System.Func<object> get)
    {
        try { return get(); } catch { return null; }
    }

    static void Consider(string name, object value, ref List<Vector3> best, ref int bestScore)
    {
        List<Vector3> list = null;
        if (value is List<Vector3> l3) list = l3;
        else if (value is Vector3[] a3) list = new List<Vector3>(a3);
        else if (value is List<Vector2> l2) { list = new List<Vector3>(); foreach (var v in l2) list.Add(new Vector3(v.x, 0f, v.y)); }
        else if (value is Vector2[] a2) { list = new List<Vector3>(); foreach (var v in a2) list.Add(new Vector3(v.x, 0f, v.y)); }
        if (list == null || list.Count < 8) return;

        int score = list.Count + (name.ToLowerInvariant().Contains("center") || name.ToLowerInvariant().Contains("centre") ? 100000 : 0);
        if (score > bestScore) { bestScore = score; best = list; }
    }

    static int Nearest(List<Vector3> pts, Vector3 p)
    {
        int best = 0; float d = float.MaxValue;
        for (int i = 0; i < pts.Count; i++)
        {
            float dd = Flat(pts[i] - p).sqrMagnitude;
            if (dd < d) { d = dd; best = i; }
        }
        return best;
    }

    void PlaceOnTrack(Transform t, List<Vector3> pts, int i, int sign)
    {
        int n = pts.Count;
        Vector3 tan = Flat(pts[(i + 1) % n] - pts[(i - 1 + n) % n]) * sign;
        t.position = OnRoad(pts[i]);
        if (tan.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(tan.normalized, Vector3.up);
    }

    /// <summary>Same X/Z, Y = top of whatever road surface is under it.</summary>
    Vector3 OnRoad(Vector3 p)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue; // ignore the shortcut itself
            if (h.point.y > bestY) bestY = h.point.y;
        }
        return new Vector3(p.x, float.IsNegativeInfinity(bestY) ? p.y : bestY, p.z);
    }

    // ─────────────────────────────────────────────────────────────────────────
    [ContextMenu("2. Build Shortcut")]
    public void Build()
    {
        if (!entryAnchor || !launchLipAnchor || !farEdgeAnchor || !exitAnchor)
        {
            Debug.LogError("BrokenBridgeBuilder: assign all 4 anchors first.", this);
            return;
        }

        if (!GetComponent<PreserveOnRegenerate>()) gameObject.AddComponent<PreserveOnRegenerate>();

        // Empty material slots render pink/magenta in URP → fill them in.
        if (!roadMaterial && trackObject)
        {
            var trackRenderer = trackObject.GetComponent<Renderer>();
            if (trackRenderer) roadMaterial = trackRenderer.sharedMaterial;
        }
        if (!roadMaterial) roadMaterial = MakeMaterial("Shortcut_Road", new Color(0.35f, 0.35f, 0.37f));
        if (!railMaterial) railMaterial = MakeMaterial("Shortcut_Rail", new Color(0.85f, 0.85f, 0.85f));
        if (!hazardMaterial) hazardMaterial = MakeMaterial("Shortcut_Hazard", new Color(1f, 0.55f, 0.1f));
        if (!waterMaterial) waterMaterial = MakeMaterial("Shortcut_Water", new Color(0.15f, 0.35f, 0.65f));

        // Clear previous output
        var old = transform.Find(RootName);
        if (old) DestroyImmediate(old.gameObject);
        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        // All geometry is computed in WORLD space, so keep the generated root
        // world-aligned even if Shortcut_BrokenBridge has been moved or rotated.
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);

        // ── Key points ──────────────────────────────────────────────────────
        float entryY = entryAnchor.position.y;
        float exitY = exitAnchor.position.y;

        Vector3 axis = Flat(farEdgeAnchor.position - launchLipAnchor.position).normalized;
        Vector3 lip = new Vector3(launchLipAnchor.position.x, entryY + bridgeHeight, launchLipAnchor.position.z);
        Vector3 rampBase = lip - axis * rampLength; rampBase.y = entryY;

        Vector3 farEdge = new Vector3(farEdgeAnchor.position.x, entryY + bridgeHeight - landingDrop, farEdgeAnchor.position.z);
        Vector3 landEnd = farEdge + axis * landingRampLength; landEnd.y = exitY;

        Vector3 entryFwd = Flat(entryAnchor.forward).normalized;
        Vector3 exitFwd = Flat(exitAnchor.forward).normalized;

        // ── Path A: entry curve + run-up ramp ───────────────────────────────
        var curveA = Bezier(entryAnchor.position, entryFwd, rampBase, axis, curveSamples, curveTangent);
        var rampA = Line(rampBase, lip, Mathf.Max(4, Mathf.CeilToInt(rampLength / 2f)));
        var pathA = Join(curveA, rampA);

        // ── Path B: landing ramp + exit curve ───────────────────────────────
        var rampB = Line(farEdge, landEnd, Mathf.Max(4, Mathf.CeilToInt(landingRampLength / 2f)));
        var curveB = Bezier(landEnd, axis, exitAnchor.position, exitFwd, curveSamples, exitCurveTangent);
        var pathB = Join(rampB, curveB);

        // ── Decks ───────────────────────────────────────────────────────────
        MakeMeshObject("Deck_EntryAndRamp", root, SweepBox(pathA, roadWidth * 0.5f, 0.02f, -deckThickness, 0f), roadMaterial, true);
        MakeMeshObject("Deck_LandingAndExit", root, SweepBox(pathB, roadWidth * 0.5f, 0.02f, -deckThickness, 0f), roadMaterial, true);

        // ── Rails on the raised parts only (open at both junctions) ─────────
        float railOff = roadWidth * 0.5f + railThickness * 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            MakeMeshObject(side < 0 ? "Rail_Ramp_L" : "Rail_Ramp_R", root,
                SweepBox(rampA, railThickness * 0.5f, railHeight, -deckThickness, side * railOff), railMaterial, true);
            MakeMeshObject(side < 0 ? "Rail_Landing_L" : "Rail_Landing_R", root,
                SweepBox(rampB, railThickness * 0.5f, railHeight, -deckThickness, side * railOff), railMaterial, true);
        }

        // ── Pillars + broken-edge debris (visual) ───────────────────────────
        Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
        foreach (float side in new[] { -1f, 1f })
        {
            Pillar(root, lip - axis * 1.0f + right * side * (roadWidth * 0.35f), entryY, lip.y - deckThickness);
            Pillar(root, farEdge + axis * 1.0f + right * side * (roadWidth * 0.35f), entryY, farEdge.y - deckThickness);
        }
        Debris(root, lip, axis, right, -1f);
        Debris(root, farEdge, axis, right, 1f);

        // ── Gap: water strip + fail zone ────────────────────────────────────
        Vector3 gapMid = (lip + farEdge) * 0.5f;
        float gapLen = Flat(farEdge - lip).magnitude;
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
        water.name = "Gap_Water";
        DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(root, false);
        water.transform.position = new Vector3(gapMid.x, entryY + 0.05f, gapMid.z);
        water.transform.rotation = Quaternion.LookRotation(Vector3.down, axis);
        water.transform.localScale = new Vector3(roadWidth + 2f, gapLen + 1f, 1f); // only under the gap, never on the track
        if (waterMaterial) water.GetComponent<Renderer>().sharedMaterial = waterMaterial;

        var branch = GetComponent<ShortcutBranch>();
        if (!branch) branch = gameObject.AddComponent<ShortcutBranch>();

        float failTop = Mathf.Max(1f, bridgeHeight - 1.5f);
        var fail = TriggerBox("FailZone", root,
            new Vector3(gapMid.x, entryY + failTop * 0.5f - 0.5f, gapMid.z), axis,
            new Vector3(roadWidth + 4f, failTop + 1f, gapLen + 2f));
        fail.AddComponent<ShortcutFailZone>().branch = branch;

        // ── Gates (placed where the branch has already left the main road) ─
        // Entry gate: first point where the shortcut is completely off the main road
        // (so cars staying on the main route never touch it). Fallback: up the ramp.
        float clearDist = WallOffset() + roadWidth * 0.5f + 0.5f;
        Vector3 gateAPos = rampBase + axis * 2f, gateADir = axis;
        if (Gen) for (int i = 0; i < curveA.Count; i++)
            if (DistToTrack(curveA[i]) >= clearDist) { gateAPos = curveA[i]; gateADir = Dir(curveA, i); break; }
        var entryGate = TriggerBox("Gate_Entry", root, gateAPos + Vector3.up * 1.5f, gateADir,
                                   new Vector3(roadWidth + 1f, 4f, 2f));
        var g1 = entryGate.AddComponent<ShortcutGate>(); g1.branch = branch; g1.type = ShortcutGate.GateType.Entry;

        Vector3 gateBPos = landEnd, gateBDir = axis;
        if (Gen) for (int i = curveB.Count - 1; i >= 0; i--)
            if (DistToTrack(curveB[i]) >= clearDist) { gateBPos = curveB[i]; gateBDir = Dir(curveB, i); break; }
        var exitGate = TriggerBox("Gate_Exit", root, gateBPos + Vector3.up * 1.5f, gateBDir,
                                  new Vector3(roadWidth + 1f, 4f, 2f));
        var g2 = exitGate.AddComponent<ShortcutGate>(); g2.branch = branch; g2.type = ShortcutGate.GateType.Exit;

        // ── Launch pad, landing target, far edge marker ─────────────────────
        float landS = landingRampLength * 0.3f;
        Vector3 landTouch = Vector3.Lerp(farEdge, landEnd, landS / landingRampLength) + Vector3.up * carPivotHeight;
        var landingTarget = Marker("LandingTarget", root, landTouch, axis);
        var farMarker = Marker("FarEdge", root, farEdge + Vector3.up * carPivotHeight, axis);

        var pad = TriggerBox("LaunchPad", root, lip - axis * 0.75f + Vector3.up * 1.2f, axis,
                             new Vector3(roadWidth, 3f, 1.5f));
        var lp = pad.AddComponent<ShortcutLaunchPad>();
        lp.branch = branch;
        lp.landingTarget = landingTarget;
        lp.farEdge = farMarker;
        lp.launchAngle = Mathf.Clamp(Mathf.Atan2(bridgeHeight, rampLength) * Mathf.Rad2Deg + 4f, 10f, 30f);

        // ── Wall openings at the split and the merge ────────────────────────
        int openings = PlaceWallOpenings(root, curveA, curveB);

        // ── Respawn on the MAIN track before the split ──────────────────────
        var respawn = Marker("FailRespawn", root, entryAnchor.position - entryFwd * 12f + Vector3.up * 0.5f, entryFwd);

        // ── AI waypoints ────────────────────────────────────────────────────
        var wpRoot = new GameObject("AI_Waypoints").transform;
        wpRoot.SetParent(root, false);
        var wps = new List<BranchWaypoint>();
        void AddWp(string n, Vector3 p, Vector3 f, BranchWaypointRole r, float s = 0f)
        {
            wps.Add(new BranchWaypoint { point = Marker(n, wpRoot, p, f), role = r, targetSpeed = s });
        }
        for (int i = 1; i <= 3; i++)
        {
            int k = Mathf.RoundToInt((curveA.Count - 1) * i / 4f);
            AddWp($"WP_Approach_{i}", curveA[k], Dir(curveA, k), BranchWaypointRole.Approach);
        }
        AddWp("WP_RampBase", rampBase, axis, BranchWaypointRole.RampBase);
        AddWp("WP_Launch", lip, axis, BranchWaypointRole.Launch);
        AddWp("WP_Airborne", gapMid + Vector3.up * 2f, axis, BranchWaypointRole.Airborne);
        AddWp("WP_Landing", landTouch, axis, BranchWaypointRole.Landing);
        AddWp("WP_LandingEnd", landEnd, axis, BranchWaypointRole.Landing);
        for (int i = 1; i <= 3; i++)
        {
            int k = Mathf.RoundToInt((curveB.Count - 1) * i / 4f);
            AddWp($"WP_Rejoin_{i}", curveB[k], Dir(curveB, k), BranchWaypointRole.Rejoin);
        }
        AddWp("WP_Merge", exitAnchor.position, exitFwd, BranchWaypointRole.Rejoin);

        // ── Fill the branch description ─────────────────────────────────────
        branch.displayName = "Broken Bridge";
        branch.requiredMix = requiredMix;
        branch.waypoints = wps;
        branch.failRespawnPoint = respawn;
        if (mainWaypointRoot)
        {
            branch.mainWaypointRoot = mainWaypointRoot;
            branch.entryMainIndex = entryMainIndex;
            branch.exitMainIndex = exitMainIndex;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(branch);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        float clear = lp.MinClearSpeed();
        Debug.Log($"Broken Bridge built. Gap {gapLen:0.0} m. A car WITHOUT the mix would need " +
                  $"{clear:0.0} m/s ({clear * 3.6f:0} km/h) to clear it; it gets capped to " +
                  $"{clear * lp.unboostedSpeedFactor:0.0} m/s. {openings} wall piece(s) will be left out by LoopTrackGenerator.", this);
    }

    static Material MakeMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.color = color;
        return m;
    }

    // ── Geometry helpers ─────────────────────────────────────────────────────
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    List<Vector3> Bezier(Vector3 a, Vector3 aFwd, Vector3 b, Vector3 bFwd, int samples, float tangent)
    {
        float len = Vector3.Distance(Flat(a), Flat(b));
        float k = len * tangent;
        Vector3 p1 = a + aFwd * k, p2 = b - bFwd * k;
        var pts = new List<Vector3>(samples + 1);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples, u = 1f - t;
            Vector3 p = u * u * u * a + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * b;
            p.y = Mathf.Lerp(a.y, b.y, Mathf.SmoothStep(0f, 1f, t));
            pts.Add(p);
        }
        return pts;
    }

    static List<Vector3> Line(Vector3 a, Vector3 b, int segments)
    {
        var pts = new List<Vector3>(segments + 1);
        for (int i = 0; i <= segments; i++) pts.Add(Vector3.Lerp(a, b, i / (float)segments));
        return pts;
    }

    static List<Vector3> Join(List<Vector3> a, List<Vector3> b)
    {
        var r = new List<Vector3>(a);
        for (int i = 1; i < b.Count; i++) r.Add(b[i]); // skip duplicate join point
        return r;
    }

    static Vector3 Dir(List<Vector3> pts, int i)
    {
        Vector3 d = Flat(pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]);
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
    }

    /// <summary>Sweeps a rectangle (half-width hw, from yBottom to yTop, shifted
    /// sideways by `lateral`) along the points. Walls stay vertical; the top
    /// follows the points' heights, so ramps come out naturally.</summary>
    static Mesh SweepBox(List<Vector3> pts, float hw, float yTop, float yBottom, float lateral)
    {
        int n = pts.Count;
        var tl = new Vector3[n]; var tr = new Vector3[n]; var br = new Vector3[n]; var bl = new Vector3[n];
        var dist = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 right = Vector3.Cross(Vector3.up, Dir(pts, i)).normalized;
            Vector3 c = pts[i] + right * lateral;
            tl[i] = c - right * hw + Vector3.up * yTop;
            tr[i] = c + right * hw + Vector3.up * yTop;
            br[i] = c + right * hw + Vector3.up * yBottom;
            bl[i] = c - right * hw + Vector3.up * yBottom;
            if (i > 0) dist[i] = dist[i - 1] + Vector3.Distance(pts[i], pts[i - 1]);
        }

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float uScale = 1f / Mathf.Max(0.01f, hw * 2f);

        // Faces: (P,Q) pairs give clockwise (front-facing) quads P_i, P_i+1, Q_i+1, Q_i
        void Face(Vector3[] P, Vector3[] Q)
        {
            int s = v.Count;
            for (int i = 0; i < n; i++)
            {
                v.Add(P[i]); v.Add(Q[i]);
                uv.Add(new Vector2(0f, dist[i] * uScale)); uv.Add(new Vector2(1f, dist[i] * uScale));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int p0 = s + i * 2, q0 = p0 + 1, p1 = p0 + 2, q1 = p0 + 3;
                t.Add(p0); t.Add(p1); t.Add(q1);
                t.Add(p0); t.Add(q1); t.Add(q0);
            }
        }
        Face(tl, tr); // top
        Face(tr, br); // right
        Face(br, bl); // bottom
        Face(bl, tl); // left

        void Cap(Vector3 a, Vector3 b, Vector3 c, Vector3 d) // clockwise a→b→c→d
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 0));
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }
        Cap(tl[0], tr[0], br[0], bl[0]);                         // start, faces backwards
        Cap(tr[n - 1], tl[n - 1], bl[n - 1], br[n - 1]);         // end, faces forwards

        var mesh = new Mesh { name = "ShortcutSweep" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject MakeMeshObject(string name, Transform parent, Mesh mesh, Material mat, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        if (mat) mr.sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    void Pillar(Transform parent, Vector3 xz, float groundY, float topY)
    {
        float h = Mathf.Max(0.2f, topY - groundY);
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        p.name = "Pillar";
        p.transform.SetParent(parent, false);
        p.transform.position = new Vector3(xz.x, groundY + h * 0.5f, xz.z);
        p.transform.localScale = new Vector3(1f, h, 1f);
        if (railMaterial) p.GetComponent<Renderer>().sharedMaterial = railMaterial;
    }

    void Debris(Transform parent, Vector3 edge, Vector3 axis, Vector3 right, float sign)
    {
        // A few tilted slabs hanging off the broken edge. Visual only (no colliders).
        for (int i = 0; i < 3; i++)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
            d.name = "BrokenSlab";
            DestroyImmediate(d.GetComponent<Collider>());
            d.transform.SetParent(parent, false);
            float across = (i - 1) * roadWidth * 0.3f;
            d.transform.position = edge - axis * sign * 0.4f + right * across + Vector3.down * (deckThickness + 0.6f + 0.3f * i);
            d.transform.rotation = Quaternion.LookRotation(axis) * Quaternion.Euler(35f + 15f * i * sign, 10f * (i - 1), 0f);
            d.transform.localScale = new Vector3(roadWidth * 0.28f, deckThickness, 2.2f);
            if (hazardMaterial) d.GetComponent<Renderer>().sharedMaterial = hazardMaterial;
        }
    }

    static GameObject TriggerBox(string name, Transform parent, Vector3 pos, Vector3 fwd, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return go;
    }

    static Transform Marker(string name, Transform parent, Vector3 pos, Vector3 fwd)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.sqrMagnitude > 0f ? fwd : Vector3.forward, Vector3.up));
        return go.transform;
    }

    // ── Track data straight from LoopTrackGenerator ─────────────────────────
    LoopTrackGenerator Gen => trackObject ? trackObject.GetComponent<LoopTrackGenerator>() : null;

    /// <summary>Distance from the main track centre line to the middle of its walls.</summary>
    float WallOffset()
    {
        var g = Gen;
        if (!g) return mainRoadWidth * 0.5f + 2.6f;
        return g.roadWidth * 0.5f + g.curbWidth + g.wallGap + g.wallThickness * 0.5f;
    }

    float DistToTrack(Vector3 p)
    {
        var g = Gen;
        if (!g || g.centerline.Count == 0) return float.MaxValue;
        float best = float.MaxValue;
        foreach (var c in g.centerline) best = Mathf.Min(best, Flat(c - p).magnitude);
        return best;
    }

    /// <summary>
    /// Works out exactly which wall pieces LoopTrackGenerator builds (same maths
    /// as the generator) and puts a small WallOpening box over each piece the
    /// shortcut road passes through. The generator then skips those pieces.
    /// </summary>
    int PlaceWallOpenings(Transform parent, List<Vector3> curveA, List<Vector3> curveB)
    {
        var g = Gen;
        if (!g || g.centerline.Count < 3)
        {
            Debug.LogWarning("BrokenBridgeBuilder: set Track Object = LoopTrack so wall openings can be placed.", this);
            return 0;
        }
        var road = new List<Vector3>(curveA);
        road.AddRange(curveB);
        float reach = roadWidth * 0.5f + 2.5f;
        float offset = WallOffset();

        var holder = new GameObject("WallOpenings").transform;
        holder.SetParent(parent, false);

        var c = g.centerline;
        int n = c.Count, count = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            Vector3 ti = (c[next] - c[(i - 1 + n) % n]).normalized;
            Vector3 tn = (c[(next + 1) % n] - c[i]).normalized;
            Vector3 si = new Vector3(-ti.z, 0, ti.x), sn = new Vector3(-tn.z, 0, tn.x);

            foreach (float sign in new[] { -1f, 1f })   // -1 = Wall_L, +1 = Wall_R
            {
                Vector3 a = c[i] + si * offset * sign, b = c[next] + sn * offset * sign;
                Vector3 mid = (a + b) * 0.5f;
                bool hit = false;
                foreach (var p in road) if (Flat(p - mid).magnitude <= reach) { hit = true; break; }
                if (!hit) continue;

                var go = new GameObject((sign < 0 ? "Open_Wall_L_" : "Open_Wall_R_") + i);
                go.transform.SetParent(holder, false);
                Vector3 along = Flat(b - a);
                go.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(along.sqrMagnitude > 0f ? along : Vector3.forward, Vector3.up));
                go.AddComponent<WallOpening>().size = new Vector3(g.wallThickness + 1.4f, 12f, along.magnitude + 0.5f);
                count++;
            }
        }
        return count;
    }

    // ── Step 0: the layout computed for this exact track ────────────────────
    [ContextMenu("0. Use Recommended Layout")]
    public void UseRecommendedLayout()
    {
        if (!entryAnchor || !launchLipAnchor || !farEdgeAnchor || !exitAnchor)
        {
            Debug.LogError("Assign the 4 anchors and Track Object first.", this);
            return;
        }
        var g = Gen;
        if (!g || g.centerline.Count < 10)
        {
            Debug.LogError("Set Track Object = LoopTrack and click 'Regenerate Track' on it first.", this);
            return;
        }
#if UNITY_EDITOR
        Undo.RecordObjects(new Object[] { this, entryAnchor, launchLipAnchor, farEdgeAnchor, exitAnchor }, "Recommended Layout");
#endif
        bool reversed = g.reverseDirection;
        bool bridge = g.layout == LoopTrackGenerator.Layout.BridgeCircuit;
        int n = g.centerline.Count;

        // Everything below is worked out in the track's ORIGINAL point order, then
        // flipped if the race runs backwards. Values were checked against the real
        // centre line: raised parts and the gap stay well clear of road and walls,
        // and no shortcut curve is tighter than the track's own corners.
        Vector3 P(int i) => g.centerline[reversed ? (n - 1 - ((i % n + n) % n)) : ((i % n + n) % n)];
        Vector3 Tan(int i) => Flat(P(i + 1) - P(i - 1)).normalized;

        int ie, ix;
        Vector3 lipCW, farCW;
        if (bridge)
        {
            // Straight shortcut across the arch: entry/exit are within 2° of the racing line.
            ie = 50; ix = 111;
            Vector3 e = P(ie), x = P(ix), ax = Flat(x - e).normalized;
            Vector3 mid = Vector3.Lerp(e, x, 0.55f);
            lipCW = mid - ax * 4f; farCW = mid + ax * 4f;          // 8 m gap
            roadWidth = 12f; bridgeHeight = 3f; rampLength = 10f;
            landingDrop = 0.5f; landingRampLength = 12f;
            curveTangent = 0.3f; exitCurveTangent = 0.3f;
        }
        else
        {
            ie = 10; ix = 54;
            lipCW = new Vector3(-54.95f, 0.12f, -2.52f); farCW = new Vector3(-49.05f, 0.12f, -1.48f);
            roadWidth = 10f; bridgeHeight = 2.5f; rampLength = 8f;
            landingDrop = 0.5f; landingRampLength = 9f;
            curveTangent = reversed ? 0.35f : 0.5f; exitCurveTangent = reversed ? 0.5f : 0.35f;
        }
        lipCW.y = farCW.y = g.surfaceY;

        Vector3 entryPos = P(ie), entryDir = Tan(ie), exitPos = P(ix), exitDir = Tan(ix), lipPos = lipCW, farPos = farCW;
        if (reversed)
        {
            entryPos = P(ix); entryDir = -Tan(ix);
            exitPos = P(ie); exitDir = -Tan(ie);
            lipPos = farCW; farPos = lipCW;
        }
        entryAnchor.SetPositionAndRotation(entryPos, Quaternion.LookRotation(entryDir, Vector3.up));
        exitAnchor.SetPositionAndRotation(exitPos, Quaternion.LookRotation(exitDir, Vector3.up));
        launchLipAnchor.SetPositionAndRotation(lipPos, Quaternion.identity);
        farEdgeAnchor.SetPositionAndRotation(farPos, Quaternion.identity);
        mainRoadWidth = g.roadWidth;
        curveSamples = 20;

        Debug.Log("Recommended layout applied. Now: ⋮ → 2. Build Shortcut. (Skip 'Snap' — it would move the anchors.)", this);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        if (entryAnchor) Gizmos.DrawRay(entryAnchor.position, entryAnchor.forward * 6f);
        if (exitAnchor) Gizmos.DrawRay(exitAnchor.position, exitAnchor.forward * 6f);
        if (launchLipAnchor && farEdgeAnchor)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(launchLipAnchor.position, farEdgeAnchor.position); // the gap
        }
    }
}
