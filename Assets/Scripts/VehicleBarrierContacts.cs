using System.Collections.Concurrent;
using Unity.Collections;
using UnityEngine;

// Side-wall impulses should deflect a supported car, not lever its chassis over
// the barrier. Keep yaw and separation, but apply lateral impulses at COM height.
public static class VehicleBarrierContacts
{
    private struct CarState
    {
        public float centerHeight;
        public bool supported;
    }

    // Contact modification may run on physics worker threads. Only cached values
    // and thread-safe lookups are accessed there; no components or scene queries.
    private static readonly ConcurrentDictionary<int, CarState> Cars = new ConcurrentDictionary<int, CarState>();
    private static readonly ConcurrentDictionary<int, byte> Barriers = new ConcurrentDictionary<int, byte>();

    static VehicleBarrierContacts()
    {
        Physics.ContactModifyEvent += Modify;
        Physics.ContactModifyEventCCD += Modify;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Cars.Clear();
        Barriers.Clear();
    }

    public static void RegisterBarrier(int colliderId) => Barriers[colliderId] = 0;
    public static void RemoveBarrier(int colliderId) => Barriers.TryRemove(colliderId, out _);
    public static void RemoveCar(int bodyId) => Cars.TryRemove(bodyId, out _);
    public static void UpdateCar(int bodyId, float centerHeight, bool supported)
        => Cars[bodyId] = new CarState { centerHeight = centerHeight, supported = supported };

    private static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        foreach (var pair in pairs)
        {
            CarState car;
            if (!(Barriers.ContainsKey(pair.otherColliderInstanceID) && Cars.TryGetValue(pair.bodyInstanceID, out car)) &&
                !(Barriers.ContainsKey(pair.colliderInstanceID) && Cars.TryGetValue(pair.otherBodyInstanceID, out car)))
                continue;
            if (!car.supported)
                continue;
            for (int i = 0; i < pair.contactCount; i++)
            {
                Vector3 normal = pair.GetNormal(i);
                // Preserve top-face landings and all contacts of airborne cars.
                if (Mathf.Abs(normal.y) > 0.25f)
                    continue;
                Vector3 point = pair.GetPoint(i);
                point.y = car.centerHeight;
                normal.y = 0f;
                pair.SetPoint(i, point);
                pair.SetNormal(i, normal.normalized);
                pair.SetBounciness(i, 0f);
            }
        }
    }
}
