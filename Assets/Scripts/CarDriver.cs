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
    [Tooltip("Above top speed (only reachable with a boost), keep the turning radius the car has at top speed by scaling cornering and tyre grip with speed.")]
    [SerializeField] private bool holdTurnRadiusAboveTopSpeed = false;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0, 0.45f, -0.25f);
    [Tooltip("Brake torque on each wheel while the engine is off (out of fuel), so the car coasts to a stop instead of rolling on forever.")]
    [SerializeField, Min(0)] private float engineOffDrag = 300f;
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
    private float[] baseSidewaysStiffness;
    private string extraControls = "";

    public float SpeedKmh => body == null ? 0 : body.linearVelocity.magnitude * 3.6f;
    public bool IsBraking { get; private set; }
    // Turned off when the tank is empty: throttle does nothing, but steering and brakes still work.
    public bool EngineRunning { get; set; } = true;
    public event Action Respawned;

    public void ConfigureWheels(Wheel[] bindings) => wheels = bindings;

    // Lets other systems on the car list their keys in the on-screen controls box.
    public void AddControlsHint(string line) => extraControls += "\n" + line;

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
        baseSidewaysStiffness = Array.ConvertAll(wheels, wheel => wheel.collider.sidewaysFriction.stiffness);
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
            Respawned?.Invoke();
            return;
        }

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        bool changingDirection = throttle * forwardSpeed < -0.5f;
        bool applyBrake = braking || changingDirection;
        // Coast drag and reverse motor torque are not brake-light triggers.
        IsBraking = applyBrake;
        float limit = (throttle >= 0 ? topSpeedKmh : reverseSpeedKmh) / 3.6f;
        float torque = applyBrake || !EngineRunning || Mathf.Abs(forwardSpeed) >= limit ? 0 : throttle * motorTorque;
        float idleBrake = !EngineRunning ? engineOffDrag : Mathf.Abs(throttle) < 0.01f ? 40f : 0;
        float speedFraction = Mathf.Clamp01(SpeedKmh / topSpeedKmh);
        float steeringLimit = Mathf.Lerp(steeringAngle, steeringAngle * 0.4f, speedFraction);
        // Bicycle-model estimate: lateral acceleration = speed^2 * tan(angle) / wheelbase.
        // Use planar speed, including sideways motion, so a skid cannot unlock
        // sharp low-speed steering just because forward velocity has dropped.
        float planarSpeedSquared = Vector3.ProjectOnPlane(body.linearVelocity, transform.up).sqrMagnitude;
        // Past top speed the allowed cornering grows with speed squared, which holds the
        // turning radius (and steering angle) at their top-speed values. The tyres get the
        // same extra grip so they can actually deliver that cornering.
        float topSpeed = topSpeedKmh / 3.6f;
        float overspeed = holdTurnRadiusAboveTopSpeed ? Mathf.Max(1f, planarSpeedSquared / (topSpeed * topSpeed)) : 1f;
        float safeAngle = Mathf.Atan(wheelbase * maxCorneringAcceleration * overspeed /
            Mathf.Max(planarSpeedSquared, 0.01f)) * Mathf.Rad2Deg;
        steeringLimit = Mathf.Min(steeringLimit, safeAngle);
        currentSteering = Mathf.Clamp(currentSteering, -steeringLimit, steeringLimit);
        float targetSteering = steering * steeringLimit;
        currentSteering = Mathf.MoveTowards(currentSteering, targetSteering, steeringResponse * Time.fixedDeltaTime);
        for (int i = 0; i < wheels.Length; i++)
        {
            Wheel wheel = wheels[i];
            wheel.collider.steerAngle = wheel.steering ? currentSteering : 0;
            wheel.collider.motorTorque = torque;
            wheel.collider.brakeTorque = applyBrake ? brakeTorque : idleBrake;
            WheelFrictionCurve sideways = wheel.collider.sidewaysFriction;
            sideways.stiffness = baseSidewaysStiffness[i] * overspeed;
            wheel.collider.sidewaysFriction = sideways;
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
        string controls = "WASD / Arrows: drive and steer\nSpace: brake    R: reset" + extraControls;
        int lines = controls.Split('\n').Length;
        GUI.Box(new Rect(16, 16, 310, 20 + 20 * lines), controls);
    }
}
