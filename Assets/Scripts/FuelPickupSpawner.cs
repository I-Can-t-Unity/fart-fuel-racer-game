using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Puts rows of fuel cans across the loop track. Each row has one can of every
// fuel type in a random order, so the player has to pick a lane. Behind each row
// an invisible gate tells analytics that the player drove past it.
public class FuelPickupSpawner : MonoBehaviour
{
    public LoopTrackGenerator track;
    public FuelInventory inventory;
    // Old: rows for the Classic loop (180 points, 438 m), about 100 m apart.
    // public int[] rowWaypoints = { 40, 85, 127, 171 };
    [Tooltip("Centerline points where a row is placed. Points are not evenly spaced (about 9 m apart on straights, 3 m in tight corners), so these were picked by distance for the Bridge Circuit (324 points, 1393 m): a row every 90-160 m, on straights or gentle bends, clear of the checkpoint. Two rows (14, 30) come before the broken-bridge split so a car can collect the same wrong fuel twice for Super Fart; 72 and 98 are on the arch the shortcut skips, so only main-route cars pass them.")]
    public int[] rowWaypoints = { 14, 30, 72, 98, 127, 148, 187, 220, 244, 271, 295 };
    [Tooltip("Distance between cube centres in a row. From about 4 m the car fits between two cubes; keep it under about 5 m so the outer cubes stay on the road.")]
    public float laneSpacing = 4.5f;
    [Tooltip("A/B test: each race picks one of these lane spacings at random, so analytics can compare pickup rates. Leave empty to always use Lane Spacing.")]
    public float[] abTestLaneSpacings = { 3.5f, 4.5f };
    [Tooltip("How far past each row its analytics gate sits, in metres.")]
    public float gateOffset = 6f;
    [Tooltip("Height of each jerry can in metres. The serialized field name is retained for existing scenes.")]
    public float cubeSize = 1.2f;
    [Tooltip("Keeps the cubes above the wheels (about 1 m) so they never touch the suspension, while still hitting the car body.")]
    public float hoverHeight = 1.7f;
    [Tooltip("Size of the fuel name above each cube; the effect line is a bit smaller.")]
    public float labelFontSize = 8f;
    [Tooltip("Gap between the top of a cube and its label, in metres.")]
    public float labelGap = .4f;
    [Tooltip("Beyond this distance from the camera labels grow, so they stay readable from further away.")]
    public float labelReadableDistance = 30f;
    [Tooltip("Largest a label grows to. Above about 2.2, long names in neighbouring lanes start to overlap.")]
    public float labelMaxScale = 1.4f;

    // The lane spacing this race actually uses (picked from the A/B list if there is one).
    public float LaneSpacingUsed { get; private set; }

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

        // One cube per fuel type, so a new type added to FuelType gets its own lane.
        FuelType[] types = (FuelType[])System.Enum.GetValues(typeof(FuelType));
        for (int row = 0; row < rowWaypoints.Length; row++)
        {
            int waypoint = rowWaypoints[row];
            Shuffle(types);
            Vector3 center = track.GetPoint(waypoint) + Vector3.up * hoverHeight;
            Vector3 forward = track.GetForward(waypoint);
            Vector3 across = new Vector3(-forward.z, 0, forward.x);
            var rowPickups = new List<FuelPickup>();
            for (int lane = 0; lane < types.Length; lane++)
            {
                Vector3 position = center + across * (lane - (types.Length - 1) * .5f) * LaneSpacingUsed;
                rowPickups.Add(Spawn(types[lane], position, forward, row, lane));
            }
            SpawnGate(row, track.GetPoint(waypoint) + forward * gateOffset, forward, rowPickups);
        }
    }

    // Every other lane gets a raised label, so neighbouring labels never overlap.
    FuelPickup Spawn(FuelType type, Vector3 position, Vector3 forward, int row, int lane)
    {
        GameObject can = new GameObject();
        can.AddComponent<MeshFilter>();
        can.AddComponent<MeshRenderer>();
        can.AddComponent<BoxCollider>();
        can.name = "Fuel_" + type;
        can.transform.SetParent(transform, false);
        can.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        can.transform.localScale = Vector3.one * cubeSize;
        can.GetComponent<Collider>().isTrigger = true;
        can.GetComponent<Renderer>().sharedMaterial = inventory.MaterialOf(type);

        FuelPickup pickup = can.AddComponent<FuelPickup>();
        pickup.type = type;
        pickup.inventory = inventory;
        pickup.labelFontSize = labelFontSize;
        pickup.labelGap = labelGap;
        pickup.labelReadableDistance = labelReadableDistance;
        pickup.labelMaxScale = labelMaxScale;
        pickup.raiseLabel = lane % 2 == 1;
        pickup.row = row;
        pickup.lane = lane;
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

    static void Shuffle(FuelType[] items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
