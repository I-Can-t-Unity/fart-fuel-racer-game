using System;
using System.Collections.Generic;
using UnityEngine;

// An invisible line across the whole track just after a row of fuel cubes. When the
// player's car drives through it, the row counts as passed for analytics.
public class FuelRowGate : MonoBehaviour
{
    public int row;
    public FuelInventory inventory;
    public List<FuelPickup> pickups = new List<FuelPickup>();

    public static event Action<FuelRowGate> PlayerPassed;

    // Cubes in this row that can still be picked up (the AI may have taken some).
    public int AvailableCount
    {
        get
        {
            int count = 0;
            foreach (FuelPickup pickup in pickups)
                if (pickup != null && pickup.IsAvailable)
                    count++;
            return count;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (inventory != null && other.transform.IsChildOf(inventory.transform))
            PlayerPassed?.Invoke(this);
    }
}
