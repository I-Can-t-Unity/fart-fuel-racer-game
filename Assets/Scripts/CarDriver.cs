using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarDriver : MonoBehaviour
{
    [Serializable]
    public class Wheel
    {
        public WheelCollider collider;
        public Transform mesh;
        public bool steering;
        [NonSerialized] public Quaternion rotationOffset;
        [NonSerialized] public Vector3 pivotOffset;
    }

    [SerializeField] private Wheel[] wheels = new Wheel[0];
    [Header("Driving")]
    [SerializeField, Min(0)] private float motorTorque = 800f;
    [SerializeField, Min(0)] private float brakeTorque = 2500f;
    [SerializeField, Min(1)] private float topSpeedKmh = 100f;
    [SerializeField, Min(1)] private float reverseSpeedKmh = 30f;
    [SerializeField, Range(0, 45)] private float steeringAngle = 28f;
    [SerializeField, Min(0)] private float steeringResponse = 90f;
    [Tooltip("Limits how sharply the car can corner at speed, in metres per second squared. Lower values give wider, steadier fast turns.")]
    [SerializeField, Min(1)] private float maxCorneringAcceleration = 6f;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0, 0.45f, -0.25f);
    [SerializeField] private bool showControls = true;

    private Rigidbody body;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float throttle;
    private float steering;
    private float currentSteering;
    private bool braking;
    private bool resetRequested;
    private float wheelbase = 2.3f;

    public float SpeedKmh => body == null ? 0 : body.linearVelocity.magnitude * 3.6f;
    public bool IsBraking { get; private set; }

    public void ConfigureWheels(Wheel[] bindings) => wheels = bindings;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.centerOfMass = centerOfMass;
        spawnPosition = body.position;
        spawnRotation = body.rotation;
        if (wheels.Length != 4)
        {
            Debug.LogError("CarDriver needs four wheel bindings. Select PlayerCar and use Tools > Car > Set Up Driving.", this);
            enabled = false;
            return;
        }
        foreach (Wheel wheel in wheels)
        {
            if (wheel == null || wheel.collider == null || wheel.mesh == null)
            {
                Debug.LogError("CarDriver has a missing wheel collider or mesh.", this);
                enabled = false;
                return;
            }
            // Preserve the imported mesh orientation and any off-center mesh pivot.
            Transform axle = wheel.collider.transform;
            wheel.rotationOffset = Quaternion.Inverse(axle.rotation) * wheel.mesh.rotation;
            Vector3 restCenter = axle.TransformPoint(wheel.collider.center)
                - axle.up * (wheel.collider.suspensionDistance * 0.5f);
            wheel.pivotOffset = Quaternion.Inverse(axle.rotation) * (wheel.mesh.position - restCenter);
        }
        float frontZ = 0, rearZ = 0;
        int frontCount = 0, rearCount = 0;
        foreach (Wheel wheel in wheels)
        {
            float z = transform.InverseTransformPoint(wheel.collider.transform.position).z;
            if (wheel.steering) { frontZ += z; frontCount++; }
            else { rearZ += z; rearCount++; }
        }
        if (frontCount > 0 && rearCount > 0)
            wheelbase = Mathf.Max(0.1f, Mathf.Abs(frontZ / frontCount - rearZ / rearCount));
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        throttle = 0;
        steering = 0;
        braking = false;
        if (keyboard == null || !Application.isFocused)
            return;
        throttle = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
            - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
        steering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
            - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
        braking = keyboard.spaceKey.isPressed;
        resetRequested |= keyboard.rKey.wasPressedThisFrame;
    }

    private void FixedUpdate()
    {
        if (resetRequested)
        {
            IsBraking = false;
            body.position = spawnPosition + Vector3.up * 0.5f;
            body.rotation = spawnRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            currentSteering = 0;
            resetRequested = false;
            foreach (Wheel wheel in wheels)
            {
                wheel.collider.motorTorque = 0;
                wheel.collider.brakeTorque = brakeTorque;
                wheel.collider.steerAngle = 0;
            }
            return;
        }

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        bool changingDirection = throttle * forwardSpeed < -0.5f;
        bool applyBrake = braking || changingDirection;
        // Coast drag and reverse motor torque are not brake-light triggers.
        IsBraking = applyBrake;
        float limit = (throttle >= 0 ? topSpeedKmh : reverseSpeedKmh) / 3.6f;
        float torque = applyBrake || Mathf.Abs(forwardSpeed) >= limit ? 0 : throttle * motorTorque;
        float speedFraction = Mathf.Clamp01(SpeedKmh / topSpeedKmh);
        float steeringLimit = Mathf.Lerp(steeringAngle, steeringAngle * 0.4f, speedFraction);
        // Bicycle-model estimate: lateral acceleration = speed^2 * tan(angle) / wheelbase.
        // Use planar speed, including sideways motion, so a skid cannot unlock
        // sharp low-speed steering just because forward velocity has dropped.
        float planarSpeedSquared = Vector3.ProjectOnPlane(body.linearVelocity, transform.up).sqrMagnitude;
        float safeAngle = Mathf.Atan(wheelbase * maxCorneringAcceleration /
            Mathf.Max(planarSpeedSquared, 0.01f)) * Mathf.Rad2Deg;
        steeringLimit = Mathf.Min(steeringLimit, safeAngle);
        currentSteering = Mathf.Clamp(currentSteering, -steeringLimit, steeringLimit);
        float targetSteering = steering * steeringLimit;
        currentSteering = Mathf.MoveTowards(currentSteering, targetSteering, steeringResponse * Time.fixedDeltaTime);
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.steerAngle = wheel.steering ? currentSteering : 0;
            wheel.collider.motorTorque = torque;
            wheel.collider.brakeTorque = applyBrake ? brakeTorque : (Mathf.Abs(throttle) < 0.01f ? 40f : 0);
        }
    }

    private void LateUpdate()
    {
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            wheel.mesh.SetPositionAndRotation(position + rotation * wheel.pivotOffset,
                rotation * wheel.rotationOffset);
        }
    }

    private void OnDisable()
    {
        IsBraking = false;
        foreach (Wheel wheel in wheels)
        {
            if (wheel?.collider == null)
                continue;
            wheel.collider.motorTorque = 0;
            wheel.collider.brakeTorque = brakeTorque;
        }
    }

    private void OnGUI()
    {
        if (!showControls)
            return;
        // GUI.Box(new Rect(16, 16, 310, 76),
        //     $"{SpeedKmh:0} km/h\nWASD / Arrows: drive and steer\nSpace: brake    R: reset");
        GUI.Box(new Rect(16, 16, 310, 60),
            "WASD / Arrows: drive and steer\nSpace: brake    R: reset");
    }
}
