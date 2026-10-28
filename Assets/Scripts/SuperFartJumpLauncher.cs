using UnityEngine;

[RequireComponent(typeof(SuperFartJumpGate))]
public class SuperFartJumpLauncher : MonoBehaviour
{
    [Min(0f)] public float minimumForwardSpeed = 20f;
    [Min(0f)] public float upwardSpeed = 7f;

    private SuperFartJumpGate gate;

    private void Awake()
    {
        gate = GetComponent<SuperFartJumpGate>();
    }

    private void OnEnable()
    {
        SuperFartJumpGate.Activated += Launch;
    }

    private void OnDisable()
    {
        SuperFartJumpGate.Activated -= Launch;
    }

    private void Launch(SuperFartJumpGate activatedGate, Rigidbody car)
    {
        // The event is shared by all gates; respond only to this one.
        if (activatedGate != gate || car == null || car.isKinematic)
            return;

        Vector3 forward =
            Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        Vector3 velocity = car.linearVelocity;
        float forwardSpeed = Vector3.Dot(velocity, forward);

        // Preserve existing forward speed if the car is already faster.
        velocity += forward *
            Mathf.Max(0f, minimumForwardSpeed - forwardSpeed);

        // Set a consistent upward launch while preserving sideways movement.
        velocity.y = upwardSpeed;
        car.linearVelocity = velocity;
    }
}