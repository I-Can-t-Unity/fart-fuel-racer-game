using System.Collections.Generic;
using UnityEngine;

// The AI rival. It now drives the same way the player's car does: its CarDriver turns the
// wheels and works the pedals through wheel physics, so a hit really pushes both cars around.
// This script supplies the steering and throttle instead of the keyboard, hunts the fuel cubes
// of its own fuel type, and gets help straightening out after a hit, so it recovers faster
// than the player, who gets no help. Which car it drives (Gasoline, Diesel or Ethanol) follows the
// car picked in the main menu: by default it takes one of the two the player didn't pick.
// The Broken Bridge: holding Super Fart at the split, it lets AIShortcutRouter steer it up the
// ramp and over the jump instead of taking the main road; without Super Fart it stays on the
// main road. Its stuck/off-track recovery leaves it alone on the branch and in the air.
// The old kinematic version is kept, commented out, at the bottom of this file.
// Old: [DefaultExecutionOrder(-10)] // set the pedals before CarDriver uses them
// Runs before ApplySelectedCar (-200) too, so it can see the player's original (Gasoline) body
// before the menu choice repaints it. Still before CarDriver, so the pedals are set in time.
[DefaultExecutionOrder(-300)]
[RequireComponent(typeof(Rigidbody))]
public class AICarWaypointDriver : MonoBehaviour
{
    [Header("Track")]
    public LoopTrackGenerator track;
    [Tooltip("The racing line, in metres to the side of the centre, used when the AI isn't going for fuel.")]
    public float laneOffset = 1.5f;
    // Old kinematic AI: held the car at this height above the road.
    // public float carHeight = 0.15f;

    [Header("AI Driving")]
    public float speedKph = 75f;
    public float cornerSpeedKph = 45f;
    // Old kinematic AI: turned the car directly at this many degrees per second.
    // public float steeringSpeed = 180f;
    public int lookAheadPoints = 8;
    [Tooltip("Angle to the aim point, in degrees, at which the AI turns the wheel all the way.")]
    public float fullSteerAtDegrees = 20f;
    [Tooltip("How far over its target speed, in km/h, the AI goes before braking instead of just lifting off.")]
    public float brakeOverKph = 6f;

    [Header("Same car as the player")]
    [Tooltip("Drive by the player's rules: the player's car handling (copied from its CarDriver at the start, so launch and top speed match), and no recovery help after hits. Off uses the handling and help below.")]
    public bool mimicPlayer = true;

    [Header("Handling (the rival's own car, only with Mimic Player off)")]
    [Tooltip("Gives the rival the handling below instead of the values on its CarDriver in the scene.")]
    public bool overrideHandling = true;
    [Tooltip("The player's car does 70.")]
    public float topSpeedKmh = 78f;
    [Tooltip("The player's car has 800, so the rival pulls away harder from slow corners.")]
    public float motorTorque = 1100f;
    [Tooltip("How fast the throttle rises to full, per second. The player's car has 3.")]
    public float throttleRise = 5f;
    [Tooltip("Cornering limit in metres per second squared. The player's car has 15.")]
    public float maxCorneringAcceleration = 15f;

    [Header("Looks")]
    [Tooltip("The car body. Leave empty to use the car model's main mesh.")]
    public Renderer bodyRenderer;
    public int bodyMaterialSlot = 0;
    [Tooltip("Paints the body with the cube material of the AI's own fuel, so its colour matches its fuel.")]
    public bool paintWithFuelColour = true;

    [Header("Car (from the main menu)")]
    [Tooltip("Pick the rival's car from the car chosen in the main menu. Off: it always runs on the Own Fuel set on AI Fuel Tank.")]
    public bool pickCarFromMenu = true;
    [Tooltip("Race the same car as the player instead of a different one. You then compete for the same fuel cubes.")]
    public bool sameCarAsPlayer = false;
    [Tooltip("With a different car: off picks one of the other two at random each race; on always takes the next one (Gasoline > Diesel > Ethanol > Gasoline).")]
    public bool alwaysNextCar = false;

    [Header("Fuel hunting")]
    [Tooltip("How far ahead, in track points, the AI looks for cubes of its colour.")]
    public int fuelSearchAheadPoints = 30;
    [Tooltip("Cubes closer than this, in track points, are too late to start swerving for.")]
    public int fuelMinAheadPoints = 4;
    [Tooltip("How fast the AI moves across to a fuel lane and back, in metres per second.")]
    public float laneChangeSpeed = 4f;
    [Tooltip("Outside Super Fart collecting, the AI only leaves the racing line for its own fuel below this fuel level.")]
    public float refuelBelowFuel = 80f;
    // Old: homing started by track points and aimed at the can's lane a few points past it. On a
    // bend that line cuts across the lane, so the AI reached the can one lane over.
    // [Tooltip("Once the can it wants is this many track points ahead or closer, the AI steers straight at the can's lane just past it, so it drives through the can instead of drifting past it.")]
    // public int homeOnCanWithinPoints = 10;
    // [Tooltip("How far past the can, in track points, the AI aims while homing, so it goes through the can straight.")]
    // public int aimPastCanPoints = 2;
    [Tooltip("Once the can it wants is this close, in metres, the AI steers straight at the can itself, so it drives through it instead of drifting past or clipping the can beside it.")]
    public float homeOnCanWithinMetres = 70f;
    [Tooltip("While homing it aims this many metres past the can, along the road at the can, so it goes through the can straight.")]
    public float aimPastCanMetres = 6f;
    [Tooltip("Only aims straight at a can when the road turns less than this many degrees between the car and the can. On a bend (the hairpin) a straight line would cut through the inside barrier, so it follows the curve in the can's lane instead.")]
    public float homeStraightBelowDegrees = 20f;
    [Tooltip("On a bend, how many track points past the can it aims, in the can's lane.")]
    public int aimPastCanPoints = 2;

    [Header("Collisions")]
    [Tooltip("Hits weaker than this, in km/h, are ignored so scraping alongside doesn't start a recovery.")]
    public float minImpactKph = 5f;
    // Old kinematic AI: physics couldn't move the car, so a hit was faked with a slowdown, a shove and a spin.
    // [Tooltip("Speed the AI loses per km/h of impact, so harder hits slow it more.")]
    // public float slowdownPerImpactKph = 1.5f;
    // [Tooltip("The most speed hits can take away at once, in km/h.")]
    // public float maxSlowdownKph = 40f;
    // [Tooltip("How fast the AI gets its speed back after a hit, in km/h per second.")]
    // // public float speedRecoveryKph = 20f;
    // public float speedRecoveryKph = 40f;
    // [Tooltip("How far a hit shoves the AI sideways, in metres per km/h of impact.")]
    // public float knockPerImpactKph = 0.08f;
    // [Tooltip("How quickly the shove dies away. Higher values settle faster.")]
    // // public float knockDamping = 4f;
    // public float knockDamping = 8f;
    // [Tooltip("Turn added by a hit, in degrees per km/h of impact; the steering pulls it straight again.")]
    // public float spinPerImpactKph = 1.5f;
    [Tooltip("How long after a hit the AI gets help recovering, in seconds. The player gets none.")]
    public float recoverySeconds = 1.2f;
    [Tooltip("How quickly spin from a hit dies away while recovering. Higher settles faster.")]
    public float spinDamping = 5f;
    [Tooltip("How quickly a sideways skid is cancelled while recovering. Higher grips sooner.")]
    public float slideDamping = 3f;
    [Tooltip("While recovering, the AI is turned back toward the track if it points more than this many degrees off.")]
    public float realignAboveDegrees = 15f;
    [Tooltip("How fast that turn back happens, in degrees per second.")]
    public float realignDegreesPerSecond = 90f;
    [Tooltip("Extra push while recovering, in metres per second squared, so the AI regains speed faster than the player.")]
    public float catchUpAcceleration = 4f;
    [Tooltip("If the AI is flipped, pinned against something or off the track this long, it is put back on the road.")]
    public float stuckSeconds = 2.5f;
    [Tooltip("Height above the road the AI is put back at, in metres.")]
    public float respawnHeight = 0.8f;
    [Tooltip("After the wheels leave the ground (the jump, a bump) or the AI is put back, stuck checks wait this long, in seconds.")]
    public float stuckGraceSeconds = 1.5f;

    [Header("Shortcuts")]
    [Tooltip("Lets the AI take the Super Fart jump when it holds Super Fart. Off: it always stays on the main road.")]
    public bool useShortcuts = true;

    private Rigidbody rb;
    private CarDriver driver;
    private AIFuelTank tank;
    private int currentWaypoint = -1;
    // Old kinematic AI:
    // private float speedPenalty;
    // private Vector3 knockVelocity;
    private float currentLane;
    private float recoverUntil = -1;
    private float stuckFor;
    private float nextPickupSearch;
    private FuelPickup chasing;
    private AIShortcutRouter router;
    private float graceUntil = -1;
    private int branchRespawns;
    private int lastRespawnPoint = -1, repeatRespawns;
    private float lastRespawnTime = -100f;
    private readonly List<FuelPickup> pickups = new List<FuelPickup>();
    private readonly Dictionary<FuelPickup, int> pickupWaypoints = new Dictionary<FuelPickup, int>();
    // The body of the car the rival drives, taken from the player's car options.
    private Material carBody;

    public bool IsRecovering => Time.time < recoverUntil;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Old kinematic AI: the copied player controls were switched off so R wouldn't reset the AI.
        // CarDriver playerControls = GetComponent<CarDriver>();
        // if (playerControls != null)
        //     playerControls.enabled = false;

        // The AI car was copied from the player car, so it has the same CarDriver and wheels.
        // Drive with them, taking the pedals from this script instead of the keys (so R can't reset it).
        driver = GetComponent<CarDriver>();
        if (driver == null)
        {
            Debug.LogError("The AI car needs a CarDriver with its four wheels set up.", this);
            enabled = false;
            return;
        }
        driver.ExternalInput = true;
        driver.enabled = true;
        // Old: the rival always had its own, faster handling (78 km/h, 1100 torque, quicker throttle).
        // if (overrideHandling)
        //     driver.Tune(topSpeedKmh, motorTorque, throttleRise, maxCorneringAcceleration);
        FuelInventory player = mimicPlayer ? FindFirstObjectByType<FuelInventory>() : null;
        CarDriver playerDriver = player != null ? player.GetComponent<CarDriver>() : null;
        if (playerDriver != null)
            driver.CopyHandlingFrom(playerDriver);
        else if (overrideHandling)
            driver.Tune(topSpeedKmh, motorTorque, throttleRise, maxCorneringAcceleration);

        tank = GetComponent<AIFuelTank>();
        if (tank == null)
            tank = gameObject.AddComponent<AIFuelTank>();

        if (pickCarFromMenu)
            ChooseCar();

        currentLane = laneOffset;

        // Added after the tank, so the router finds the tank as this car's fuel-mix holder.
        if (useShortcuts)
        {
            router = GetComponent<AIShortcutRouter>();
            if (router == null)
                router = gameObject.AddComponent<AIShortcutRouter>();
            router.rb = rb;
            router.RejoinedMainLine += OnRejoinedMainLine;
            router.RecoveredToMainLine += OnRecoveredToMainLine;
        }
    }

    void OnDestroy()
    {
        if (router == null)
            return;
        router.RejoinedMainLine -= OnRejoinedMainLine;
        router.RecoveredToMainLine -= OnRecoveredToMainLine;
    }

    // Through the exit gate: carry on along the main road from the merge.
    void OnRejoinedMainLine(int index)
    {
        currentWaypoint = index;
        currentLane = laneOffset;
        stuckFor = 0f;
        branchRespawns = 0;
        graceUntil = Time.time + stuckGraceSeconds;
    }

    // Missed the jump (the fail zone already put the car back before the split) or gave up
    // on the shortcut: carry on along the main road from there.
    void OnRecoveredToMainLine(int index)
    {
        if (index >= 0)
            currentWaypoint = index;
        currentLane = laneOffset;
        chasing = null;
        stuckFor = 0f;
        recoverUntil = -1;
        branchRespawns = 0;
        graceUntil = Time.time + stuckGraceSeconds;
    }

    // Uses the same saved choice as ApplySelectedCar (0 = Gasoline, 1 = Diesel, 2 = Ethanol),
    // gives the rival a car to match, and remembers that car's body paint.
    void ChooseCar()
    {
        int carCount = System.Enum.GetValues(typeof(FuelType)).Length;
        int player = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCarFuel", 0), 0, carCount - 1);

        int rival = player;
        if (!sameCarAsPlayer)
            rival = (player + (alwaysNextCar ? 1 : Random.Range(1, carCount))) % carCount;
        tank.ownFuel = (FuelType)rival;

        // The player's car still has its original Gasoline body here, since this runs before
        // ApplySelectedCar repaints it; Diesel and Ethanol bodies come from ApplySelectedCar.
        FuelInventory playerBag = FindFirstObjectByType<FuelInventory>();
        ApplySelectedCar playerCar = playerBag != null ? playerBag.GetComponent<ApplySelectedCar>() : null;
        switch (tank.ownFuel)
        {
            case FuelType.Gasoline:
                if (playerBag != null && playerBag.bodyRenderer != null &&
                    playerBag.bodyMaterialSlot >= 0 && playerBag.bodyMaterialSlot < playerBag.bodyRenderer.sharedMaterials.Length)
                    carBody = playerBag.bodyRenderer.sharedMaterials[playerBag.bodyMaterialSlot];
                break;
            case FuelType.Diesel:
                carBody = playerCar != null ? playerCar.dieselBody : null;
                break;
            case FuelType.Ethanol:
                carBody = playerCar != null ? playerCar.ethanolBody : null;
                break;
        }
    }

    void Start()
    {
        // Old kinematic AI: physics didn't move the car; the script placed it every step.
        // rb.isKinematic = true;
        rb.isKinematic = false;

        if (track == null)
            track = FindFirstObjectByType<LoopTrackGenerator>();

        if (router != null)
        {
            router.track = track;
            FuelInventory playerBag = FindFirstObjectByType<FuelInventory>();
            if (playerBag != null)
                router.rival = playerBag.transform;
        }

        PaintBody();
    }

    void FixedUpdate()
    {
        if (!driver.CanDrive)
            return;
        if (track == null || track.centerline.Count == 0)
            return;

        int count = track.centerline.Count;
        // Old: always tracked the nearest centre-line point.
        // UpdateWaypoint(count);
        // On the branch the nearest centre-line point is in the bypassed part of the main road,
        // so the track position is held at the split until the router hands back the merge.
        if (router == null || !router.IsFollowingBranch)
            UpdateWaypoint(count);

        if (router != null)
        {
            // Committed or on the branch: the tank must keep its Super Fart for the entry gate.
            tank.usingShortcut = router.IsUsingShortcut;
            if (router.TryGetOverride(currentWaypoint, out AIShortcutRouter.SteerCommand command))
            {
                DriveShortcut(command);
                return;
            }
        }

        // How sharp the road ahead is: 0 on a straight, 1 for a 60 degree bend or more.
        int targetIndex = currentWaypoint + lookAheadPoints;
        float turnAngle = Vector3.Angle(track.GetForward(currentWaypoint), track.GetForward(targetIndex + 6));
        float cornerFactor = Mathf.Clamp01(turnAngle / 60f);
        tank.cornerAhead = cornerFactor;

        // Slide across to the lane of the next cube of its fuel, or back to the racing line.
        float targetLane = FindFuelLane(count, out float fuelLane) ? fuelLane : laneOffset;
        currentLane = Mathf.MoveTowards(currentLane, targetLane, laneChangeSpeed * Time.fixedDeltaTime);

        // Steer toward a point ahead on that lane, the way a player would.
        // Old: always aimed Look Ahead Points ahead, so the car only drifted slowly toward a can's
        // lane and was still a metre or two off when it got there, missing the can or clipping
        // the one beside it.
        // Vector3 direction = track.GetForward(targetIndex);
        // Vector3 side = new Vector3(-direction.z, 0f, direction.x);
        // Vector3 target = track.GetPoint(targetIndex) + side * currentLane;
        // Old (first homing version): aimed at the can's lane a few track points past it.
        // int aimIndex = targetIndex;
        // float aimLane = currentLane;
        // if (chasing != null && AheadOf(chasing, count) <= homeOnCanWithinPoints)
        // {
        //     aimIndex = pickupWaypoints[chasing] + aimPastCanPoints;
        //     aimLane = fuelLane;
        //     currentLane = fuelLane;
        // }
        // Vector3 direction = track.GetForward(aimIndex);
        // Vector3 side = new Vector3(-direction.z, 0f, direction.x);
        // Vector3 target = track.GetPoint(aimIndex) + side * aimLane;
        Vector3 direction = track.GetForward(targetIndex);
        Vector3 side = new Vector3(-direction.z, 0f, direction.x);
        Vector3 target = track.GetPoint(targetIndex) + side * currentLane;
        if (chasing != null)
        {
            Vector3 can = chasing.transform.position;
            Vector3 toCan = can - rb.position;
            toCan.y = 0f;
            // Old: aimed straight at the can within Home On Can Within Metres everywhere. In the
            // hairpin that line went through the inside barrier, so the AI hit it again and again.
            // if (toCan.magnitude <= homeOnCanWithinMetres)
            // {
            //     target = can + track.GetForward(pickupWaypoints[chasing]) * aimPastCanMetres;
            //     currentLane = fuelLane;
            // }
            int canIndex = pickupWaypoints[chasing];
            float roadTurn = Vector3.Angle(track.GetForward(currentWaypoint), track.GetForward(canIndex));
            if (toCan.magnitude <= homeOnCanWithinMetres && roadTurn < homeStraightBelowDegrees)
            {
                // Close to the can on a straight: aim at the can itself, a little further along the road there.
                target = can + track.GetForward(canIndex) * aimPastCanMetres;
                currentLane = fuelLane;
            }
            else if (AheadOf(chasing, count) <= lookAheadPoints)
            {
                // Close to the can on a bend: follow the road, in the can's lane, to just past the can.
                int aimIndex = canIndex + aimPastCanPoints;
                Vector3 aimForward = track.GetForward(aimIndex);
                target = track.GetPoint(aimIndex) + new Vector3(-aimForward.z, 0f, aimForward.x) * fuelLane;
                currentLane = fuelLane;
            }
        }
        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float steer = Mathf.Clamp(Vector3.SignedAngle(forward, toTarget, Vector3.up) / fullSteerAtDegrees, -1f, 1f);

        // Throttle up to the target speed, lift off a little over it and brake well over it.
        float desiredSpeed = Mathf.Lerp(speedKph, cornerSpeedKph, cornerFactor);
        float speed = Vector3.Dot(rb.linearVelocity, forward) * 3.6f;
        float gap = desiredSpeed - speed;
        float throttle = gap > 0f ? Mathf.Clamp01(gap / 10f + 0.3f) : 0f;
        bool brake = gap < -brakeOverKph;

        // Don't waste a fart boost by braking against it, unless a sharp corner is coming.
        if (tank.Fart.IsFarting && cornerFactor < 0.5f)
        {
            throttle = 1f;
            brake = false;
        }

        driver.SetInput(throttle, steer, brake);

        Recover(forward, toTarget.normalized, desiredSpeed, speed);
        CheckStuck(speed, throttle);
    }

    // On the shortcut: steer at the router's target at its speed, flat out up the ramp and
    // wheels straight in the air. The racing line and fuel hunting wait until the merge.
    void DriveShortcut(AIShortcutRouter.SteerCommand command)
    {
        chasing = null;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 toTarget = command.target - rb.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            toTarget = forward;

        float angle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        float steer = command.lockSteering ? 0f : Mathf.Clamp(angle / fullSteerAtDegrees, -1f, 1f);
        tank.cornerAhead = Mathf.Clamp01(Mathf.Abs(angle) / 60f);

        float desiredSpeed = command.targetSpeed > 0f ? command.targetSpeed * 3.6f : speedKph;
        float speed = Vector3.Dot(rb.linearVelocity, forward) * 3.6f;
        float gap = desiredSpeed - speed;
        float throttle = gap > 0f ? Mathf.Clamp01(gap / 10f + 0.3f) : 0f;
        bool brake = gap < -brakeOverKph;
        if (command.fullThrottle)
        {
            throttle = 1f;
            brake = false;
        }
        if (command.lockSteering)
        {
            // In the air: no pedals, so it lands the way it took off.
            throttle = 0f;
            brake = false;
        }

        driver.SetInput(throttle, steer, brake);
        Recover(forward, toTarget.normalized, desiredSpeed, speed);
        CheckStuck(speed, throttle);
    }

    // A real hit now moves the AI car. For a moment afterwards it gets help the player doesn't:
    // spin and sideways skid die away faster, it is turned back toward the track, and it gets an
    // extra push back up to speed.
    void Recover(Vector3 forward, Vector3 aimDirection, float desiredSpeed, float speed)
    {
        // The player gets no help after a hit, so with Mimic Player the AI doesn't either.
        if (mimicPlayer)
            return;
        // While touching another car, let the contact solver separate both bodies.
        // Cancelling lateral velocity or rotating into an opponent can pin it in place.
        if (!IsRecovering || driver.HasVehicleContact || driver.GroundedWheelCount < 2)
            return;

        float yaw = Vector3.Dot(rb.angularVelocity, Vector3.up);
        rb.AddTorque(-Vector3.up * yaw * spinDamping, ForceMode.Acceleration);

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float slide = Vector3.Dot(rb.linearVelocity, right);
        rb.AddForce(-right * Mathf.Clamp(slide * slideDamping, -6f, 6f), ForceMode.Acceleration);

        float headingError = Vector3.SignedAngle(forward, aimDirection, Vector3.up);
        if (Mathf.Abs(headingError) > realignAboveDegrees)
        {
            float desiredYaw = Mathf.Clamp(headingError, -realignDegreesPerSecond, realignDegreesPerSecond)
                * Mathf.Deg2Rad;
            rb.AddTorque(Vector3.up * Mathf.Clamp((desiredYaw - yaw) * 2f, -3f, 3f), ForceMode.Acceleration);
        }

        if (speed < desiredSpeed)
            rb.AddForce(forward * catchUpAcceleration, ForceMode.Acceleration);
    }

    // With real physics the AI can end up on its roof or wedged against a wall. The player has R
    // for that; the AI puts itself back on the road at the point it had reached.
    // Old: the same checks everywhere, so the branch (off the main road) and the jump (wheels
    // off the ground) looked like being stuck and the AI was pulled back mid-shortcut.
    // void CheckStuck(float speed, float throttle)
    // {
    //     bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
    //     bool pinned = tank.fuel > 0f && throttle > 0.5f && Mathf.Abs(speed) < 3f;
    //     float maxSideways = track.roadWidth * 0.5f + track.curbWidth + 3f;
    //     bool offTrack = Mathf.Abs(LateralOffset(rb.position, currentWaypoint)) > maxSideways;
    //
    //     stuckFor = upsideDown || pinned || offTrack ? stuckFor + Time.fixedDeltaTime : 0f;
    //     if (stuckFor >= stuckSeconds)
    //         PutBackOnTrack();
    // }
    void CheckStuck(float speed, float throttle)
    {
        // No wheel on the ground: in the air (or a bump). Wait until it has landed a moment.
        if (driver.GroundedWheelCount == 0)
            graceUntil = Mathf.Max(graceUntil, Time.time + stuckGraceSeconds);
        if (Time.time < graceUntil && Vector3.Dot(transform.up, Vector3.up) >= 0.3f)
        {
            stuckFor = 0f;
            return;
        }

        bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
        bool pinned = tank.fuel > 0f && throttle > 0.5f && Mathf.Abs(speed) < 3f;
        // The branch leaves the main road on purpose, so "off the track" only applies on the main road.
        bool onShortcut = (router != null && router.IsFollowingBranch) || ShortcutTracker.IsOnBranch(rb);
        float maxSideways = track.roadWidth * 0.5f + track.curbWidth + 3f;
        bool offTrack = !onShortcut && Mathf.Abs(LateralOffset(rb.position, currentWaypoint)) > maxSideways;

        stuckFor = upsideDown || pinned || offTrack ? stuckFor + Time.fixedDeltaTime : 0f;
        if (stuckFor >= stuckSeconds)
        {
            if (tank.logFuelDecisions)
                Debug.Log($"[AI] Stuck ({(upsideDown ? "upside down" : pinned ? "pinned, not moving" : "off the track")}) " +
                          $"at track point {currentWaypoint}, fuel {tank.fuel:0}: putting it back" +
                          (onShortcut ? " on the shortcut." : " on the road."), this);
            if (onShortcut)
                PutBackOnShortcut();
            else
                PutBackOnTrack();
        }
    }

    // Counts a respawn at this track point; true if it's at the same spot as the last one within 10 s.
    bool RepeatRespawn(int waypoint)
    {
        int count = track.centerline.Count;
        int apart = Mathf.Min(((waypoint - lastRespawnPoint) % count + count) % count,
                              ((lastRespawnPoint - waypoint) % count + count) % count);
        bool repeat = lastRespawnPoint >= 0 && Time.time - lastRespawnTime < 10f && apart <= 6;
        repeatRespawns = repeat ? repeatRespawns + 1 : 0;
        lastRespawnPoint = waypoint;
        lastRespawnTime = Time.time;
        return repeat;
    }

    // Stuck on the branch: put back on the branch road behind it the first time; the second
    // time give up and go back before the split, like a missed jump, so it can't loop.
    void PutBackOnShortcut()
    {
        ShortcutBranch branch = router != null ? router.ActiveBranch : null;
        if (branch == null)
            ShortcutTracker.TryGetBranch(rb, out branch);
        branchRespawns++;

        if (branch != null && branchRespawns < 2
            && branch.GetSafeRespawnPose(rb.position, out Vector3 safePosition, out Quaternion safeRotation))
        {
            Place(safePosition, safeRotation);
            return;
        }

        if (branch != null && branch.failRespawnPoint != null)
        {
            Place(branch.failRespawnPoint.position, branch.failRespawnPoint.rotation);
            if (ShortcutTracker.IsOnBranch(rb))
                ShortcutTracker.RaiseFailed(rb, branch);   // the mix is already spent: a missed jump
            else if (router != null)
                OnRecoveredToMainLine(router.AbortShortcut());
            return;
        }

        if (router != null)
            router.AbortShortcut();
        ShortcutTracker.Exit(rb);
        PutBackOnTrack();
    }

    void Place(Vector3 position, Quaternion rotation)
    {
        stuckFor = 0f;
        recoverUntil = -1;
        currentLane = laneOffset;
        chasing = null;
        graceUntil = Time.time + stuckGraceSeconds;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    void PutBackOnTrack()
    {
        stuckFor = 0f;
        recoverUntil = -1;
        currentLane = laneOffset;
        chasing = null;
        // Put back at the same spot again: move it a few points further on each time, past
        // whatever keeps trapping it there.
        if (RepeatRespawn(currentWaypoint))
            currentWaypoint = (currentWaypoint + 5 * repeatRespawns) % track.centerline.Count;

        Vector3 forward = track.GetForward(currentWaypoint);
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);
        Vector3 position = track.GetPoint(currentWaypoint) + side * laneOffset;
        position.y = track.surfaceY + respawnHeight;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = Quaternion.LookRotation(forward, Vector3.up);
        // Give it a moment to get going before it can count as stuck again.
        graceUntil = Time.time + stuckGraceSeconds;
    }

    void OnCollisionEnter(Collision collision)
    {
        // The impulse is what this car felt, so this is how much its own speed changed.
        float impactKph = collision.impulse.magnitude / rb.mass * 3.6f;
        if (impactKph >= minImpactKph)
            recoverUntil = Time.time + recoverySeconds;
    }

    // Looks only near the last known point instead of the whole track, so a part of the
    // loop that passes close by can't make the AI think it has jumped there.
    void UpdateWaypoint(int count)
    {
        currentWaypoint = currentWaypoint < 0
            ? NearestPoint(rb.position, 0, count)
            : NearestPoint(rb.position, currentWaypoint - 5, 26);
    }

    int NearestPoint(Vector3 position, int from, int span)
    {
        int count = track.centerline.Count;
        float closestDistance = float.MaxValue;
        int closestIndex = 0;

        for (int i = 0; i < span; i++)
        {
            int index = ((from + i) % count + count) % count;
            Vector3 difference = track.GetPoint(index) - position;
            difference.y = 0f;

            float distance = difference.sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }
        return closestIndex;
    }

    // How far a point is to the side of the centreline, measured the same way the fuel spawner lays out its lanes.
    float LateralOffset(Vector3 position, int index)
    {
        Vector3 forward = track.GetForward(index);
        Vector3 across = new Vector3(-forward.z, 0f, forward.x);
        Vector3 offset = position - track.GetPoint(index);
        offset.y = 0f;
        return Vector3.Dot(offset, across);
    }

    // Picks the nearest cube of the AI's fuel ahead that is still on the track, and sticks with it until
    // it is taken or passed. Nothing to chase while the bag is full, since the cube would stay put.
    // With the jump split coming up it goes for its Super Fart fuel instead (two in a row make
    // Super Fart); if the player takes that can it picks the next one, or just keeps racing.
    bool FindFuelLane(int count, out float lane)
    {
        lane = laneOffset;
        if (tank.IsFull)
        {
            chasing = null;
            return false;
        }
        // Racing normally with plenty of fuel: stay on the racing line.
        if (!tank.WantsSuperFart && tank.fuel >= refuelBelowFuel)
        {
            chasing = null;
            return false;
        }

        RefreshPickups(count);

        if (chasing != null)
        {
            int ahead = AheadOf(chasing, count);
            // Old: if (chasing.IsAvailable && ahead <= fuelSearchAheadPoints)
            if (chasing.IsAvailable && ahead <= fuelSearchAheadPoints && chasing.type == WantedFuel())
            {
                lane = LateralOffset(chasing.transform.position, pickupWaypoints[chasing]);
                return true;
            }
            chasing = null;
        }

        int nearest = int.MaxValue;
        foreach (FuelPickup pickup in pickups)
        {
            // Old: only its own fuel.
            // if (pickup == null || !pickup.IsAvailable || pickup.type != tank.ownFuel)
            if (pickup == null || !pickup.IsAvailable || pickup.type != WantedFuel())
                continue;
            int ahead = AheadOf(pickup, count);
            if (ahead < fuelMinAheadPoints || ahead > fuelSearchAheadPoints || ahead >= nearest)
                continue;
            nearest = ahead;
            chasing = pickup;
        }

        if (chasing == null)
            return false;
        if (tank.logFuelDecisions)
            Debug.Log($"[AI] Going for the {chasing.type} can at track point {pickupWaypoints[chasing]} (row {chasing.row}, lane {chasing.lane}).", this);
        lane = LateralOffset(chasing.transform.position, pickupWaypoints[chasing]);
        return true;
    }

    // Old: FuelType WantedFuel() => tank.WantsSuperFart ? tank.SuperFartFuel : tank.ownFuel;
    // The tank decides, so what it hunts and what it accepts can't disagree.
    FuelType WantedFuel() => tank.WantedFuel;

    int AheadOf(FuelPickup pickup, int count)
    {
        return ((pickupWaypoints[pickup] - currentWaypoint) % count + count) % count;
    }

    // The spawner makes the cubes in its own Start, so keep looking until they exist.
    void RefreshPickups(int count)
    {
        if (pickups.Count > 0 || Time.time < nextPickupSearch)
            return;
        nextPickupSearch = Time.time + 1f;

        FuelPickupSpawner spawner = FindFirstObjectByType<FuelPickupSpawner>();
        if (spawner == null)
            return;

        foreach (FuelPickup pickup in spawner.GetComponentsInChildren<FuelPickup>())
        {
            pickups.Add(pickup);
            pickupWaypoints[pickup] = NearestPoint(pickup.transform.position, 0, count);
        }
    }

    // Uses the exact material of the AI's fuel cubes, the same way the player's fuel cubes
    // use the player's body material, so the colours always match.
    void PaintBody()
    {
        if (!paintWithFuelColour)
            return;

        FuelInventory playerBag = FindFirstObjectByType<FuelInventory>();
        Renderer body = bodyRenderer != null ? bodyRenderer : FindBodyRenderer();
        if (playerBag == null || body == null)
            return;

        Material[] materials = body.sharedMaterials;
        if (bodyMaterialSlot < 0 || bodyMaterialSlot >= materials.Length)
            return;
        // Old: always used the cube material of the AI's fuel.
        // materials[bodyMaterialSlot] = playerBag.MaterialOf(tank.ownFuel);
        // Now the body of the car picked for the rival, falling back to its fuel cube colour.
        materials[bodyMaterialSlot] = carBody != null ? carBody : playerBag.MaterialOf(tank.ownFuel);
        body.sharedMaterials = materials;
    }

    // The car model's body is the mesh with the most material slots (body, glass, lights...).
    Renderer FindBodyRenderer()
    {
        Renderer best = null;
        foreach (MeshRenderer candidate in GetComponentsInChildren<MeshRenderer>())
            if (best == null || candidate.sharedMaterials.Length > best.sharedMaterials.Length)
                best = candidate;
        return best;
    }
}

// ---------------------------------------------------------------------------------------
// Old kinematic AI, before it drove with wheel physics. Kept for reference, not compiled.
// ---------------------------------------------------------------------------------------
/*

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
*/
