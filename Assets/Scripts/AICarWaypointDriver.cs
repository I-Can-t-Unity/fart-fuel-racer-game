using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
[RequireComponent(typeof(Rigidbody))]
public class AICarWaypointDriver : MonoBehaviour
{
    [Header("Track")]
    public LoopTrackGenerator track;
    [Tooltip("The racing line, in metres to the side of the centre.")]
    public float laneOffset = 1.5f;

    [Header("AI Driving")]
    [Tooltip("Target speed on straights, in km/h. Keep it a little under the car's top speed so catch-up has room.")]
    public float speedKph = 80f;
    [Tooltip("Share of the car's cornering limit the AI uses in corners. Below 1 leaves a safety margin.")]
    [Range(0.5f, 1f)] public float cornerSpeedMargin = 0.9f;
    [Tooltip("Braking the AI plans on before a corner, in metres per second squared.")]
    public float cornerBrakeDecel = 7f;
    [Tooltip("How far ahead the AI aims when standing still, in metres.")]
    public float lookAheadMinMetres = 10f;
    [Tooltip("Extra aim distance for every m/s of speed, in seconds. Further ahead at speed keeps the line smooth; closer in slow corners stops it cutting them.")]
    public float lookAheadSeconds = 0.9f;
    [Tooltip("How far past the aim point it reads the road for corners, per m/s of speed, in seconds, so it slows down before the bend instead of in it.")]
    public float cornerPreviewSeconds = 1.6f;
    [Tooltip("Angle to the aim point, in degrees, at which the AI turns the wheel all the way. Higher is gentler.")]
    public float fullSteerAtDegrees = 30f;
    [Tooltip("Steering damping against how fast the car is already turning, in seconds. Higher stops weaving; too high makes it turn in late.")]
    public float steerDamping = 0.12f;
    [Tooltip("How fast the target speed may change, in km/h per second, so it eases into and out of corners.")]
    public float speedRampKph = 40f;
    [Tooltip("Throttle that holds a steady speed. More is added the further below target it is.")]
    public float cruiseThrottle = 0.35f;
    [Tooltip("How far below target, in km/h, it goes before using full throttle.")]
    public float throttleBandKph = 8f;
    [Tooltip("How far over its target speed, in km/h, the AI goes before braking instead of just lifting off.")]
    public float brakeOverKph = 10f;

    [Header("Colour cubes")]
    [Tooltip("Drives through cubes: its own colour while it leads, the leader's colour while it is 2nd or 3rd.")]
    public bool huntCubes = true;
    [Tooltip("How far ahead along the road, in metres, it looks for a cube to go for.")]
    public float cubeSearchMetres = 120f;
    [Tooltip("Cubes closer than this, in metres, are too late to start moving across for.")]
    public float cubeMinAheadMetres = 12f;
    [Tooltip("Slowest it moves across the road, in metres per second: back to its racing line, and toward cubes that are far away.")]
    public float laneChangeSpeed = 4f;
    [Tooltip("Fastest it moves across toward a cube, in metres per second. Cubes it can't reach in time at this speed are skipped.")]
    public float maxLaneChangeSpeed = 8f;
    [Tooltip("It aims to be lined up with the cube this many seconds before reaching it, so it goes through it straight.")]
    public float laneSettleSeconds = 0.5f;
    [Tooltip("When the cube is within its aim distance, it aims along the road through the cube to this many metres past it, following the curve on bends.")]
    public float aimPastCubeMetres = 4f;

    [Header("Start grid")]
    [Tooltip("At the start, the car the player picked takes the middle of the grid and the other two start either side of it. Each AI car's lane follows its side.")]
    public bool playerStartsInMiddle = true;
    [Tooltip("Lane, in metres from the centre, for the AI cars that start either side of the player.")]
    public float sideLaneMetres = 3.5f;

    [Header("Keeping the race close")]
    [Tooltip("Scales the AI's speeds by how long its lane is compared with the centre line, so an outside lane isn't slower over a lap and an inside lane isn't faster.")]
    public bool equalizeLaneLength = true;
    [Tooltip("Speeds the AI up a little when it is behind the player and eases it off when it is ahead.")]
    public bool catchUp = true;
    [Tooltip("Gap to the player, in metres, inside which the catch-up does nothing.")]
    public float catchUpDeadZoneMetres = 10f;
    [Tooltip("Gap to the player, in metres, at which the catch-up is at full strength.")]
    public float catchUpFullAtMetres = 120f;
    [Tooltip("Most the AI speeds up when behind the player, as a fraction of its target speed (0.07 = 7%).")]
    public float catchUpMaxBoost = 0.07f;
    [Tooltip("Most the AI eases off when ahead of the player, as a fraction of its target speed.")]
    public float catchUpMaxSlowdown = 0.07f;

    [Header("Looks")]
    [Tooltip("The car body. Leave empty to use the car model's main mesh.")]
    public Renderer bodyRenderer;
    public int bodyMaterialSlot = 0;
    [Tooltip("Paints the body to match the rival's car. Not used on the colour-switching grid, where each car keeps its own colour.")]
    public bool paintWithFuelColour = true;

    [Header("Car (from the main menu, single-rival scenes only)")]
    [Tooltip("Pick the rival's car from the car chosen in the main menu. Off: it keeps the Own Fuel set on AI Fuel Tank.")]
    public bool pickCarFromMenu = true;
    [Tooltip("Race the same car as the player instead of a different one.")]
    public bool sameCarAsPlayer = false;
    [Tooltip("With a different car: off picks one of the other two at random each race; on always takes the next one (Gasoline > Diesel > Ethanol > Gasoline).")]
    public bool alwaysNextCar = false;

    [Header("Respawn")]
    [Tooltip("If the AI is flipped, pinned against something or off the track this long, it is put back on the road.")]
    public float stuckSeconds = 2.5f;
    [Tooltip("Height above the road the AI is put back at, in metres.")]
    public float respawnHeight = 0.8f;
    [Tooltip("After the wheels leave the ground (the jump, a bump) or the AI is put back, stuck checks wait this long, in seconds.")]
    public float stuckGraceSeconds = 1.5f;
    [Tooltip("Logs each time the AI is put back on the road (for play-testing).")]
    public bool logRespawns = true;

    [Header("Shortcuts")]
    [Tooltip("Lets the AI take a shortcut when it holds the mix the shortcut needs. Off: it always stays on the main road.")]
    public bool useShortcuts = true;

    private Rigidbody rb;
    private CarDriver driver;
    private AIFuelTank tank;
    private AIShortcutRouter router;
    private int currentWaypoint = -1;
    private float stuckFor;
    private float graceUntil = -1;
    private int branchRespawns;
    private int lastRespawnPoint = -1, repeatRespawns;
    private float lastRespawnTime = -100f;
    private Material carBody;
    private bool onSwitchGrid;
    private readonly List<FuelPickup> cubes = new List<FuelPickup>();
    private FuelPickup chasing;
    private float currentLane;
    private float nextCubeSearch;
    private readonly Dictionary<FuelPickup, Vector2> cubeSpots = new Dictionary<FuelPickup, Vector2>();
    private float[] alongTrack;
    private float lapMetres;
    private float carAlong;
    private float laneRate;
    private float targetSpeedKph = -1f;
    private float laneLengthFactor = 1f;
    private float measuredLane = float.NaN;

    private static int arrangedScene = -1;

    public void PrepareControlHandoff()
    {
        chasing = null;
        currentLane = laneOffset;
        currentWaypoint = -1;
        stuckFor = 0f;
        targetSpeedKph = -1f;
        graceUntil = Time.time + stuckGraceSeconds;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ArrangeStartGrid();

        driver = GetComponent<CarDriver>();
        if (driver == null)
        {
            Debug.LogError("The AI car needs a CarDriver with its four wheels set up.", this);
            enabled = false;
            return;
        }
        driver.ExternalInput = true;
        driver.enabled = true;

        FuelInventory player = FindFirstObjectByType<FuelInventory>();
        CarDriver playerDriver = player != null ? player.GetComponent<CarDriver>() : null;
        if (playerDriver != null)
            driver.CopyHandlingFrom(playerDriver);

        tank = GetComponent<AIFuelTank>();
        if (tank == null)
            tank = gameObject.AddComponent<AIFuelTank>();

        if (!UseGridColour() && pickCarFromMenu)
            ChooseCar();

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

    void ArrangeStartGrid()
    {
        if (!playerStartsInMiddle || arrangedScene == gameObject.scene.handle)
            return;
        arrangedScene = gameObject.scene.handle;

        RaceManager race = FindFirstObjectByType<RaceManager>();
        if (race == null || !race.colorSwitchingMode || race.racers == null || race.racers.Length != 3)
            return;
        CarDriver[] cars = (CarDriver[])race.racers.Clone();
        foreach (CarDriver car in cars)
            if (car == null)
                return;

        int selection = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCarColor", PlayerPrefs.GetInt("SelectedCarFuel", 0)), 0, 2);
        CarDriver chosen = race.racers[selection];
        Vector3 forward = Vector3.ProjectOnPlane(chosen.transform.forward, Vector3.up).normalized;
        Vector3 laneSide = new Vector3(-forward.z, 0f, forward.x);

        System.Array.Sort(cars, (a, b) => Vector3.Dot(a.transform.position, laneSide).CompareTo(Vector3.Dot(b.transform.position, laneSide)));
        Vector3[] slotPositions = new Vector3[3];
        Quaternion[] slotRotations = new Quaternion[3];
        for (int i = 0; i < 3; i++)
        {
            slotPositions[i] = cars[i].transform.position;
            slotRotations[i] = cars[i].transform.rotation;
        }

        CarDriver[] order = new CarDriver[3];
        order[1] = chosen;
        int side = 0;
        foreach (CarDriver car in cars)
        {
            if (car == chosen)
                continue;
            order[side] = car;
            side = 2;
        }

        float[] slotLanes = { -sideLaneMetres, 0f, sideLaneMetres };
        for (int i = 0; i < 3; i++)
        {
            order[i].transform.SetPositionAndRotation(slotPositions[i], slotRotations[i]);
            Rigidbody body = order[i].GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = slotPositions[i];
                body.rotation = slotRotations[i];
            }
            AICarWaypointDriver ai = order[i].GetComponent<AICarWaypointDriver>();
            if (ai != null)
                ai.laneOffset = slotLanes[i];
        }
    }

    void OnEnable()
    {
        targetSpeedKph = -1f;
    }

    void OnDestroy()
    {
        if (router == null)
            return;
        router.RejoinedMainLine -= OnRejoinedMainLine;
        router.RecoveredToMainLine -= OnRecoveredToMainLine;
    }

    void Start()
    {
        rb.isKinematic = false;
        currentLane = laneOffset;

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
        driver.EngineRunning = true;
        driver.FuelFraction = 1f;
        if (track == null || track.centerline.Count == 0)
            return;

        if (router == null || !router.IsFollowingBranch)
            UpdateWaypoint(track.centerline.Count);

        if (router != null && router.TryGetOverride(currentWaypoint, out AIShortcutRouter.SteerCommand command))
        {
            DriveShortcut(command);
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float speed = Vector3.Dot(rb.linearVelocity, forward) * 3.6f;
        float speedMs = Mathf.Max(0f, speed / 3.6f);

        float aimDistance = lookAheadMinMetres + speedMs * lookAheadSeconds;
        tank.UpdateStanding(rb);
        laneRate = laneChangeSpeed;
        float lane = FindCubeLane(track.centerline.Count, speedMs, out float cubeLane) ? cubeLane : laneOffset;
        currentLane = Mathf.MoveTowards(currentLane, lane, laneRate * Time.fixedDeltaTime);
        Vector3 target = PointAlongTrack(aimDistance, currentLane);
        if (chasing != null)
        {
            float cubeAhead = MetresAhead(chasing);
            if (cubeAhead <= aimDistance)
            {
                target = PointAlongTrack(cubeAhead + aimPastCubeMetres, cubeLane);
                currentLane = Mathf.MoveTowards(currentLane, cubeLane, maxLaneChangeSpeed * Time.fixedDeltaTime);
            }
        }
        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            return;

        float cornerLimit = CornerLimitKph(aimDistance + speedMs * cornerPreviewSeconds, lane);
        float wantedSpeed = Mathf.Min(speedKph, cornerLimit) * LaneLengthFactor() * CatchUpFactor();
        targetSpeedKph = targetSpeedKph < 0f
            ? wantedSpeed
            : Mathf.MoveTowards(targetSpeedKph, wantedSpeed, speedRampKph * Time.fixedDeltaTime);
        float desiredSpeed = targetSpeedKph;

        float yawRate = Vector3.Dot(rb.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
        float aimAngle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        float steer = Mathf.Clamp((aimAngle - yawRate * steerDamping) / fullSteerAtDegrees, -1f, 1f);
        Pedals(desiredSpeed, speed, out float throttle, out bool brake);
        if (tank.Fart != null && tank.Fart.IsFarting && cornerLimit >= speedKph)
        {
            throttle = 1f;
            brake = false;
        }

        driver.SetInput(throttle, steer, brake);
        CheckStuck(speed, throttle);
    }

    bool FindCubeLane(int count, float speedMs, out float lane)
    {
        lane = laneOffset;
        if (!huntCubes)
        {
            chasing = null;
            return false;
        }
        RefreshCubes(count);
        FuelType wanted = tank.CubeColour;

        if (chasing != null)
        {
            float ahead = MetresAhead(chasing);
            if (chasing.IsAvailable && chasing.type == wanted && ahead <= cubeSearchMetres && ahead > 0.5f)
            {
                lane = cubeSpots[chasing].y;
                laneRate = LaneRateFor(lane, ahead, speedMs);
                return true;
            }
            chasing = null;
        }

        float nearest = float.MaxValue;
        float nearestLane = laneOffset;
        foreach (FuelPickup cube in cubes)
        {
            if (cube == null || !cube.IsAvailable || cube.type != wanted)
                continue;
            float ahead = MetresAhead(cube);
            if (ahead < cubeMinAheadMetres || ahead > cubeSearchMetres || ahead >= nearest)
                continue;
            float cubeLane = cubeSpots[cube].y;
            float secondsToLineUp = ahead / Mathf.Max(speedMs, 5f) - laneSettleSeconds;
            if (Mathf.Abs(cubeLane - currentLane) > maxLaneChangeSpeed * Mathf.Max(secondsToLineUp, 0f))
                continue;
            nearest = ahead;
            nearestLane = cubeLane;
            chasing = cube;
        }
        if (chasing == null)
            return false;
        if (tank.logFuelDecisions)
            Debug.Log($"[AI] {name} ({tank.ownFuel}, P{tank.RacePosition}) going for the {chasing.type} cube {nearest:0} m ahead, {nearestLane:0.0} m from the centre.", this);
        lane = nearestLane;
        laneRate = LaneRateFor(lane, nearest, speedMs);
        return true;
    }

    float LaneRateFor(float cubeLane, float ahead, float speedMs)
    {
        float secondsToLineUp = Mathf.Max(ahead / Mathf.Max(speedMs, 5f) - laneSettleSeconds, 0.25f);
        float needed = Mathf.Abs(cubeLane - currentLane) / secondsToLineUp;
        return Mathf.Clamp(needed, laneChangeSpeed, maxLaneChangeSpeed);
    }

    void ProjectOnTrack(Vector3 position, int near, out float along, out float lateral)
    {
        int count = track.centerline.Count;
        along = 0f;
        lateral = 0f;
        float best = float.MaxValue;
        for (int start = near - 1; start <= near; start++)
        {
            int a = (start % count + count) % count;
            Vector3 from = track.GetPoint(a);
            Vector3 segment = track.GetPoint(a + 1) - from;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length < 0.001f)
                continue;
            Vector3 direction = segment / length;
            Vector3 offset = position - from;
            offset.y = 0f;
            float t = Mathf.Clamp(Vector3.Dot(offset, direction), 0f, length);
            Vector3 across = offset - direction * t;
            if (across.sqrMagnitude >= best)
                continue;
            best = across.sqrMagnitude;
            along = alongTrack[a] + t;
            lateral = Vector3.Dot(offset, new Vector3(-direction.z, 0f, direction.x));
        }
    }

    float MetresAhead(FuelPickup cube)
    {
        return Mathf.Repeat(cubeSpots[cube].x - carAlong, lapMetres);
    }

    void RefreshCubes(int count)
    {
        if (alongTrack == null || alongTrack.Length != count + 1)
        {
            alongTrack = new float[count + 1];
            for (int i = 0; i < count; i++)
            {
                Vector3 segment = track.GetPoint(i + 1) - track.GetPoint(i);
                segment.y = 0f;
                alongTrack[i + 1] = alongTrack[i] + segment.magnitude;
            }
            lapMetres = Mathf.Max(alongTrack[count], 1f);
        }
        ProjectOnTrack(rb.position, currentWaypoint, out carAlong, out _);
        if (cubes.Count > 0 || Time.time < nextCubeSearch)
            return;
        nextCubeSearch = Time.time + 1f;
        foreach (FuelPickup cube in FindObjectsByType<FuelPickup>(FindObjectsSortMode.None))
        {
            int near = NearestPoint(cube.transform.position, 0, count);
            ProjectOnTrack(cube.transform.position, near, out float along, out float lateral);
            cubes.Add(cube);
            cubeSpots[cube] = new Vector2(along, lateral);
        }
    }

    // OnTriggerEnter still fires on a disabled script, hence the enabled check.
    void OnTriggerEnter(Collider other)
    {
        if (!enabled || !huntCubes || tank == null)
            return;
        FuelPickup cube = other.GetComponentInParent<FuelPickup>();
        if (cube == null || !cube.IsAvailable || !tank.WantsCube(cube.type))
            return;
        if (!cube.TakeForAI())
            return;
        tank.TakeCube(cube.type);
        if (cube == chasing)
            chasing = null;
    }

    void DriveShortcut(AIShortcutRouter.SteerCommand command)
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 toTarget = command.target - rb.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            toTarget = forward;

        float angle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        float steer = command.lockSteering ? 0f : Mathf.Clamp(angle / fullSteerAtDegrees, -1f, 1f);
        float desiredSpeed = command.targetSpeed > 0f ? command.targetSpeed * 3.6f : speedKph;
        float speed = Vector3.Dot(rb.linearVelocity, forward) * 3.6f;
        Pedals(desiredSpeed, speed, out float throttle, out bool brake);
        if (command.fullThrottle)
        {
            throttle = 1f;
            brake = false;
        }
        if (command.lockSteering)
        {
            throttle = 0f;
            brake = false;
        }

        driver.SetInput(throttle, steer, brake);
        CheckStuck(speed, throttle);
    }

    void Pedals(float desiredSpeed, float speed, out float throttle, out bool brake)
    {
        float gap = desiredSpeed - speed;
        throttle = Mathf.Clamp01(cruiseThrottle + gap / Mathf.Max(throttleBandKph, 0.1f));
        brake = gap < -brakeOverKph;
        if (brake)
            throttle = 0f;
    }

    float LaneLengthFactor()
    {
        if (!equalizeLaneLength)
            return 1f;
        if (measuredLane != laneOffset)
        {
            measuredLane = laneOffset;
            float centre = 0f, lane = 0f;
            int count = track.centerline.Count;
            Vector3 previousLane = LanePoint(count - 1, laneOffset);
            for (int i = 0; i < count; i++)
            {
                Vector3 laneHere = LanePoint(i, laneOffset);
                centre += Vector3.Distance(track.GetPoint(i - 1), track.GetPoint(i));
                lane += Vector3.Distance(previousLane, laneHere);
                previousLane = laneHere;
            }
            laneLengthFactor = centre > 1f ? Mathf.Clamp(lane / centre, 0.85f, 1.15f) : 1f;
        }
        return laneLengthFactor;
    }

    Vector3 LanePoint(int index, float lane)
    {
        Vector3 direction = (track.GetForward(index - 1) + track.GetForward(index)).normalized;
        return track.GetPoint(index) + new Vector3(-direction.z, 0f, direction.x) * lane;
    }

    float CatchUpFactor()
    {
        RacePositionHUD race = RacePositionHUD.Instance;
        if (!catchUp || race == null || race.PlayerBody == null || race.PlayerBody == rb)
            return 1f;
        if (!race.TryGetMetres(rb, out float mine) || !race.TryGetMetres(race.PlayerBody, out float player))
            return 1f;
        float behind = player - mine;
        float strength = Mathf.Clamp01((Mathf.Abs(behind) - catchUpDeadZoneMetres)
            / Mathf.Max(catchUpFullAtMetres - catchUpDeadZoneMetres, 1f));
        return behind > 0f ? 1f + catchUpMaxBoost * strength : 1f - catchUpMaxSlowdown * strength;
    }

    Vector3 PointAlongTrack(float distance, float lane)
    {
        int count = track.centerline.Count;
        int index = currentWaypoint;
        Vector3 from = track.GetPoint(index);
        Vector3 along = track.GetPoint(index + 1) - from;
        along.y = 0f;
        Vector3 fromCar = rb.position - from;
        fromCar.y = 0f;
        float left = Mathf.Max(0f, distance + (along.sqrMagnitude > 0.0001f ? Vector3.Dot(fromCar, along.normalized) : 0f));

        for (int step = 0; step < count; step++)
        {
            int next = (index + 1) % count;
            Vector3 to = track.GetPoint(next);
            float length = Vector3.Distance(from, to);
            if (length >= left || step == count - 1)
            {
                float t = length > 0.001f ? Mathf.Clamp01(left / length) : 0f;
                Vector3 point = Vector3.Lerp(from, to, t);
                Vector3 direction = Vector3.Slerp(track.GetForward(index), track.GetForward(next), t);
                Vector3 side = new Vector3(-direction.z, 0f, direction.x).normalized;
                return point + side * lane;
            }
            left -= length;
            from = to;
            index = next;
        }
        return from;
    }

    float CornerLimitKph(float distance, float lane)
    {
        int count = track.centerline.Count;
        float cornering = driver.MaxCorneringAcceleration * cornerSpeedMargin;
        float limit = float.MaxValue;
        float travelled = 0f;
        int index = currentWaypoint;
        for (int step = 0; step < count && travelled < distance; step++)
        {
            int next = (index + 1) % count;
            travelled += Vector3.Distance(track.GetPoint(index), track.GetPoint(next));
            Vector3 before = track.GetForward(next - 1);
            Vector3 after = track.GetForward(next + 1);
            float angle = Vector3.Angle(before, after) * Mathf.Deg2Rad;
            if (angle > 0.001f)
            {
                float radius = Vector3.Distance(track.GetPoint(next - 1), track.GetPoint(next + 1)) / angle;
                radius = Mathf.Max(radius + lane * Mathf.Sign(Vector3.Cross(before, after).y), 3f);
                float cornerMs = Mathf.Sqrt(cornering * radius);
                limit = Mathf.Min(limit, Mathf.Sqrt(cornerMs * cornerMs + 2f * cornerBrakeDecel * travelled));
            }
            index = next;
        }
        return limit == float.MaxValue ? speedKph : limit * 3.6f;
    }

    void CheckStuck(float speed, float throttle)
    {
        if (driver.GroundedWheelCount == 0)
            graceUntil = Mathf.Max(graceUntil, Time.time + stuckGraceSeconds);
        if (Time.time < graceUntil && Vector3.Dot(transform.up, Vector3.up) >= 0.3f)
        {
            stuckFor = 0f;
            return;
        }

        bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
        bool pinned = throttle > 0.5f && Mathf.Abs(speed) < 3f;
        bool onShortcut = (router != null && router.IsFollowingBranch) || ShortcutTracker.IsOnBranch(rb);
        float maxSideways = track.roadWidth * 0.5f + track.curbWidth + 3f;
        bool offTrack = !onShortcut && Mathf.Abs(LateralOffset(rb.position, currentWaypoint)) > maxSideways;

        stuckFor = upsideDown || pinned || offTrack ? stuckFor + Time.fixedDeltaTime : 0f;
        if (stuckFor < stuckSeconds)
            return;

        if (logRespawns)
            Debug.Log($"[AI] {name} stuck ({(upsideDown ? "upside down" : pinned ? "pinned, not moving" : "off the track")}) " +
                      $"at track point {currentWaypoint}: putting it back" + (onShortcut ? " on the shortcut." : " on the road."), this);
        if (onShortcut)
            PutBackOnShortcut();
        else
            PutBackOnTrack();
    }

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
                ShortcutTracker.RaiseFailed(rb, branch);
            else if (router != null)
                OnRecoveredToMainLine(router.AbortShortcut());
            return;
        }

        if (router != null)
            router.AbortShortcut();
        ShortcutTracker.Exit(rb);
        PutBackOnTrack();
    }

    void PutBackOnTrack()
    {
        if (RepeatRespawn(currentWaypoint))
            currentWaypoint = (currentWaypoint + 5 * repeatRespawns) % track.centerline.Count;

        Vector3 forward = track.GetForward(currentWaypoint);
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);
        Vector3 position = track.GetPoint(currentWaypoint) + side * laneOffset;
        position.y = track.surfaceY + respawnHeight;
        Place(position, Quaternion.LookRotation(forward, Vector3.up));
    }

    void Place(Vector3 position, Quaternion rotation)
    {
        chasing = null;
        currentLane = laneOffset;
        stuckFor = 0f;
        graceUntil = Time.time + stuckGraceSeconds;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    void OnRejoinedMainLine(int index)
    {
        currentWaypoint = index;
        stuckFor = 0f;
        branchRespawns = 0;
        graceUntil = Time.time + stuckGraceSeconds;
    }

    void OnRecoveredToMainLine(int index)
    {
        if (index >= 0)
            currentWaypoint = index;
        stuckFor = 0f;
        branchRespawns = 0;
        graceUntil = Time.time + stuckGraceSeconds;
    }

    // Search only near the last point: parts of the loop pass close to each other.
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

    float LateralOffset(Vector3 position, int index)
    {
        Vector3 forward = track.GetForward(index);
        Vector3 across = new Vector3(-forward.z, 0f, forward.x);
        Vector3 offset = position - track.GetPoint(index);
        offset.y = 0f;
        return Vector3.Dot(offset, across);
    }

    bool UseGridColour()
    {
        CarDriver myCar = driver;
        RaceManager race = FindFirstObjectByType<RaceManager>();
        int slot = race != null && race.colorSwitchingMode && race.racers != null && myCar != null
            ? System.Array.IndexOf(race.racers, myCar) : -1;
        if (slot < 0)
        {
            FuelInventory grid = null;
            foreach (FuelInventory inventory in FindObjectsByType<FuelInventory>(FindObjectsSortMode.None))
                if (inventory.cars != null && inventory.cars.Length > 0)
                    grid = inventory;
            slot = grid != null && myCar != null ? System.Array.IndexOf(grid.cars, myCar) : -1;
        }
        if (slot < 0)
            return false;
        tank.ownFuel = (FuelType)slot;
        onSwitchGrid = true;
        return true;
    }

    void ChooseCar()
    {
        int carCount = System.Enum.GetValues(typeof(FuelType)).Length;
        int player = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCarFuel", 0), 0, carCount - 1);

        int rival = player;
        if (!sameCarAsPlayer)
            rival = (player + (alwaysNextCar ? 1 : Random.Range(1, carCount))) % carCount;
        tank.ownFuel = (FuelType)rival;

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

    void PaintBody()
    {
        if (!paintWithFuelColour || onSwitchGrid)
            return;

        FuelInventory playerBag = FindFirstObjectByType<FuelInventory>();
        Renderer body = bodyRenderer != null ? bodyRenderer : FindBodyRenderer();
        if (playerBag == null || body == null)
            return;

        Material[] materials = body.sharedMaterials;
        if (bodyMaterialSlot < 0 || bodyMaterialSlot >= materials.Length)
            return;
        materials[bodyMaterialSlot] = carBody != null ? carBody : playerBag.MaterialOf(tank.ownFuel);
        body.sharedMaterials = materials;
    }

    Renderer FindBodyRenderer()
    {
        Renderer best = null;
        foreach (MeshRenderer candidate in GetComponentsInChildren<MeshRenderer>())
            if (best == null || candidate.sharedMaterials.Length > best.sharedMaterials.Length)
                best = candidate;
        return best;
    }
}
