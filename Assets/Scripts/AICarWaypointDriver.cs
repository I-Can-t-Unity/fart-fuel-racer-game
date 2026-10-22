using System.Collections.Generic;
using UnityEngine;

// The AI rival. It now drives the same way the player's car does: its CarDriver turns the
// wheels and works the pedals through wheel physics, so a hit really pushes both cars around.
// This script supplies the steering and throttle instead of the keyboard, hunts the fuel cubes
// of its own fuel type, and gets help straightening out after a hit, so it recovers faster
// than the player, who gets no help. Which car it drives (Gasoline, Diesel or Ethanol) follows the
// car picked in the main menu: by default it takes one of the two the player didn't pick.
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
    public float speedKph = 55f;
    public float cornerSpeedKph = 35f;
    // Old kinematic AI: turned the car directly at this many degrees per second.
    // public float steeringSpeed = 180f;
    public int lookAheadPoints = 8;
    [Tooltip("Angle to the aim point, in degrees, at which the AI turns the wheel all the way.")]
    public float fullSteerAtDegrees = 20f;
    [Tooltip("How far over its target speed, in km/h, the AI goes before braking instead of just lifting off.")]
    public float brakeOverKph = 6f;

    [Header("Handling (the rival's own car)")]
    [Tooltip("Gives the rival the handling below instead of the values on its CarDriver in the scene.")]
    public bool overrideHandling = true;
    [Tooltip("The player's car does 70.")]
    public float topSpeedKmh = 66f;
    [Tooltip("The player's car has 800, so the rival pulls away harder from slow corners.")]
    public float motorTorque = 950f;
    [Tooltip("How fast the throttle rises to full, per second. The player's car has 3.")]
    public float throttleRise = 5f;
    [Tooltip("Cornering limit in metres per second squared. The player's car has 15.")]
    public float maxCorneringAcceleration = 13f;

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
        if (overrideHandling)
            driver.Tune(topSpeedKmh, motorTorque, throttleRise, maxCorneringAcceleration);

        tank = GetComponent<AIFuelTank>();
        if (tank == null)
            tank = gameObject.AddComponent<AIFuelTank>();

        if (pickCarFromMenu)
            ChooseCar();

        currentLane = laneOffset;
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

        PaintBody();
    }

    void FixedUpdate()
    {
        if (track == null || track.centerline.Count == 0)
            return;

        int count = track.centerline.Count;
        UpdateWaypoint(count);

        // How sharp the road ahead is: 0 on a straight, 1 for a 60 degree bend or more.
        int targetIndex = currentWaypoint + lookAheadPoints;
        float turnAngle = Vector3.Angle(track.GetForward(currentWaypoint), track.GetForward(targetIndex + 6));
        float cornerFactor = Mathf.Clamp01(turnAngle / 60f);
        tank.cornerAhead = cornerFactor;

        // Slide across to the lane of the next cube of its fuel, or back to the racing line.
        float targetLane = FindFuelLane(count, out float fuelLane) ? fuelLane : laneOffset;
        currentLane = Mathf.MoveTowards(currentLane, targetLane, laneChangeSpeed * Time.fixedDeltaTime);

        // Steer toward a point ahead on that lane, the way a player would.
        Vector3 direction = track.GetForward(targetIndex);
        Vector3 side = new Vector3(-direction.z, 0f, direction.x);
        Vector3 target = track.GetPoint(targetIndex) + side * currentLane;
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

    // A real hit now moves the AI car. For a moment afterwards it gets help the player doesn't:
    // spin and sideways skid die away faster, it is turned back toward the track, and it gets an
    // extra push back up to speed.
    void Recover(Vector3 forward, Vector3 aimDirection, float desiredSpeed, float speed)
    {
        if (!IsRecovering)
            return;

        float dt = Time.fixedDeltaTime;
        rb.angularVelocity *= Mathf.Exp(-spinDamping * dt);

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 velocity = rb.linearVelocity;
        float slide = Vector3.Dot(velocity, right);
        velocity -= right * slide * (1f - Mathf.Exp(-slideDamping * dt));
        rb.linearVelocity = velocity;

        float headingError = Vector3.SignedAngle(forward, aimDirection, Vector3.up);
        if (Mathf.Abs(headingError) > realignAboveDegrees)
        {
            float step = Mathf.Clamp(headingError, -realignDegreesPerSecond * dt, realignDegreesPerSecond * dt);
            rb.MoveRotation(Quaternion.AngleAxis(step, Vector3.up) * rb.rotation);
        }

        if (speed < desiredSpeed)
            rb.AddForce(forward * catchUpAcceleration, ForceMode.Acceleration);
    }

    // With real physics the AI can end up on its roof or wedged against a wall. The player has R
    // for that; the AI puts itself back on the road at the point it had reached.
    void CheckStuck(float speed, float throttle)
    {
        bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
        bool pinned = tank.fuel > 0f && throttle > 0.5f && Mathf.Abs(speed) < 3f;
        float maxSideways = track.roadWidth * 0.5f + track.curbWidth + 3f;
        bool offTrack = Mathf.Abs(LateralOffset(rb.position, currentWaypoint)) > maxSideways;

        stuckFor = upsideDown || pinned || offTrack ? stuckFor + Time.fixedDeltaTime : 0f;
        if (stuckFor >= stuckSeconds)
            PutBackOnTrack();
    }

    void PutBackOnTrack()
    {
        stuckFor = 0f;
        recoverUntil = -1;
        currentLane = laneOffset;
        chasing = null;

        Vector3 forward = track.GetForward(currentWaypoint);
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);
        Vector3 position = track.GetPoint(currentWaypoint) + side * laneOffset;
        position.y = track.surfaceY + respawnHeight;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = Quaternion.LookRotation(forward, Vector3.up);
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
    bool FindFuelLane(int count, out float lane)
    {
        lane = laneOffset;
        if (tank.IsFull)
        {
            chasing = null;
            return false;
        }

        RefreshPickups(count);

        if (chasing != null)
        {
            int ahead = AheadOf(chasing, count);
            if (chasing.IsAvailable && ahead <= fuelSearchAheadPoints)
            {
                lane = LateralOffset(chasing.transform.position, pickupWaypoints[chasing]);
                return true;
            }
            chasing = null;
        }

        int nearest = int.MaxValue;
        foreach (FuelPickup pickup in pickups)
        {
            if (pickup == null || !pickup.IsAvailable || pickup.type != tank.ownFuel)
                continue;
            int ahead = AheadOf(pickup, count);
            if (ahead < fuelMinAheadPoints || ahead > fuelSearchAheadPoints || ahead >= nearest)
                continue;
            nearest = ahead;
            chasing = pickup;
        }

        if (chasing == null)
            return false;
        lane = LateralOffset(chasing.transform.position, pickupWaypoints[chasing]);
        return true;
    }

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

    // Uses the exact material of the AI's fuel cubes, the same way the player's red cubes
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
