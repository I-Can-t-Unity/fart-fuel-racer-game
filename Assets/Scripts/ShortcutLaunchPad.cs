using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Sits on the ramp lip. Cars that PAID the mix get a computed launch that lands
// them on the landing target (if they arrive straight and fast enough).
// Cars that did NOT pay get their speed capped so they always fall short —
// this keeps the jump fair no matter how fast a car's normal top speed is.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(BoxCollider))]
public class ShortcutLaunchPad : MonoBehaviour
{
    public ShortcutBranch branch;
    [Tooltip("Point on the landing ramp the car's pivot should come down at.")]
    public Transform landingTarget;
    [Tooltip("The far broken edge (used for the 'no-boost' speed cap).")]
    public Transform farEdge;

    [Header("Assisted launch (mix paid)")]
    [Range(5f, 45f)] public float launchAngle = 18f;
    [Tooltip("Below this speed the assist does not fire — the car just rolls off.")]
    public float minSpeedForAssist = 8f;
    [Tooltip("Arriving more crooked than this = no assist. This is the skill part.")]
    public float maxHeadingError = 20f;
    public bool zeroSpinOnLaunch = true;

    [Header("No mix paid")]
    public bool capUnboostedSpeed = true;
    [Range(0.3f, 0.95f)] public float unboostedSpeedFactor = 0.7f;

    [Header("Physics")]
    [Tooltip("<= 0 uses Physics.gravity. If your car adds extra downforce in the air, put the total here.")]
    public float gravityOverride = 0f;

    public event System.Action<Rigidbody, bool> Launched; // (car, assisted)

    readonly Dictionary<Rigidbody, float> cooldown = new Dictionary<Rigidbody, float>();

    float G => gravityOverride > 0f ? gravityOverride : Mathf.Abs(Physics.gravity.y);
    Vector3 Axis
    {
        get { Vector3 f = transform.forward; f.y = 0f; return f.sqrMagnitude > 0f ? f.normalized : Vector3.forward; }
    }

    void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;
        if (cooldown.TryGetValue(rb, out float until) && Time.time < until) return;
        cooldown[rb] = Time.time + 1f;

        Vector3 vel = RbCompat.GetVelocity(rb);
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        float speed = flat.magnitude;
        float heading = speed > 0.1f ? Vector3.Angle(flat, Axis) : 180f;
        bool paid = ShortcutTracker.HasBoost(rb);

        if (paid && landingTarget && speed >= minSpeedForAssist && heading <= maxHeadingError)
        {
            Vector3 dir = flat / speed; // keep the car's own heading: a crooked approach lands crooked
            float alongAxis = Vector3.Dot(landingTarget.position - rb.position, Axis);
            float d = alongAxis / Mathf.Max(0.2f, Vector3.Dot(dir, Axis));
            float dy = landingTarget.position.y - rb.position.y;

            if (SolveLaunchSpeed(d, dy, launchAngle, G, out float v))
            {
                float a = launchAngle * Mathf.Deg2Rad;
                RbCompat.SetVelocity(rb, dir * (v * Mathf.Cos(a)) + Vector3.up * (v * Mathf.Sin(a)));
                if (zeroSpinOnLaunch) RbCompat.SetAngularVelocity(rb, Vector3.zero);
                ShortcutTracker.ConsumeBoost(rb);
                Launched?.Invoke(rb, true);
                return;
            }
        }

        if (!paid && capUnboostedSpeed)
        {
            float cap = unboostedSpeedFactor * MinClearSpeed();
            if (speed > cap)
            {
                Vector3 capped = flat * (cap / speed);
                RbCompat.SetVelocity(rb, new Vector3(capped.x, vel.y, capped.z));
            }
        }
        Launched?.Invoke(rb, false);
    }

    /// <summary>Speed (along the ramp angle) needed just to reach the far edge.</summary>
    public float MinClearSpeed()
    {
        if (!farEdge) return 30f;
        float d = Vector3.Dot(farEdge.position - transform.position, Axis);
        float dy = farEdge.position.y - transform.position.y;
        return SolveLaunchSpeed(d, dy, launchAngle, G, out float v) ? v : 30f;
    }

    /// Projectile at fixed angle θ: d = v cosθ T,  dy = v sinθ T − ½ g T²
    public static bool SolveLaunchSpeed(float d, float dy, float angleDeg, float g, out float v)
    {
        v = 0f;
        if (d <= 0.1f) return false;
        float a = angleDeg * Mathf.Deg2Rad;
        float t2 = 2f * (d * Mathf.Tan(a) - dy) / g;
        if (t2 <= 0f) return false;
        v = d / (Mathf.Cos(a) * Mathf.Sqrt(t2));
        return true;
    }

    // Draws the boosted arc (green) and the best no-mix arc (red) for tuning.
    void OnDrawGizmos()
    {
        Vector3 start = transform.position;
        float a = launchAngle * Mathf.Deg2Rad;
        if (landingTarget)
        {
            float d = Vector3.Dot(landingTarget.position - start, Axis);
            if (SolveLaunchSpeed(d, landingTarget.position.y - start.y, launchAngle, G, out float v))
                DrawArc(start, Axis * (v * Mathf.Cos(a)) + Vector3.up * (v * Mathf.Sin(a)), Color.green);
        }
        if (farEdge)
        {
            float vc = unboostedSpeedFactor * MinClearSpeed();
            DrawArc(start, Axis * (vc * Mathf.Cos(a)) + Vector3.up * (vc * Mathf.Sin(a)), Color.red);
        }
    }

    void DrawArc(Vector3 p, Vector3 v, Color c)
    {
        Gizmos.color = c;
        float g = G;
        Vector3 prev = p;
        for (int i = 1; i <= 40; i++)
        {
            float t = i * 0.05f;
            Vector3 next = p + v * t + Vector3.down * (0.5f * g * t * t);
            Gizmos.DrawLine(prev, next);
            prev = next;
            if (next.y < p.y - 6f) break;
        }
    }
}
