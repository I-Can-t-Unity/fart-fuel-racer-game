
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AICarWaypointDriver : MonoBehaviour
{
    [Header("Track")]
    public LoopTrackGenerator track;
    public float laneOffset = 1.5f;
    public float carHeight = 0.15f;

    [Header("AI Driving")]
    public float speedKph = 55f;
    public float cornerSpeedKph = 35f;
    public float steeringSpeed = 180f;
    public int lookAheadPoints = 8;

    [Header("Collisions")]
    [Tooltip("Hits weaker than this, in km/h, are ignored so scraping alongside doesn't stall the AI.")]
    public float minImpactKph = 5f;
    [Tooltip("Speed the AI loses per km/h of impact, so harder hits slow it more.")]
    public float slowdownPerImpactKph = 1.5f;
    [Tooltip("The most speed hits can take away at once, in km/h.")]
    public float maxSlowdownKph = 40f;
    [Tooltip("How fast the AI gets its speed back after a hit, in km/h per second.")]
    // public float speedRecoveryKph = 20f;
    public float speedRecoveryKph = 40f;
    [Tooltip("How far a hit shoves the AI sideways, in metres per km/h of impact.")]
    public float knockPerImpactKph = 0.08f;
    [Tooltip("How quickly the shove dies away. Higher values settle faster.")]
    // public float knockDamping = 4f;
    public float knockDamping = 8f;
    [Tooltip("Turn added by a hit, in degrees per km/h of impact; the steering pulls it straight again.")]
    public float spinPerImpactKph = 1.5f;

    private Rigidbody rb;
    private int currentWaypoint;
    private float speedPenalty;
    private Vector3 knockVelocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // The AI car was copied from the player car. Its CarDriver would read
        // the keyboard and teleport the AI back to the start whenever R is pressed.
        CarDriver playerControls = GetComponent<CarDriver>();
        if (playerControls != null)
            playerControls.enabled = false;
    }

    void Start()
    {
        rb.isKinematic = true;

        if (track == null)
            track = FindFirstObjectByType<LoopTrackGenerator>();
    }

    void FixedUpdate()
    {
        if (track == null || track.centerline.Count == 0)
            return;

        int count = track.centerline.Count;

        // Find the nearest point on the racetrack.
        float closestDistance = float.MaxValue;
        int closestIndex = currentWaypoint;

        for (int i = 0; i < count; i++)
        {
            Vector3 difference = track.GetPoint(i) - rb.position;
            difference.y = 0f;

            float distance = difference.sqrMagnitude;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        currentWaypoint = closestIndex;

        // Aim ahead along the racetrack.
        int targetIndex =
            (currentWaypoint + lookAheadPoints) % count;

        Vector3 direction = track.GetForward(targetIndex);

        Vector3 side = new Vector3(
            -direction.z,
            0f,
            direction.x
        );

        Vector3 target =
            track.GetPoint(targetIndex) + side * laneOffset;

        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.001f)
            return;

        // Smoothly rotate toward the target.
        Quaternion desiredRotation =
            Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        Quaternion newRotation = Quaternion.RotateTowards(
            rb.rotation,
            desiredRotation,
            steeringSpeed * Time.fixedDeltaTime
        );

        rb.MoveRotation(newRotation);

        // Detect upcoming curves.
        Vector3 forwardNow =
            track.GetForward(currentWaypoint);

        Vector3 forwardAhead =
            track.GetForward((targetIndex + 6) % count);

        float turnAngle =
            Vector3.Angle(forwardNow, forwardAhead);

        float cornerFactor =
            Mathf.Clamp01(turnAngle / 60f);

        float desiredSpeed =
            Mathf.Lerp(speedKph, cornerSpeedKph, cornerFactor);

        // After a hit the AI is slower for a moment and gets its speed back gradually.
        speedPenalty = Mathf.MoveTowards(speedPenalty, 0f, speedRecoveryKph * Time.fixedDeltaTime);
        desiredSpeed = Mathf.Max(0f, desiredSpeed - speedPenalty);

        // Move forward at the desired speed.
        Vector3 movement =
            (newRotation * Vector3.forward) *
            (desiredSpeed / 3.6f) *
            Time.fixedDeltaTime;

        // Keep the AI car at its intended height above the road.
        Vector3 newPosition = rb.position + movement;

        // A hit's sideways shove fades out; steering toward the racing line brings the car back.
        newPosition += knockVelocity * Time.fixedDeltaTime;
        knockVelocity *= Mathf.Exp(-knockDamping * Time.fixedDeltaTime);

        // Kinematic bodies pass straight through walls, so keep a shoved car on the road.
        Vector3 nearest = track.GetPoint(currentWaypoint);
        Vector3 nearestForward = track.GetForward(currentWaypoint);
        Vector3 across = new Vector3(-nearestForward.z, 0f, nearestForward.x);
        float sideways = Vector3.Dot(newPosition - nearest, across);
        float maxSideways = track.roadWidth * 0.5f + track.curbWidth;
        newPosition -= across * (sideways - Mathf.Clamp(sideways, -maxSideways, maxSideways));

        // newPosition.y = track.surfaceY + 0.25f;
        newPosition.y = track.surfaceY + carHeight;

        rb.MovePosition(newPosition);
    }

    // Kinematic bodies aren't moved by physics, so a hit is turned into a slowdown,
    // a sideways shove and a little spin, all of which wear off as the AI drives on.
    void OnCollisionEnter(Collision collision)
    {
        Rigidbody other = collision.rigidbody;
        if (other == null || other.isKinematic)
            return;

        // The impulse is what the other car felt, so this is how much its speed changed.
        float impactKph = collision.impulse.magnitude / other.mass * 3.6f;
        if (impactKph < minImpactKph)
            return;

        speedPenalty = Mathf.Min(maxSlowdownKph, speedPenalty + impactKph * slowdownPerImpactKph);

        Vector3 away = rb.position - other.position;
        away.y = 0f;
        // Decaying at knockDamping, this start speed shoves the car knockPerImpactKph metres per km/h.
        knockVelocity += away.normalized * impactKph * knockPerImpactKph * knockDamping;

        // Set the rotation directly: FixedUpdate's MoveRotation would otherwise overwrite it.
        float spinSide = Mathf.Sign(Vector3.Dot(transform.right, away));
        rb.rotation *= Quaternion.Euler(0f, spinSide * impactKph * spinPerImpactKph, 0f);
    }
}
