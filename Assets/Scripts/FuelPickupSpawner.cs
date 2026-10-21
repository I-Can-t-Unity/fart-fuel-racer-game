using System.Collections;
using UnityEngine;

// Puts rows of fuel cubes across the loop track. Each row has one cube of every
// fuel type in a random order, so the player has to pick a lane.
public class FuelPickupSpawner : MonoBehaviour
{
    public LoopTrackGenerator track;
    public FuelInventory inventory;
    [Tooltip("Centerline points (0-179) where a row is placed. These are about 100 m apart around the lap, so there is time to pick a lane before the next row.")]
    public int[] rowWaypoints = { 40, 85, 127, 171 };
    [Tooltip("Distance between cube centres in a row. From about 4 m the car fits between two cubes; keep it under about 5 m so the outer cubes stay on the road.")]
    public float laneSpacing = 4.5f;
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

    IEnumerator Start()
    {
        // The track fills in its centerline in its own Start.
        while (track == null || track.centerline.Count == 0)
            yield return null;

        // One cube per fuel type, so a new type added to FuelType gets its own lane.
        FuelType[] types = (FuelType[])System.Enum.GetValues(typeof(FuelType));
        foreach (int waypoint in rowWaypoints)
        {
            Shuffle(types);
            Vector3 center = track.GetPoint(waypoint) + Vector3.up * hoverHeight;
            Vector3 forward = track.GetForward(waypoint);
            Vector3 across = new Vector3(-forward.z, 0, forward.x);
            for (int lane = 0; lane < types.Length; lane++)
                Spawn(types[lane], center + across * (lane - (types.Length - 1) * .5f) * laneSpacing, forward, lane % 2 == 1);
        }
    }

    // Every other lane gets a raised label, so neighbouring labels never overlap.
    void Spawn(FuelType type, Vector3 position, Vector3 forward, bool raiseLabel)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Fuel_" + type;
        cube.transform.SetParent(transform, false);
        cube.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        cube.transform.localScale = Vector3.one * cubeSize;
        cube.GetComponent<Collider>().isTrigger = true;
        cube.GetComponent<Renderer>().sharedMaterial = inventory.MaterialOf(type);

        FuelPickup pickup = cube.AddComponent<FuelPickup>();
        pickup.type = type;
        pickup.inventory = inventory;
        pickup.labelFontSize = labelFontSize;
        pickup.labelGap = labelGap;
        pickup.labelReadableDistance = labelReadableDistance;
        pickup.labelMaxScale = labelMaxScale;
        pickup.raiseLabel = raiseLabel;
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
