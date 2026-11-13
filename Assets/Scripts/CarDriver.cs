using System;
using System.Collections.Generic;
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
    [SerializeField, Min(0)] private float steeringResponse = 180f;
    [Tooltip("Max lateral acceleration at speed (m/s²). Lower = wider fast turns.")]
    [SerializeField, Min(1)] private float maxCorneringAcceleration = 15f;
    [Tooltip("Above top speed, scale grip with speed up to Max Boost Grip.")]
    [SerializeField] private bool holdTurnRadiusAboveTopSpeed = false;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0, 0.35f, -0.35f);
    [Tooltip("Brake torque per wheel when out of fuel.")]
    [SerializeField, Min(0)] private float engineOffDrag = 300f;
    [Tooltip("Deceleration after the race ends (m/s²).")]
    [SerializeField, Min(0.1f)] private float raceEndDeceleration = 8f;
    [SerializeField] private bool showControls = true;

    [Header("Grip and stability")]
    [SerializeField, Range(0.5f, 2f)] private float tireGrip = 1.2f;
    [Tooltip("Downward acceleration at top speed (m/s²).")]
    [SerializeField, Min(0)] private float downforceAcceleration = 5f;
    [SerializeField, Min(0)] private float yawStability = 2.5f;
    [SerializeField, Range(1f, 3f)] private float maxBoostGrip = 1.6f;
    [Tooltip("Frictionless chassis so cars slide off each other and walls.")]
    [SerializeField] private bool slidingChassisContacts = true;

    [Header("Fuel and throttle")]
    [Tooltip("Power and top speed multiplier by fuel level (0 = empty, 1 = full).")]
    [SerializeField] private AnimationCurve powerByFuel = new AnimationCurve(
        new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1f));
    [SerializeField, Range(0, 1)] private float sputterBelowFuel = 0.1f;
    [SerializeField, Min(0.1f)] private float throttleRise = 3f;
    [SerializeField, Min(0.1f)] private float throttleFall = 6f;

    [Header("Corner assist (player only)")]
    [SerializeField] private bool cornerAssist = true;
    [Tooltip("Turn radius used when the track can't be read (m).")]
    [SerializeField, Min(3)] private float cornerAssistRadius = 14f;
    [SerializeField, Min(0)] private float cornerAssistDecel = 6f;
    [SerializeField, Min(0)] private float cornerAssistDelay = 0.12f;
    [SerializeField, Min(0.01f)] private float cornerAssistRamp = 0.3f;
    [Tooltip("Use the actual bend ahead on the track to decide how much to slow down.")]
    [SerializeField] private bool cornerAssistReadsTrack = true;
    [Tooltip("Scales bend radius down so the assist slows a little early.")]
    [SerializeField, Range(0.5f, 1f)] private float cornerAssistBendMargin = 0.8f;
    [SerializeField, Min(0.1f)] private float cornerAssistLookAheadSeconds = 2f;

    [Header("High-speed handling")]
    [Tooltip("Player steering speed at top speed, relative to low speed.")]
    [SerializeField, Range(0.2f, 1f)] private float highSpeedSteerRate = 0.85f;
    [Tooltip("How much faster steering returns to centre than it turns in.")]
    [SerializeField, Range(1f, 4f)] private float steerReturnRate = 1.5f;
    [SerializeField, Min(0)] private float antiRollForce = 3500f;
    [SerializeField, Range(0.3f, 1f)] private float rearBrakeShare = 0.7f;
    [Tooltip("Torque fades out over this last fraction of top speed.")]
    [SerializeField, Range(0f, 0.3f)] private float topSpeedTaper = 0.08f;
    [Tooltip("Extra steering angle allowed at speed beyond the no-slip limit.")]
    [SerializeField, Range(1f, 1.6f)] private float steerSlipAllowance = 1.2f;
    [Tooltip("Fraction of sideways sliding removed per second while grounded.")]
    [SerializeField, Min(0)] private float sideSlipDamping = 1.5f;

    [Header("Collisions")]
    [SerializeField] private bool bounceOnContact = true;
    [SerializeField, Range(0f, 1f)] private float carBounce = 0.35f;
    [SerializeField, Range(0f, 1f)] private float wallBounce = 0.2f;
    [Tooltip("Minimum speed (m/s) at which the car is pushed away from what it touches.")]
    [SerializeField, Min(0)] private float minSeparationSpeed = 1.2f;
    [SerializeField, Min(0)] private float maxBounceSpeed = 6f;
    [SerializeField, Min(0.02f)] private float bounceCooldown = 0.35f;

    private static readonly List<CarDriver> all = new List<CarDriver>();
    public static IReadOnlyList<CarDriver> All => all;

    private Rigidbody body;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float throttleInput;
    private float throttle;
    private float steering;
    private float currentSteering;
    private float steeringInputSmoothed;
    private bool braking;
    private bool resetRequested;
    private float wheelbase = 2.3f;
    private float[] baseSidewaysStiffness;
    private float[] baseForwardStiffness;
    private PhysicsMaterial chassisMaterial;
    private readonly Dictionary<Collider, PhysicsMaterial> chassisMaterials = new Dictionary<Collider, PhysicsMaterial>();
    private readonly Dictionary<Collider, bool> modifiableContacts = new Dictionary<Collider, bool>();
    private readonly List<Vector2Int> axles = new List<Vector2Int>();
    private int barrierBodyId;
    private float steerHeldSeconds;
    private float steerHeldSign;
    private float lastVehicleContact = float.NegativeInfinity;
    private float lastBounceTime = float.NegativeInfinity;
    private LoopTrackGenerator assistTrack;
    private bool searchedAssistTrack;
    private int assistTrackIndex = -1;
    private RigidbodyConstraints constraintsBeforeCountdown;

    public Rigidbody Body => body;
    public int GroundedWheelCount { get; private set; }
    public float SpeedKmh => body == null ? 0 : body.linearVelocity.magnitude * 3.6f;
    public bool IsBraking { get; private set; }
    public bool IsStopped { get; private set; }
    public bool IsCountdownHeld { get; private set; }
    public bool CanDrive => !IsStopped && !IsCountdownHeld
        && (RaceManager.Instance == null || RaceManager.Instance.IsRacing);
    // False when the tank is empty: no drive, but steering and brakes still work.
    public bool EngineRunning { get; set; } = true;
    public float FuelFraction { get; set; } = 1f;
    public float Throttle => throttle;
    public float MaxCorneringAcceleration => maxCorneringAcceleration;
    public event Action Respawned;
    // When true, input comes from SetInput (AI) instead of keyboard/gamepad.
    public bool ExternalInput { get; set; }

    private bool HasVehicleContact => Time.fixedTime - lastVehicleContact < Time.fixedDeltaTime * 2.5f;

    public void ConfigureWheels(Wheel[] bindings) => wheels = bindings;

    internal void SetRespawnPoint(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
    }

    internal void RequestReset() => resetRequested = true;

    public void SetInput(float throttleAmount, float steeringAmount, bool brake)
    {
        if (!CanDrive)
            return;
        throttleInput = Mathf.Clamp(throttleAmount, -1, 1);
        steering = Mathf.Clamp(steeringAmount, -1, 1);
        braking = brake;
    }

    public void SetCountdownHold(bool hold)
    {
        if (IsCountdownHeld == hold)
            return;
        if (body == null)
            body = GetComponent<Rigidbody>();

        throttleInput = throttle = steering = currentSteering = steeringInputSmoothed = 0;
        braking = resetRequested = false;
        IsBraking = hold;
        if (hold)
        {
            constraintsBeforeCountdown = body.constraints;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
        else
            body.constraints = constraintsBeforeCountdown;

        IsCountdownHeld = hold;
        foreach (Wheel wheel in wheels)
        {
            if (wheel?.collider == null)
                continue;
            wheel.collider.motorTorque = 0;
            wheel.collider.brakeTorque = hold ? brakeTorque : 0;
            wheel.collider.steerAngle = 0;
        }
    }

    // Stops driving immediately; FixedUpdate then slows the car down and parks it.
    public void StopDriving()
    {
        IsStopped = true;
        EngineRunning = false;
        throttleInput = throttle = steering = currentSteering = steeringInputSmoothed = 0;
        braking = resetRequested = false;
        IsBraking = true;

        foreach (Wheel wheel in wheels)
        {
            if (wheel?.collider == null)
                continue;
            wheel.collider.motorTorque = 0;
            wheel.collider.brakeTorque = 0;
            wheel.collider.steerAngle = 0;
        }

        if (body == null)
            body = GetComponent<Rigidbody>();
    }

    private void SlowToStop()
    {
        if (body.isKinematic)
            return;

        body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, Vector3.zero,
            Mathf.Max(0.1f, raceEndDeceleration) * Time.fixedDeltaTime);
        body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero,
            2f * Time.fixedDeltaTime);

        if (body.linearVelocity.sqrMagnitude > 0.09f || body.angularVelocity.sqrMagnitude > 0.01f)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        foreach (Wheel wheel in wheels)
        {
            if (wheel?.collider != null)
                wheel.collider.brakeTorque = brakeTorque;
        }
    }

    // Copies all handling settings (not wheels) from another car.
    public void CopyHandlingFrom(CarDriver other)
    {
        if (other == null || other == this)
            return;
        motorTorque = other.motorTorque;
        brakeTorque = other.brakeTorque;
        topSpeedKmh = other.topSpeedKmh;
        reverseSpeedKmh = other.reverseSpeedKmh;
        steeringAngle = other.steeringAngle;
        steeringResponse = other.steeringResponse;
        maxCorneringAcceleration = other.maxCorneringAcceleration;
        holdTurnRadiusAboveTopSpeed = other.holdTurnRadiusAboveTopSpeed;
        engineOffDrag = other.engineOffDrag;
        raceEndDeceleration = other.raceEndDeceleration;
        tireGrip = other.tireGrip;
        downforceAcceleration = other.downforceAcceleration;
        yawStability = other.yawStability;
        maxBoostGrip = other.maxBoostGrip;
        powerByFuel = other.powerByFuel;
        sputterBelowFuel = other.sputterBelowFuel;
        throttleRise = other.throttleRise;
        throttleFall = other.throttleFall;
        cornerAssist = other.cornerAssist;
        cornerAssistRadius = other.cornerAssistRadius;
        cornerAssistDecel = other.cornerAssistDecel;
        cornerAssistDelay = other.cornerAssistDelay;
        cornerAssistRamp = other.cornerAssistRamp;
        cornerAssistReadsTrack = other.cornerAssistReadsTrack;
        cornerAssistBendMargin = other.cornerAssistBendMargin;
        cornerAssistLookAheadSeconds = other.cornerAssistLookAheadSeconds;
        highSpeedSteerRate = other.highSpeedSteerRate;
        steerReturnRate = other.steerReturnRate;
        antiRollForce = other.antiRollForce;
        rearBrakeShare = other.rearBrakeShare;
        topSpeedTaper = other.topSpeedTaper;
        steerSlipAllowance = other.steerSlipAllowance;
        sideSlipDamping = other.sideSlipDamping;
        bounceOnContact = other.bounceOnContact;
        carBounce = other.carBounce;
        wallBounce = other.wallBounce;
        minSeparationSpeed = other.minSeparationSpeed;
        maxBounceSpeed = other.maxBounceSpeed;
        bounceCooldown = other.bounceCooldown;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRegistry() => all.Clear();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.centerOfMass = centerOfMass;
        body.solverIterations = Mathf.Max(body.solverIterations, 12);
        body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 4);
        body.maxDepenetrationVelocity = 3f;
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
            // Keep the imported mesh rotation and any off-centre pivot.
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
        BuildAxles();
        baseSidewaysStiffness = Array.ConvertAll(wheels, wheel => wheel.collider.sidewaysFriction.stiffness);
        baseForwardStiffness = Array.ConvertAll(wheels, wheel => wheel.collider.forwardFriction.stiffness);
        int barrierLayer = LayerMask.NameToLayer("TrackBarrier");
        foreach (Wheel wheel in wheels)
        {
            // Suspension rays must not land on wall tops, or the spring launches the car.
            if (barrierLayer >= 0)
                wheel.collider.excludeLayers |= 1 << barrierLayer;
            wheel.collider.ConfigureVehicleSubsteps(5f, 8, 12);
            wheel.collider.forceAppPointDistance = 0.1f;
        }
        ConfigureChassisContacts();
        barrierBodyId = body.GetInstanceID();
        foreach (Collider shape in GetComponentsInChildren<Collider>(true))
        {
            if (shape is WheelCollider || shape.isTrigger || shape.attachedRigidbody != body)
                continue;
            modifiableContacts.Add(shape, shape.hasModifiableContacts);
            shape.hasModifiableContacts = true;
        }
    }

    private void OnEnable()
    {
        if (!all.Contains(this))
            all.Add(this);
    }

    private void Start()
    {
        if (showControls && !ExternalInput && RaceManager.Instance == null)
            DrivingControlsHUD.CreateStandalone(transform);
    }

    // Front and rear axles as (left, right) wheel indices.
    private void BuildAxles()
    {
        axles.Clear();
        var front = new List<int>();
        var rear = new List<int>();
        for (int i = 0; i < wheels.Length; i++)
            (wheels[i].steering ? front : rear).Add(i);
        foreach (var pair in new[] { front, rear })
        {
            if (pair.Count != 2)
                continue;
            float x0 = transform.InverseTransformPoint(wheels[pair[0]].collider.transform.position).x;
            float x1 = transform.InverseTransformPoint(wheels[pair[1]].collider.transform.position).x;
            axles.Add(x0 <= x1 ? new Vector2Int(pair[0], pair[1]) : new Vector2Int(pair[1], pair[0]));
        }
    }

    private void Update()
    {
        if (!CanDrive || ExternalInput)
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
        if (IsStopped)
        {
            SlowToStop();
            return;
        }
        if (!CanDrive)
            return;
        if (resetRequested)
        {
            Respawn();
            return;
        }

        float throttleRate = Mathf.Abs(throttleInput) > Mathf.Abs(throttle) ? throttleRise : throttleFall;
        throttle = Mathf.MoveTowards(throttle, throttleInput, throttleRate * Time.fixedDeltaTime);
        float power = powerByFuel.Evaluate(FuelFraction);
        if (FuelFraction < sputterBelowFuel && Mathf.PerlinNoise(Time.time * 4f, 0) < 0.35f)
            power = 0;

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        // Pressing against the direction of travel brakes.
        bool applyBrake = braking || throttleInput * forwardSpeed < -0.5f;
        IsBraking = applyBrake;

        float limit = (throttle >= 0 ? topSpeedKmh * power : reverseSpeedKmh) / 3.6f;
        float taper = topSpeedTaper > 0f
            ? Mathf.Clamp01((limit - Mathf.Abs(forwardSpeed)) / Mathf.Max(limit * topSpeedTaper, 0.01f))
            : (Mathf.Abs(forwardSpeed) >= limit ? 0f : 1f);
        float torque = applyBrake || !EngineRunning ? 0 : throttle * motorTorque * power * taper;
        float idleBrake = !EngineRunning ? engineOffDrag : Mathf.Abs(throttleInput) < 0.01f ? 40f : 0;
        if (ApplyCornerAssist(forwardSpeed, power, applyBrake))
            torque = 0;

        float speedFraction = Mathf.Clamp01(SpeedKmh / topSpeedKmh);
        float planarSpeedSquared = Vector3.ProjectOnPlane(body.linearVelocity, transform.up).sqrMagnitude;
        float topSpeed = topSpeedKmh / 3.6f;
        float overspeed = holdTurnRadiusAboveTopSpeed
            ? Mathf.Clamp(planarSpeedSquared / (topSpeed * topSpeed), 1f, maxBoostGrip) : 1f;
        // Bicycle model: lateral accel = v² · tan(angle) / wheelbase.
        float safeAngle = Mathf.Atan(wheelbase * maxCorneringAcceleration * overspeed /
            Mathf.Max(planarSpeedSquared, 0.01f)) * Mathf.Rad2Deg;
        float steeringLimit = Mathf.Min(Mathf.Lerp(steeringAngle, steeringAngle * 0.4f, speedFraction),
            safeAngle * steerSlipAllowance);

        float inputRate = steeringResponse / Mathf.Max(steeringAngle, 1f);
        if (!ExternalInput)
        {
            inputRate *= Mathf.Lerp(1f, highSpeedSteerRate, speedFraction);
            bool returning = steering * steeringInputSmoothed < 0f
                || Mathf.Abs(steering) < Mathf.Abs(steeringInputSmoothed);
            if (returning)
                inputRate *= steerReturnRate;
        }
        steeringInputSmoothed = Mathf.MoveTowards(steeringInputSmoothed, steering, inputRate * Time.fixedDeltaTime);
        currentSteering = steeringInputSmoothed * steeringLimit;

        GroundedWheelCount = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            Wheel wheel = wheels[i];
            wheel.collider.steerAngle = wheel.steering ? currentSteering : 0;
            wheel.collider.motorTorque = torque;
            wheel.collider.brakeTorque = applyBrake ? brakeTorque * (wheel.steering ? 1f : rearBrakeShare) : idleBrake;
            WheelFrictionCurve sideways = wheel.collider.sidewaysFriction;
            sideways.stiffness = baseSidewaysStiffness[i] * tireGrip * overspeed;
            wheel.collider.sidewaysFriction = sideways;
            WheelFrictionCurve forward = wheel.collider.forwardFriction;
            forward.stiffness = baseForwardStiffness[i] * tireGrip;
            wheel.collider.forwardFriction = forward;
            if (wheel.collider.isGrounded)
                GroundedWheelCount++;
        }
        VehicleBarrierContacts.UpdateCar(barrierBodyId, body.worldCenterOfMass.y,
            GroundedWheelCount >= 2 && Vector3.Dot(transform.up, Vector3.up) > 0.7f);
        ApplyGroundedStability(forwardSpeed, planarSpeedSquared, topSpeed);
        ApplyAntiRoll();
    }

    private void Respawn()
    {
        ShortcutTracker.Exit(body);
        IsBraking = false;
        body.position = spawnPosition + Vector3.up * 0.5f;
        body.rotation = spawnRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        currentSteering = steeringInputSmoothed = 0;
        lastVehicleContact = float.NegativeInfinity;
        throttle = 0;
        resetRequested = false;
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.motorTorque = 0;
            wheel.collider.brakeTorque = brakeTorque;
            wheel.collider.steerAngle = 0;
        }
        Respawned?.Invoke();
    }

    private void ApplyAntiRoll()
    {
        if (antiRollForce <= 0f)
            return;
        foreach (Vector2Int axle in axles)
        {
            WheelCollider left = wheels[axle.x].collider;
            WheelCollider right = wheels[axle.y].collider;
            bool leftGrounded = left.GetGroundHit(out WheelHit leftHit);
            bool rightGrounded = right.GetGroundHit(out WheelHit rightHit);
            float leftTravel = leftGrounded ? SuspensionTravel(left, leftHit) : 1f;
            float rightTravel = rightGrounded ? SuspensionTravel(right, rightHit) : 1f;
            float force = (leftTravel - rightTravel) * antiRollForce;
            if (leftGrounded)
                body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (rightGrounded)
                body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }
    }

    // 0 = fully compressed, 1 = fully extended.
    private static float SuspensionTravel(WheelCollider wheel, WheelHit hit)
    {
        float hitY = wheel.transform.InverseTransformPoint(hit.point).y;
        return Mathf.Clamp01((wheel.center.y - hitY - wheel.radius) / Mathf.Max(wheel.suspensionDistance, 0.01f));
    }

    // While throttle and steering are held together, slow to a speed the bend ahead allows.
    // Returns true while slowing, so the engine gives no drive.
    private bool ApplyCornerAssist(float forwardSpeed, float power, bool applyBrake)
    {
        float sign = Mathf.Abs(steering) > 0.5f ? Mathf.Sign(steering) : 0f;
        steerHeldSeconds = sign != 0f && sign == steerHeldSign ? steerHeldSeconds + Time.fixedDeltaTime : 0f;
        steerHeldSign = sign;
        if (!cornerAssist || ExternalInput || applyBrake || throttleInput <= 0f || forwardSpeed <= 0f
            || GroundedWheelCount < 2)
            return false;
        float strength = Mathf.Clamp01((steerHeldSeconds - cornerAssistDelay) / cornerAssistRamp)
            * Mathf.Abs(steeringInputSmoothed);
        if (strength <= 0f)
            return false;
        float cornerSpeed = Mathf.Sqrt(maxCorneringAcceleration * AssistBendRadius(forwardSpeed, Mathf.Sign(steering)));
        if (forwardSpeed <= cornerSpeed)
            return false;
        float cap = Mathf.Lerp(Mathf.Max(topSpeedKmh * power / 3.6f, forwardSpeed), cornerSpeed, strength);
        if (forwardSpeed <= cap)
            return false;
        float slow = Mathf.Min(cornerAssistDecel, (forwardSpeed - cap) / Time.fixedDeltaTime);
        body.AddForce(-transform.forward * slow, ForceMode.Acceleration);
        return true;
    }

    // Tightest bend radius ahead in the steering direction, on the car's own line.
    // Infinity if there is no bend that way; cornerAssistRadius if the track can't be used.
    private float AssistBendRadius(float forwardSpeed, float steerSign)
    {
        if (!cornerAssistReadsTrack || ShortcutTracker.IsOnBranch(body))
            return cornerAssistRadius;
        if (!searchedAssistTrack)
        {
            searchedAssistTrack = true;
            assistTrack = FindFirstObjectByType<LoopTrackGenerator>();
        }
        if (assistTrack == null || assistTrack.centerline.Count < 4)
            return cornerAssistRadius;

        int count = assistTrack.centerline.Count;
        int near = NearestTrackPoint(assistTrack, count);
        Vector3 along = assistTrack.GetForward(near);
        along.y = 0f;
        Vector3 offset = body.position - assistTrack.GetPoint(near);
        offset.y = 0f;
        float lateral = Vector3.Dot(offset, new Vector3(-along.z, 0f, along.x));
        bool onRoad = Mathf.Abs(lateral) <= assistTrack.roadWidth * 0.5f + assistTrack.curbWidth + 2f;
        bool rightWay = Vector3.Dot(transform.forward, along.normalized) > 0.3f;
        if (!onRoad || !rightWay)
            return cornerAssistRadius;

        float lookAhead = Mathf.Max(15f, forwardSpeed * cornerAssistLookAheadSeconds);
        float tightest = float.PositiveInfinity;
        float travelled = 0f;
        int index = near;
        for (int step = 0; step < count && travelled < lookAhead; step++)
        {
            int next = index + 1;
            travelled += Vector3.Distance(assistTrack.GetPoint(index), assistTrack.GetPoint(next));
            Vector3 before = assistTrack.GetForward(next - 1);
            Vector3 after = assistTrack.GetForward(next + 1);
            before.y = 0f;
            after.y = 0f;
            float angle = Vector3.Angle(before, after) * Mathf.Deg2Rad;
            float turn = Mathf.Sign(Vector3.Cross(before, after).y); // +1 = right-hand bend
            if (angle > 0.001f && turn == steerSign)
            {
                float bend = Vector3.Distance(assistTrack.GetPoint(next - 1), assistTrack.GetPoint(next + 1)) / angle;
                tightest = Mathf.Min(tightest, Mathf.Max((bend + lateral * turn) * cornerAssistBendMargin, 3f));
            }
            index = next;
        }
        return tightest;
    }

    // Local search around the last point; full search if the car has jumped far away.
    private int NearestTrackPoint(LoopTrackGenerator track, int count)
    {
        int best = FindNearestTrackPoint(track, count, assistTrackIndex < 0 ? 0 : assistTrackIndex - 5,
            assistTrackIndex < 0 ? count : Mathf.Min(26, count), out float distance);
        if (assistTrackIndex >= 0 && distance > 15f * 15f)
            best = FindNearestTrackPoint(track, count, 0, count, out distance);
        assistTrackIndex = best;
        return best;
    }

    private int FindNearestTrackPoint(LoopTrackGenerator track, int count, int from, int span, out float bestDistance)
    {
        bestDistance = float.MaxValue;
        int best = 0;
        for (int i = 0; i < span; i++)
        {
            int index = ((from + i) % count + count) % count;
            Vector3 difference = track.GetPoint(index) - body.position;
            difference.y = 0f;
            if (difference.sqrMagnitude < bestDistance)
            {
                bestDistance = difference.sqrMagnitude;
                best = index;
            }
        }
        return best;
    }

    private void ApplyGroundedStability(float forwardSpeed, float speedSquared, float topSpeed)
    {
        if (GroundedWheelCount < 2)
            return;
        float contactFraction = GroundedWheelCount / (float)wheels.Length;
        float aero = Mathf.Clamp(speedSquared / (topSpeed * topSpeed), 0f, maxBoostGrip);
        body.AddForce(-transform.up * (downforceAcceleration * aero * contactFraction), ForceMode.Acceleration);

        // Damp only yaw beyond what the current steering asks for.
        float desiredYaw = forwardSpeed * Mathf.Tan(currentSteering * Mathf.Deg2Rad) / wheelbase;
        float yaw = Vector3.Dot(body.angularVelocity, transform.up);
        float excessYaw = yaw - Mathf.Clamp(desiredYaw, Mathf.Min(0f, yaw), Mathf.Max(0f, yaw));
        body.AddTorque(-transform.up * (excessYaw * yawStability * contactFraction), ForceMode.Acceleration);

        // Skipped right after a hit so bounces still carry.
        if (sideSlipDamping > 0f && !HasVehicleContact && Time.fixedTime - lastBounceTime > 0.4f)
        {
            Vector3 sideways = Vector3.Project(body.linearVelocity, transform.right);
            body.AddForce(-sideways * (sideSlipDamping * contactFraction), ForceMode.Acceleration);
        }
    }

    private void ConfigureChassisContacts()
    {
        if (!slidingChassisContacts)
            return;
        chassisMaterial = new PhysicsMaterial("Sliding vehicle chassis")
        {
            staticFriction = 0f,
            dynamicFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        foreach (Collider shape in GetComponentsInChildren<Collider>(true))
        {
            if (shape is WheelCollider || shape.isTrigger || shape.attachedRigidbody != body)
                continue;
            chassisMaterials.Add(shape, shape.sharedMaterial);
            shape.sharedMaterial = chassisMaterial;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TrackVehicleContact(collision);
        BounceOffContact(collision, true);
    }

    private void OnCollisionStay(Collision collision)
    {
        TrackVehicleContact(collision);
        BounceOffContact(collision, false);
    }

    // Pushes the car away from a wall or car it hit sideways. Each car in a car-car
    // contact takes half the push.
    private void BounceOffContact(Collision collision, bool firstTouch)
    {
        if (!bounceOnContact || body == null || body.isKinematic || IsCountdownHeld || IsStopped)
            return;
        if (!firstTouch && Time.fixedTime - lastBounceTime < bounceCooldown)
            return;
        int contactCount = collision.contactCount;
        if (contactCount == 0)
            return;

        Rigidbody other = collision.rigidbody;
        CarDriver otherCar = other != null ? other.GetComponent<CarDriver>() : null;
        bool hitCar = otherCar != null;
        if (!hitCar && other != null && !other.isKinematic)
            return;

        Vector3 normal = Vector3.zero;
        Vector3 point = Vector3.zero;
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            normal += contact.normal;
            point += contact.point;
        }
        point /= contactCount;
        if (normal.sqrMagnitude < 0.0001f)
            return;
        normal.Normalize();
        if (Mathf.Abs(normal.y) > (hitCar ? 0.6f : 0.35f))
            return;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.0001f)
            return;
        normal.Normalize();
        Vector3 toSelf = body.worldCenterOfMass - point;
        toSelf.y = 0f;
        if (Vector3.Dot(normal, toSelf) < 0f)
            normal = -normal;

        Vector3 relative = body.GetPointVelocity(point) - (other != null ? other.GetPointVelocity(point) : Vector3.zero);
        float separating = Vector3.Dot(relative, normal);
        float impact = firstTouch ? Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) : 0f;
        float wanted = Mathf.Min(Mathf.Max(impact * (hitCar ? carBounce : wallBounce), minSeparationSpeed), maxBounceSpeed);
        float needed = wanted - separating;
        if (needed <= 0f)
            return;
        bool otherPushes = hitCar && otherCar.bounceOnContact && otherCar.body != null && !otherCar.body.isKinematic
            && !otherCar.IsCountdownHeld && !otherCar.IsStopped;
        body.AddForce(normal * needed * (otherPushes ? 0.5f : 1f), ForceMode.VelocityChange);
        lastBounceTime = Time.fixedTime;
    }

    private void TrackVehicleContact(Collision collision)
    {
        if (collision.rigidbody != null && collision.rigidbody.GetComponent<CarDriver>() != null)
            lastVehicleContact = Time.fixedTime;
    }

    private void OnDestroy()
    {
        VehicleBarrierContacts.RemoveCar(barrierBodyId);
        foreach (var entry in modifiableContacts)
            if (entry.Key != null)
                entry.Key.hasModifiableContacts = entry.Value;
        foreach (var entry in chassisMaterials)
            if (entry.Key != null && entry.Key.sharedMaterial == chassisMaterial)
                entry.Key.sharedMaterial = entry.Value;
        if (chassisMaterial != null)
        {
            if (Application.isPlaying) Destroy(chassisMaterial);
            else DestroyImmediate(chassisMaterial);
        }
    }

    private void LateUpdate()
    {
        if (IsCountdownHeld || (IsStopped && body.isKinematic))
            return;
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            wheel.mesh.SetPositionAndRotation(position + rotation * wheel.pivotOffset,
                rotation * wheel.rotationOffset);
        }
    }

    private void OnDisable()
    {
        all.Remove(this);
        VehicleBarrierContacts.RemoveCar(barrierBodyId);
        IsBraking = false;
        foreach (Wheel wheel in wheels)
        {
            if (wheel?.collider == null)
                continue;
            wheel.collider.motorTorque = 0;
            wheel.collider.brakeTorque = brakeTorque;
        }
    }

}
