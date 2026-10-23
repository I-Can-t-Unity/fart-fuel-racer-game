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
    [SerializeField, Min(1)] private float maxCorneringAcceleration = 9f;
    [Tooltip("Above top speed (only reachable with a boost), keep the turning radius the car has at top speed by scaling cornering and tyre grip with speed.")]
    [SerializeField] private bool holdTurnRadiusAboveTopSpeed = false;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0, 0.45f, -0.25f);
    [Tooltip("Brake torque on each wheel while the engine is off (out of fuel), so the car coasts to a stop instead of rolling on forever.")]
    [SerializeField, Min(0)] private float engineOffDrag = 300f;
    [SerializeField] private bool showControls = true;
    [Header("Fuel and throttle")]
    [Tooltip("Top speed and engine power multiplier by fuel level, from 0 (empty) to 1 (full). Flip the curve to make a light, nearly empty car the fastest.")]
    [SerializeField] private AnimationCurve powerByFuel = new AnimationCurve(
        new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1f));
    [Tooltip("Below this fuel level the engine sputters, cutting power in short bursts.")]
    [SerializeField, Range(0, 1)] private float sputterBelowFuel = 0.1f;
    [Tooltip("How fast the throttle rises to full, per second. 3 takes about a third of a second.")]
    [SerializeField, Min(0.1f)] private float throttleRise = 3f;
    [Tooltip("How fast the throttle drops back when the pedal is released, per second.")]
    [SerializeField, Min(0.1f)] private float throttleFall = 6f;

    private Rigidbody body;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    // What the player is asking for, and the eased value the engine actually uses.
    private float throttleInput;
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
    // Set by RaceManager every frame: 0 is an empty tank, 1 a full one.
    public float FuelFraction { get; set; } = 1f;
    public float Throttle => throttle;
    public event Action Respawned;
    // Set by an AI driver: the car then takes its pedals and steering from SetInput
    // instead of the keyboard and gamepad, and hides the on-screen controls box.
    public bool ExternalInput { get; set; }

    public void ConfigureWheels(Wheel[] bindings) => wheels = bindings;

    // Used by an AI driver in place of the player's keys: throttle -1 to 1 (negative reverses),
    // steering -1 (left) to 1 (right). The throttle still eases in, exactly as for the player.
    public void SetInput(float throttleAmount, float steeringAmount, bool brake)
    {
        throttleInput = Mathf.Clamp(throttleAmount, -1, 1);
        steering = Mathf.Clamp(steeringAmount, -1, 1);
        braking = brake;
    }

    // Lets an AI driver give its car its own handling without editing the scene.
    public void Tune(float topSpeed, float torque, float rise, float cornering)
    {
        topSpeedKmh = Mathf.Max(1, topSpeed);
        motorTorque = Mathf.Max(0, torque);
        throttleRise = Mathf.Max(0.1f, rise);
        maxCorneringAcceleration = Mathf.Max(1, cornering);
    }

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
        // Old fuel/speed system: instant, keyboard-only throttle.
        // Keyboard keyboard = Keyboard.current;
        // throttle = 0;
        // steering = 0;
        // braking = false;
        // if (keyboard == null || !Application.isFocused)
        //     return;
        // throttle = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
        //     - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
        // steering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
        //     - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
        // braking = keyboard.spaceKey.isPressed;
        // resetRequested |= keyboard.rKey.wasPressedThisFrame;

        // An AI-driven car keeps whatever SetInput gave it and ignores the keys (R included).
        if (ExternalInput)
            return;

        throttleInput = 0;
        steering = 0;
        braking = false;
        if (!Application.isFocused)
            return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            throttleInput = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            steering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            braking = keyboard.spaceKey.isPressed;
            resetRequested |= keyboard.rKey.wasPressedThisFrame;
        }
        // Triggers give analog throttle and reverse, the left stick steers, A brakes and Start resets.
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            throttleInput += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            steering += pad.leftStick.x.ReadValue();
            braking |= pad.buttonSouth.isPressed;
            resetRequested |= pad.startButton.wasPressedThisFrame;
        }
        throttleInput = Mathf.Clamp(throttleInput, -1, 1);
        steering = Mathf.Clamp(steering, -1, 1);
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
            throttle = 0;
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

        // Ease the throttle toward the input: it rises gently and drops quickly.
        float throttleRate = Mathf.Abs(throttleInput) > Mathf.Abs(throttle) ? throttleRise : throttleFall;
        throttle = Mathf.MoveTowards(throttle, throttleInput, throttleRate * Time.fixedDeltaTime);
        // A low tank weakens the engine, and near empty it sputters.
        float power = powerByFuel.Evaluate(FuelFraction);
        if (FuelFraction < sputterBelowFuel && Mathf.PerlinNoise(Time.time * 4f, 0) < 0.35f)
            power = 0;

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        // Old fuel/speed system:
        // bool changingDirection = throttle * forwardSpeed < -0.5f;
        // Use the raw input so pressing the other way still brakes instantly.
        bool changingDirection = throttleInput * forwardSpeed < -0.5f;
        bool applyBrake = braking || changingDirection;
        // Coast drag and reverse motor torque are not brake-light triggers.
        IsBraking = applyBrake;
        // Old fuel/speed system: fixed top speed and full torque whatever the fuel level.
        // float limit = (throttle >= 0 ? topSpeedKmh : reverseSpeedKmh) / 3.6f;
        // float torque = applyBrake || !EngineRunning || Mathf.Abs(forwardSpeed) >= limit ? 0 : throttle * motorTorque;
        // float idleBrake = !EngineRunning ? engineOffDrag : Mathf.Abs(throttle) < 0.01f ? 40f : 0;
        float limit = (throttle >= 0 ? topSpeedKmh * power : reverseSpeedKmh) / 3.6f;
        float torque = applyBrake || !EngineRunning || Mathf.Abs(forwardSpeed) >= limit ? 0 : throttle * motorTorque * power;
        float idleBrake = !EngineRunning ? engineOffDrag : Mathf.Abs(throttleInput) < 0.01f ? 40f : 0;
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
        // if (!showControls)
        //     return;
        if (!showControls || ExternalInput)
            return;
        // GUI.Box(new Rect(16, 16, 310, 76),
        //     $"{SpeedKmh:0} km/h\nWASD / Arrows: drive and steer\nSpace: brake    R: reset");
        string controls = "WASD / Arrows: drive and steer\nSpace: brake    R: reset" + extraControls;
        int lines = controls.Split('\n').Length;
        GUI.Box(new Rect(16, 16, 310, 20 + 20 * lines), controls);
    }
}
