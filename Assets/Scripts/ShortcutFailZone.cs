using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Trigger volume in the gap under the bridge. Falling in = respawn on the main
// track before the split (mix already spent → the shortcut "cost you time").
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(BoxCollider))]
public class ShortcutFailZone : MonoBehaviour
{
    public ShortcutBranch branch;
    readonly Dictionary<Rigidbody, float> cooldown = new Dictionary<Rigidbody, float>();

    void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || branch == null || branch.failRespawnPoint == null) return;
        // Only cars that actually drove into the shortcut can "drown".
        // A car on the main track that touches this box is ignored.
        if (!ShortcutTracker.TryGetBranch(rb, out var onBranch) || onBranch != branch) return;
        if (cooldown.TryGetValue(rb, out float until) && Time.time < until) return;
        cooldown[rb] = Time.time + 1.5f;

        Vector3 p = branch.failRespawnPoint.position;
        Quaternion r = branch.failRespawnPoint.rotation;

        var handler = ShortcutTracker.FindOnCar<ICarRespawnHandler>(rb);
        if (handler != null)
        {
            handler.RespawnAt(p, r);
        }
        else
        {
            RbCompat.SetVelocity(rb, Vector3.zero);
            RbCompat.SetAngularVelocity(rb, Vector3.zero);
            rb.position = p;
            rb.rotation = r;
            rb.transform.SetPositionAndRotation(p, r);
        }
        ShortcutTracker.RaiseFailed(rb, branch);
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
