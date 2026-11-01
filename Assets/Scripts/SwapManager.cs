using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Does the SWITCHEROO. Put it on an empty "SwapManager" object.
///
/// How the swap works: the two cars trade PLACES (position, rotation, speed) and
/// trade PAINT + CHARGES. Your input, camera and HUD stay on "your" car object, so
/// to you it feels exactly like jumping into the rival's car — and none of the
/// existing driving/camera/HUD scripts need changing.
///
/// Lap safety: a swap is refused if the finish line or a lap checkpoint lies between
/// the two cars, so lap counts and checkpoint flags always stay correct.
/// </summary>
public class SwapManager : MonoBehaviour
{
    public static SwapManager Instance { get; private set; }

    [Header("Cars")]
    public SwapCar carA;   // e.g. PlayerCar
    public SwapCar carB;   // e.g. AICar

    [Header("Rules")]
    [Tooltip("After a swap, neither car can swap again for this long.")]
    public float immunitySeconds = 4f;

    [Header("Lap safety")]
    public LoopTrackGenerator track;
    [Tooltip("FinishLineTrigger and LapCheckpoint. No swap across these.")]
    public Transform[] lapBarriers;

    [Header("AI")]
    [Tooltip("After a swap, the AI's 'next waypoint' is moved this many points ahead of where it now is.")]
    public int aiLookahead = 3;

    /// (car that touched the block, the other car). Fired after the swap.
    public static event System.Action<SwapCar, SwapCar> Swapped;

    int[] barrierIdx;

    void Awake() { Instance = this; }

    void Start()
    {
        if (!track) track = FindFirstObjectByType<LoopTrackGenerator>();
        if (carA && carB && carA.color == carB.color)
        {
            carB.color = Switcheroo.Other(carA.color);
            Debug.LogWarning($"[Switcheroo] Both cars were {Switcheroo.Name(carA.color)}; made {carB.name} {Switcheroo.Name(carB.color)}.", this);
        }
        StartCoroutine(InitBarriers());
    }

    IEnumerator InitBarriers()
    {
        while (track && track.centerline.Count == 0) yield return null;   // track builds itself in Start
        var list = new List<int>();
        if (lapBarriers != null)
            foreach (var b in lapBarriers) if (b) list.Add(Nearest(track.centerline, b.position));
        barrierIdx = list.ToArray();
    }

    public bool TrySwap(SwapCar instigator)
    {
        if (!carA || !carB) { Debug.LogWarning("SwapManager: assign Car A and Car B.", this); return false; }
        SwapCar other = instigator == carA ? carB : carA;
        if (instigator.Immune || other.Immune) return false;
        if (CrossesBarrier(instigator.Body.position, other.Body.position))
        {
            SwapHUD.Banner(instigator, "NO SWAP ACROSS THE LINE", 0.8f);
            return false;
        }

        DoSwap(instigator, other);
        return true;
    }

    void DoSwap(SwapCar a, SwapCar b)
    {
        Rigidbody ra = a.Body, rb = b.Body;

        // Trade places + motion.
        Vector3 pa = ra.position, pb = rb.position;
        Quaternion qa = ra.rotation, qb = rb.rotation;
        Vector3 va = ra.linearVelocity, vb = rb.linearVelocity;
        Vector3 wa = ra.angularVelocity, wb = rb.angularVelocity;

        Place(ra, pb, qb, vb, wb);
        Place(rb, pa, qa, va, wa);

        // Trade paint + stash (they belong to "the car" you jump into).
        CarColor ca = a.color, cb = b.color;
        int ka = a.Charges, kb = b.Charges;
        a.SetColor(cb); a.SetCharges(kb);
        b.SetColor(ca); b.SetCharges(ka);

        float until = Time.time + immunitySeconds;
        a.ImmuneUntil = until;
        b.ImmuneUntil = until;

        PoofCloud.Spawn(ra.position + Vector3.up, Switcheroo.ToColor(a.color));
        PoofCloud.Spawn(rb.position + Vector3.up, Switcheroo.ToColor(b.color));

        foreach (var car in new[] { a, b })
        {
            if (car.isPlayer) SwapHUD.Banner(car, $"SWITCHEROO!\nYOU'RE {Switcheroo.Name(car.color)} NOW", 1.8f);
            else ResyncAI(car);
        }
        Debug.Log($"[Switcheroo] {a.name} grabbed the wrong colour → swapped with {b.name}.", this);
        Swapped?.Invoke(a, b);
    }

    static void Place(Rigidbody r, Vector3 p, Quaternion q, Vector3 v, Vector3 w)
    {
        r.position = p;
        r.rotation = q;
        r.transform.SetPositionAndRotation(p, q);
        r.linearVelocity = v;
        r.angularVelocity = w;
    }

    // ── Lap safety ────────────────────────────────────────────────────────────
    bool CrossesBarrier(Vector3 p, Vector3 q)
    {
        if (!track || track.centerline.Count == 0 || barrierIdx == null || barrierIdx.Length == 0) return false;
        var c = track.centerline; int n = c.Count;
        int i = Nearest(c, p), j = Nearest(c, q);
        int d = ((j - i) % n + n) % n;
        int from = d <= n / 2 ? i : j;
        int len = d <= n / 2 ? d : n - d;               // the short way between the two cars
        foreach (int k in barrierIdx)
        {
            int off = ((k - from) % n + n) % n;
            if (off <= len) return true;
        }
        return false;
    }

    static int Nearest(List<Vector3> pts, Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 d = pts[i] - p; d.y = 0f;
            float s = d.sqrMagnitude;
            if (s < bd) { bd = s; best = i; }
        }
        return best;
    }

    // ── AI: point it at the right waypoint after being moved ─────────────────
    static readonly string[] indexNames = {
        "currentwaypoint", "currentwaypointindex", "waypointindex", "currentindex", "targetindex",
        "targetwaypoint", "nextwaypoint", "nextwaypointindex", "currentpoint", "trackpoint", "targetpoint", "nextindex"
    };

    void ResyncAI(SwapCar car)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        bool done = false;
        foreach (var mb in car.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb == null || mb is SwapCar) continue;
            var type = mb.GetType();

            // Which list of points does this AI follow? Its own, if it has one; otherwise the track.
            List<Vector3> pts = OwnWaypoints(mb, type, F) ?? (track ? track.centerline : null);
            if (pts == null || pts.Count < 4) continue;

            foreach (var f in type.GetFields(F))
            {
                if (f.FieldType != typeof(int)) continue;
                if (System.Array.IndexOf(indexNames, f.Name.ToLowerInvariant().Trim('_')) < 0) continue;
                int idx = (Nearest(pts, car.Body.position) + aiLookahead) % pts.Count;
                f.SetValue(mb, idx);
                Debug.Log($"[Switcheroo] AI resynced: {type.Name}.{f.Name} = {idx}", car);
                done = true;
            }
        }
        if (!done)
            Debug.LogWarning("[Switcheroo] Couldn't find the AI's waypoint index to update after the swap. " +
                             "If the AI drives backwards after a swap, send the AI script so it can be hooked up.", car);
    }

    static List<Vector3> OwnWaypoints(MonoBehaviour mb, System.Type type, BindingFlags F)
    {
        foreach (var f in type.GetFields(F))
        {
            object v = f.GetValue(mb);
            if (v is Transform[] ta && ta.Length >= 4) { var l = new List<Vector3>(); foreach (var t in ta) if (t) l.Add(t.position); return l; }
            if (v is List<Transform> tl && tl.Count >= 4) { var l = new List<Vector3>(); foreach (var t in tl) if (t) l.Add(t.position); return l; }
            if (v is Vector3[] va && va.Length >= 4) return new List<Vector3>(va);
            if (v is List<Vector3> vl && vl.Count >= 4 && !(mb is LoopTrackGenerator)) return vl;
            if (v is Transform root && f.Name.ToLowerInvariant().Contains("waypoint") && root.childCount >= 4)
            {
                var l = new List<Vector3>(); foreach (Transform t in root) l.Add(t.position); return l;
            }
        }
        return null;
    }
}
