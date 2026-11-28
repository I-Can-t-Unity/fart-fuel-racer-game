using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Puts rows of fuel cans across the loop track. Each row has one can of every
// fuel type in a random order, so the player has to pick a lane. Behind each row
// an invisible gate tells analytics that the player drove past it.
//
// Auto Place (tick it per level): rows are worked out from the track itself -
// only on straight or gently curving road, never on corners, crossings or the jump,
// none just after the start and none in the final sprint to the finish. Long
// continuous bends (the Level 5 corkscrews) get a "trail" of single blocks instead.
public class FuelPickupSpawner : MonoBehaviour
{
    public enum RowShape { Line, Diagonal, Arrow, Split }

    public LoopTrackGenerator track;
    public FuelInventory inventory;
    // Old: rows for the Classic loop (180 points, 438 m), about 100 m apart.
    // public int[] rowWaypoints = { 40, 85, 127, 171 };
    [Tooltip("Used only when Auto Place is off. Centerline points where a row is placed.")]
    public int[] rowWaypoints = { 14, 30, 72, 98, 127, 148, 187, 220, 244, 271, 295 };
    [Tooltip("Distance between cube centres in a row. From about 4 m the car fits between two cubes; keep it under about 5 m so the outer cubes stay on the road.")]
    public float laneSpacing = 4.5f;
    [Tooltip("A/B test: each race picks one of these lane spacings at random, so analytics can compare pickup rates. Leave empty to always use Lane Spacing.")]
    public float[] abTestLaneSpacings = { 3.5f, 4.5f };
    [Tooltip("Outer blocks always stay at least this far (m) inside the road edge. Lane spacing is reduced automatically on narrow roads.")]
    public float wallMargin = 1.5f;
    [Tooltip("Outer blocks always stay at least this far (m) inside the road edge, so on narrow roads the lanes squeeze together instead of sitting by the walls.")]
    public float edgeMargin = 3.5f;
    [Tooltip("How far past each row its analytics gate sits, in metres.")]
    public float gateOffset = 6f;
    [Tooltip("Height of each jerry can in metres. The serialized field name is retained for existing scenes.")]
    public float cubeSize = 1.2f;
    [Tooltip("Keeps the cubes above the wheels (about 1 m) so they never touch the suspension, while still hitting the car body.")]
    public float hoverHeight = 1.7f;

    [Header("Auto Place (strategic rows)")]
    [Tooltip("On = rows are placed from the track shape using the settings below. Off = Row Waypoints are used.")]
    public bool autoPlace = false;
    [Tooltip("Metres between rows on straight road. Lower = more blocks = harder.")]
    public float rowSpacing = 150f;
    [Tooltip("Bends tighter than this radius (m) count as corners: no rows on them.")]
    public float minCornerRadius = 45f;
    [Tooltip("Also keep rows this far (m) before and after every corner (braking and turn-in zones).")]
    public float cornerClearance = 15f;
    [Tooltip("No rows in the first metres after the start line, so the cars don't pile into one.")]
    public float startClear = 60f;
    [Tooltip("No rows in the last metres before the finish line: a block-free final sprint.")]
    public float finishClear = 150f;

    [Header("Row shapes")]
    [Tooltip("Shapes each row can take, picked at random per row. Line = 3 across. Diagonal / Arrow / Split stagger the lanes.")]
    public RowShape[] rowShapes = { RowShape.Line };
    [Tooltip("How far apart (m) staggered lanes are along the road.")]
    public float stagger = 8f;

    [Header("Trail on long bends (corkscrews)")]
    [Tooltip("On long continuous bends, place single blocks spread out and alternating inner/outer lane instead of rows.")]
    public bool trailOnLongBends = true;
    [Tooltip("A bend at least this long (m) with a radius between 18 m and Min Corner Radius gets a trail.")]
    public float trailMinLength = 80f;
    [Tooltip("Metres between trail blocks.")]
    public float trailSpacing = 25f;

    [Header("Respawn")]
    [Tooltip("Seconds before a taken block comes back. Lower = more swaps = harder.")]
    public float respawnSeconds = 10f;
    [Tooltip("A block won't reappear while any car is this close (m), so nobody gets swapped by a block popping up right in front of them.")]
    public float fairRespawnDistance = 0f;
    [Tooltip("When a whole row is back, mix up its colours, so lanes can't be memorised from lap to lap.")]
    public bool reshuffleWhenRowReturns = false;
    [Tooltip("Rows only reshuffle when no car is this close (m), so you never see colours change in front of you.")]
    public float reshuffleSafeDistance = 45f;

    // The lane spacing this race actually uses (picked from the A/B list if there is one),
    // narrowed to fit the road so the outer blocks keep Edge Margin from the edge.
    public float LaneSpacingUsed { get; private set; }

    void FitLanesToRoad()
    {
        if (track == null) return;
        float widest = Mathf.Max(1.5f, track.roadWidth * .5f - edgeMargin);   // outer lane is 1 spacing from the centre
        LaneSpacingUsed = Mathf.Min(LaneSpacingUsed, widest);
    }

    // Every row (or trail group) and whether all its blocks were available last frame.
    readonly List<List<FuelPickup>> rows = new List<List<FuelPickup>>();
    readonly List<bool> rowWasFull = new List<bool>();
    CarDriver[] cars;

    void Awake()
    {
        LaneSpacingUsed = abTestLaneSpacings != null && abTestLaneSpacings.Length > 0
            ? abTestLaneSpacings[Random.Range(0, abTestLaneSpacings.Length)]
            : laneSpacing;
    }

    IEnumerator Start()
    {
        // The track fills in its centerline in its own Start.
        while (track == null || track.centerline.Count == 0)
            yield return null;

        // Keep the outer blocks well inside the road on narrow tracks: the outer block's
        // edge stays at least Wall Margin from the road edge.
        float maxSpacing = track.roadWidth * .5f - cubeSize * .5f - wallMargin;
        if (LaneSpacingUsed > maxSpacing)
        {
            Debug.Log($"FuelPickupSpawner: lane spacing {LaneSpacingUsed:0.#} m is too wide for this {track.roadWidth:0.#} m road, using {maxSpacing:0.#} m.", this);
            LaneSpacingUsed = Mathf.Max(2.5f, maxSpacing);
        }

        FitLanesToRoad();
        cars = FindObjectsByType<CarDriver>(FindObjectsSortMode.None);
        FuelPickup.Cars = cars;

        if (autoPlace) AutoPlace();
        else
        {
            // One cube per fuel type, so a new type added to FuelType gets its own lane.
            for (int row = 0; row < rowWaypoints.Length; row++)
            {
                int waypoint = rowWaypoints[row];
                FuelType[] types = ShuffledTypes();
                Vector3 center = track.GetPoint(waypoint) + Vector3.up * hoverHeight;
                Vector3 forward = track.GetForward(waypoint);
                Vector3 across = new Vector3(-forward.z, 0, forward.x);
                var rowPickups = new List<FuelPickup>();
                for (int lane = 0; lane < types.Length; lane++)
                {
                    Vector3 position = center + across * (lane - (types.Length - 1) * .5f) * LaneSpacingUsed;
                    rowPickups.Add(Spawn(types[lane], position, forward, row, lane));
                }
                AddRow(rowPickups);
                SpawnGate(row, track.GetPoint(waypoint) + forward * gateOffset, forward, rowPickups);
            }
        }
    }

    // ---------------------------------------------------------------- automatic placement

    void AutoPlace()
    {
        var c = track.centerline;
        int n = c.Count;
        int start = ((track.startIndex % n) + n) % n;

        // Distance of every point from the start line, in race order.
        float[] dist = new float[n];
        float lap = 0f;
        for (int k = 0; k < n; k++)
        {
            int i = (start + k) % n;
            dist[i] = lap;
            lap += Flat(c[(i + 1) % n] - c[i]).magnitude;
        }

        // How tight the road bends at every point.
        float[] radius = new float[n];
        for (int i = 0; i < n; i++)
            radius[i] = Radius(Flat(c[(i - 3 + n) % n]), Flat(c[i]), Flat(c[(i + 3) % n]));

        // Points where the road passes over or under another part of itself.
        bool[] crossing = new bool[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float ds = Mathf.Abs(dist[i] - dist[j]); ds = Mathf.Min(ds, lap - ds);
                if (ds > 60f && Flat(c[i] - c[j]).sqrMagnitude < 22f * 22f) { crossing[i] = true; break; }
            }

        // Rows stay off crossings; trails (on stacked corkscrew rings) ignore them.
        bool InZone(int i, bool checkCrossing = true) =>
            dist[i] < startClear || dist[i] > lap - finishClear || (checkCrossing && crossing[i]) || NearJump(i, n);

        // Any corner within [from, to] metres of point i?
        bool CornerNear(int i, float from, float to)
        {
            for (int j = 0; j < n; j++)
            {
                float d = dist[j] - dist[i];
                if (d < -lap * .5f) d += lap; else if (d > lap * .5f) d -= lap;
                if (d >= from && d <= to && radius[j] < minCornerRadius) return true;
            }
            return false;
        }

        int rowNumber = 0, rowsPlaced = 0, trailBlocks = 0;
        float lastRow = -1e9f;
        float rowLength = 2f * stagger;
        RowShape[] shapes = rowShapes != null && rowShapes.Length > 0 ? rowShapes : new[] { RowShape.Line };

        // 1) Rows on straight / gentle road.
        for (int k = 0; k < n; k++)
        {
            int i = (start + k) % n;
            if (dist[i] - lastRow < rowSpacing || InZone(i)) continue;
            if (dist[i] + rowLength > lap - finishClear) continue;
            if (CornerNear(i, -cornerClearance, rowLength + cornerClearance)) continue;
            PlaceRow(i, shapes[Random.Range(0, shapes.Length)], rowNumber++);
            lastRow = dist[i];
            rowsPlaced++;
        }

        // 2) Trails on long continuous bends (e.g. the Level 5 corkscrews).
        if (trailOnLongBends)
        {
            bool InBend(int i) => radius[i] >= 18f && radius[i] < minCornerRadius;
            int k = 0;
            while (k < n)
            {
                int i = (start + k) % n;
                if (!InBend(i)) { k++; continue; }
                int kEnd = k;
                while (kEnd < n && InBend((start + kEnd) % n)) kEnd++;
                float runStart = dist[i], runEnd = dist[(start + kEnd - 1) % n];
                if (runEnd - runStart >= trailMinLength)
                {
                    var group = new List<FuelPickup>();
                    FuelType[] bag = ShuffledTypes();
                    int inBag = 0, side = Random.value < .5f ? 1 : -1;
                    float next = runStart + 10f;
                    for (int kk = k; kk < kEnd; kk++)
                    {
                        int p = (start + kk) % n;
                        if (dist[p] < next || dist[p] > runEnd - 10f || InZone(p, false)) continue;
                        next = dist[p] + trailSpacing;
                        Vector3 fwd = Flat(track.GetForward(p)).normalized;
                        Vector3 across = new Vector3(-fwd.z, 0, fwd.x);
                        // Trail blocks sit in the inner or outer half of the road, never by the wall.
                        Vector3 pos = c[p] + Vector3.up * hoverHeight + across * (side * LaneSpacingUsed * .7f);
                        if (inBag == bag.Length) { bag = ShuffledTypes(); inBag = 0; }
                        group.Add(Spawn(bag[inBag++], pos, fwd, rowNumber, side > 0 ? 2 : 0));
                        side = -side;
                        trailBlocks++;
                        if (group.Count == 3)
                        {
                            AddRow(group);
                            SpawnGate(rowNumber++, c[p] + fwd * gateOffset, fwd, group);
                            group = new List<FuelPickup>();
                        }
                    }
                    if (group.Count > 0) AddRow(group);
                }
                k = kEnd;
            }
        }

        Debug.Log($"FuelPickupSpawner: {rowsPlaced} rows and {trailBlocks} trail blocks on a {lap:0} m lap.", this);
    }

    // One row of one block per colour, in the given shape, starting at centre-line point i.
    void PlaceRow(int i, RowShape shape, int row)
    {
        FuelType[] types = ShuffledTypes();
        int lanes = types.Length;
        float[] ahead = new float[lanes];
        bool flip = Random.value < .5f;
        for (int l = 0; l < lanes; l++)
        {
            int ll = flip ? lanes - 1 - l : l;        // mirror some rows so they aren't always the same way round
            switch (shape)
            {
                case RowShape.Diagonal: ahead[l] = ll * stagger; break;
                case RowShape.Arrow: ahead[l] = Mathf.Abs(l - (lanes - 1) * .5f) > .1f ? stagger : 0f; break;
                case RowShape.Split: ahead[l] = ll == lanes - 1 ? 2f * stagger : 0f; break;
                default: ahead[l] = 0f; break;
            }
        }

        var rowPickups = new List<FuelPickup>();
        for (int lane = 0; lane < lanes; lane++)
        {
            int p = PointAhead(i, ahead[lane]);
            Vector3 fwd = Flat(track.GetForward(p)).normalized;
            Vector3 across = new Vector3(-fwd.z, 0, fwd.x);
            Vector3 pos = track.GetPoint(p) + Vector3.up * hoverHeight
                        + across * (lane - (lanes - 1) * .5f) * LaneSpacingUsed;
            rowPickups.Add(Spawn(types[lane], pos, fwd, row, lane));
        }
        AddRow(rowPickups);
        // The analytics gate goes just past the furthest block of the row.
        int last = PointAhead(i, MaxAhead(ahead));
        Vector3 gateFwd = Flat(track.GetForward(last)).normalized;
        SpawnGate(row, track.GetPoint(last) + gateFwd * gateOffset, gateFwd, rowPickups);
    }

    static float MaxAhead(float[] a) { float m = 0; foreach (float v in a) m = Mathf.Max(m, v); return m; }

    // The centre-line point about `metres` further along the track than point i.
    int PointAhead(int i, float metres)
    {
        var c = track.centerline; int n = c.Count; float walked = 0f; int j = i;
        while (walked < metres)
        {
            int nj = (j + 1) % n;
            walked += Flat(c[nj] - c[j]).magnitude;
            j = nj;
            if (j == i) break;
        }
        return j;
    }

    bool NearJump(int i, int n)
    {
        if (track.layout != LoopTrackGenerator.Layout.Flyover) return false;
        return i >= track.jumpRampStart - 12 && i <= track.jumpLandingEnd + 10;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static float Radius(Vector3 a, Vector3 b, Vector3 c)
    {
        float ab = (b - a).magnitude, bc = (c - b).magnitude, ac = (c - a).magnitude;
        float area = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * .5f;
        return area < 1e-4f ? 1e9f : ab * bc * ac / (4f * area);
    }

    // ---------------------------------------------------------------- respawn & reshuffle

    void AddRow(List<FuelPickup> pickups)
    {
        foreach (var p in pickups)
        {
            p.respawnSeconds = respawnSeconds;
            p.fairRespawnDistance = fairRespawnDistance;
        }
        rows.Add(pickups);
        rowWasFull.Add(true);
    }

    void Update()
    {
        if (!reshuffleWhenRowReturns) return;
        for (int r = 0; r < rows.Count; r++)
        {
            bool full = true;
            foreach (var p in rows[r]) if (p != null && !p.IsAvailable) { full = false; break; }
            // The row has just come back complete: mix its colours (once no car is close).
            if (full && !rowWasFull[r])
            {
                if (CarNear(rows[r], reshuffleSafeDistance)) continue;   // try again next frame
                Reshuffle(rows[r]);
            }
            rowWasFull[r] = full;
        }
    }

    bool CarNear(List<FuelPickup> row, float distance)
    {
        if (cars == null) return false;
        foreach (var p in row)
            foreach (var car in cars)
                if (p != null && car != null && (car.transform.position - p.transform.position).sqrMagnitude < distance * distance)
                    return true;
        return false;
    }

    void Reshuffle(List<FuelPickup> row)
    {
        // A trail group has blocks far apart, so give each a fresh random colour from a new bag;
        // a full row keeps exactly one of each colour (always a safe lane).
        FuelType[] types = ShuffledTypes();
        for (int k = 0; k < row.Count; k++)
            if (row[k] != null) row[k].SetType(types[k % types.Length]);
    }

    // ---------------------------------------------------------------- spawning (as before)

    static FuelType[] ShuffledTypes()
    {
        FuelType[] types = (FuelType[])System.Enum.GetValues(typeof(FuelType));
        for (int i = types.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (types[i], types[j]) = (types[j], types[i]);
        }
        return types;
    }

    FuelPickup Spawn(FuelType type, Vector3 position, Vector3 forward, int row, int lane)
    {
        GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cube);
        can.name = "Fuel_" + type;
        can.transform.SetParent(transform, false);
        can.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        can.transform.localScale = Vector3.one * cubeSize;
        can.GetComponent<Collider>().isTrigger = true;
        can.GetComponent<Renderer>().sharedMaterial = inventory.MaterialOf(type);

        FuelPickup pickup = can.AddComponent<FuelPickup>();
        pickup.type = type;
        pickup.inventory = inventory;
        pickup.row = row;
        pickup.lane = lane;
        pickup.respawnSeconds = respawnSeconds;
        return pickup;
    }

    // A trigger spanning the road, curbs and the gap to the walls, so the car can't miss it.
    void SpawnGate(int row, Vector3 position, Vector3 forward, List<FuelPickup> rowPickups)
    {
        var gate = new GameObject("RowGate_" + row).AddComponent<FuelRowGate>();
        gate.transform.SetParent(transform, false);
        gate.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        var box = gate.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(track.roadWidth + 2 * (track.curbWidth + track.wallGap), 4, 1);
        box.center = new Vector3(0, 2, 0);
        gate.row = row;
        gate.inventory = inventory;
        gate.pickups = rowPickups;
    }
}
