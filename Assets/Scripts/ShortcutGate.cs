using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Entry / exit gates. Put the ENTRY gate a few metres inside the branch, past
// the point where it has visibly split from the main road, so cars staying on
// the main route never touch it. Same for the EXIT gate before the merge.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(BoxCollider))]
public class ShortcutGate : MonoBehaviour
{
    public enum GateType { Entry, Exit }

    public ShortcutBranch branch;
    public GateType type = GateType.Entry;

    void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || branch == null) return;

        if (type == GateType.Entry)
        {
            if (ShortcutTracker.IsOnBranch(rb)) return; // car has several colliders

            // Driving in spends the mix (if you hold the right one).
            var holder = ShortcutTracker.FindOnCar<IFuelMixHolder>(rb);
            bool paid = holder != null
                        && holder.HeldMix == branch.requiredMix
                        && holder.TryConsumeMix(branch.requiredMix);

            ShortcutTracker.Enter(rb, branch, paid);
        }
        else
        {
            if (ShortcutTracker.TryGetBranch(rb, out var b) && b == branch)
                ShortcutTracker.RaiseCompleted(rb, branch);
        }
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = type == GateType.Entry ? new Color(0, 1, 1, 0.25f) : new Color(1, 1, 1, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
