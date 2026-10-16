using System.Collections;
using UnityEngine;

// Puts rows of fuel cubes across the loop track. Each row has one cube of every
// fuel type in a random order, so the player has to pick a lane.
public class FuelPickupSpawner : MonoBehaviour
{
    public LoopTrackGenerator track;
    public FuelInventory inventory;
    [Tooltip("Centerline points (0-179) where a row is placed. Straight sections work best.")]
    public int[] rowWaypoints = { 43, 66, 135, 151 };
    public float laneSpacing = 3.5f;
    public float cubeSize = 1.2f;
    [Tooltip("Keeps the cubes above the wheels (about 1 m) so they never touch the suspension, while still hitting the car body.")]
    public float hoverHeight = 1.7f;

    IEnumerator Start()
    {
        // The track fills in its centerline in its own Start.
        while (track == null || track.centerline.Count == 0)
            yield return null;

        FuelType[] types = { FuelType.Correct, FuelType.Green, FuelType.Purple };
        foreach (int waypoint in rowWaypoints)
        {
            Shuffle(types);
            Vector3 center = track.GetPoint(waypoint) + Vector3.up * hoverHeight;
            Vector3 forward = track.GetForward(waypoint);
            Vector3 across = new Vector3(-forward.z, 0, forward.x);
            for (int lane = 0; lane < types.Length; lane++)
                Spawn(types[lane], center + across * (lane - 1) * laneSpacing, forward);
        }
    }

    void Spawn(FuelType type, Vector3 position, Vector3 forward)
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
