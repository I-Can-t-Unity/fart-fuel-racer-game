
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AICarWaypointDriver : MonoBehaviour
{
    [Header("Track")]
    public LoopTrackGenerator track;
    public float laneOffset = 1.5f;
    public float carHeight = 0.15f;

    [Header("AI Driving")]
    public float speedKph = 35f;
    public float cornerSpeedKph = 22f;
    public float steeringSpeed = 180f;
    public int lookAheadPoints = 8;

    private Rigidbody rb;
    private int currentWaypoint;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
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

        // Move forward at the desired speed.
        Vector3 movement =
            (newRotation * Vector3.forward) *
            (desiredSpeed / 3.6f) *
            Time.fixedDeltaTime;

        // Keep the AI car at its intended height above the road.
        Vector3 newPosition = rb.position + movement;
        // newPosition.y = track.surfaceY + 0.25f;
        newPosition.y = track.surfaceY + carHeight;

        rb.MovePosition(newPosition);
    }
}
