using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
[RequireComponent(typeof(Rigidbody))]
public class AICarWaypointDriver : MonoBehaviour
{
    [Header("Track")]
    public LoopTrackGenerator track;
    [Tooltip("Racing line offset from the centre (m).")]
    public float laneOffset = 1.5f;

    [Header("AI Driving")]
    [Tooltip("Target speed on straights (km/h).")]
    public float speedKph = 80f;
    [Tooltip("Fraction of the car's cornering limit used in corners.")]
    [Range(0.5f, 1f)] public float cornerSpeedMargin = 0.9f;
    [Tooltip("Planned braking before corners (m/s²).")]
    public float cornerBrakeDecel = 7f;
    [Tooltip("Aim distance at standstill (m).")]
    public float lookAheadMinMetres = 10f;
    [Tooltip("Extra aim distance per m/s of speed (s).")]
    public float lookAheadSeconds = 0.9f;
    [Tooltip("Corner preview distance per m/s of speed (s).")]
    public float cornerPreviewSeconds = 1.6f;
    [Tooltip("Aim angle for full steering lock (degrees).")]
    public float fullSteerAtDegrees = 30f;
    [Tooltip("Steering damping against yaw rate (s).")]
    public float steerDamping = 0.12f;
    [Tooltip("Max change in target speed (km/h per second).")]
    public float speedRampKph = 40f;
    [Tooltip("Throttle that holds a steady speed.")]
    public float cruiseThrottle = 0.35f;
    [Tooltip("Speed deficit (km/h) for full throttle.")]
    public float throttleBandKph = 8f;
    [Tooltip("Overspeed (km/h) before braking instead of lifting off.")]
    public float brakeOverKph = 10f;

    [Header("Colour cubes")]
    [Tooltip("Own colour while leading, leader's colour while 2nd or 3rd.")]
    public bool huntCubes = true;
    [Tooltip("Search distance for cubes (m).")]
    public float cubeSearchMetres = 120f;
    [Tooltip("Ignore cubes closer than this (m).")]
    public float cubeMinAheadMetres = 12f;
    [Tooltip("Minimum lane-change speed (m/s).")]
    public float laneChangeSpeed = 4f;
    [Tooltip("Maximum lane-change speed (m/s).")]
    public float maxLaneChangeSpeed = 8f;
    [Tooltip("Be lined up this many seconds before the cube.")]
    public float laneSettleSeconds = 0.5f;
    [Tooltip("Aim this far past the cube (m).")]
    public float aimPastCubeMetres = 4f;
    [Tooltip("Aim distance while lining up for a cube (s of travel).")]
    public float cubeLookAheadSeconds = 0.45f;
    [Tooltip("Lateral acceleration used to reach a cube (m/s²).")]
    public float cubeLateralAccel = 6f;
    [Tooltip("Max offset from the cube centre that still collects it (m).")]
    public float cubeHitTolerance = 1f;

    [Header("Other cars")]
    [Tooltip("Avoid cutting across, ramming or sticking to other cars.")]
    public bool avoidOtherCars = true;
    [Tooltip("Centre-to-centre side gap kept from a car alongside (m).")]
    public float sideGapMetres = 2.8f;
    [Tooltip("Along-track distance within which a car counts as alongside (m).")]
    public float alongsideMetres = 5.5f;
    [Tooltip("Along-track distance within which two cars count as level (m).")]
    public float levelMetres = 1.5f;
    [Tooltip("Speed fraction while letting another car cross ahead.")]
    [Range(0.5f, 1f)] public float yieldSpeedShare = 0.85f;
    [Tooltip("Time gap kept behind a slower car (s).")]
    public float followSeconds = 0.6f;

    [Header("Start grid")]
    [Tooltip("Player's car starts in the middle of the grid, AI cars either side.")]
    public bool playerStartsInMiddle = true;
    [Tooltip("Lane offset for the AI cars beside the player at the start (m).")]
    public float sideLaneMetres = 3.5f;

    [Header("Keeping the race close")]
    [Tooltip("Scale speed by lane length so inside and outside lanes are even over a lap.")]
    public bool equalizeLaneLength = true;
    [Tooltip("Speed up when behind the player, ease off when ahead.")]
    public bool catchUp = true;
    [Tooltip("Catch-up dead zone (m).")]
    public float catchUpDeadZoneMetres = 10f;
    [Tooltip("Gap at which catch-up is at full strength (m).")]
    public float catchUpFullAtMetres = 120f;
    [Tooltip("Max speed-up when behind (fraction).")]
    public float catchUpMaxBoost = 0.07f;
    [Tooltip("Max slow-down when ahead (fraction).")]
    public float catchUpMaxSlowdown = 0.07f;

    [Header("Looks")]
    [Tooltip("Car body renderer. Empty = largest mesh on the car.")]
    public Renderer bodyRenderer;
    public int bodyMaterialSlot = 0;
    [Tooltip("Paint the body in the rival's fuel colour (not on the colour-switching grid).")]
    public bool paintWithFuelColour = true;

    [Header("Car (from the main menu, single-rival scenes only)")]
    [Tooltip("Pick the rival's car from the main-menu choice.")]
    public bool pickCarFromMenu = true;
    [Tooltip("Use the same car as the player.")]
    public bool sameCarAsPlayer = false;
    [Tooltip("Off: random other car. On: next car in order.")]
    public bool alwaysNextCar = false;

    [Header("Respawn")]
    [Tooltip("Respawn after being flipped, pinned or off track this long (s).")]
    public float stuckSeconds = 2.5f;
    [Tooltip("Respawn height above the road (m).")]
    public float respawnHeight = 0.8f;
    [Tooltip("Pause stuck checks this long after leaving the ground or respawning (s).")]
    public float stuckGraceSeconds = 1.5f;
    [Tooltip("Log respawns.")]
    public bool logRespawns = true;

    [Header("Shortcuts")]
    [Tooltip("Allow shortcuts when the AI holds the required mix.")]
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
    private readonly Dictionary<FuelPickup, Vector3> cubeGround = new Dictionary<FuelPickup, Vector3>();
    private readonly Dictionary<FuelPickup, Vector3> cubeRoadForward = new Dictionary<FuelPickup, Vector3>();
    private float[] alongTrack;
    private float lapMetres;
    private float carAlong;
    private float carLateral; // measured; currentLane is the target
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

        // Practice cars stay parked until the player takes them over.
        if (RaceManager.Instance != null && RaceManager.Instance.IsSwapPractice)
        {
            paintWithFuelColour = false;
            return;
        }

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

        float speedShare = 1f;
        float followCapKph = float.MaxValue;
        bool givingWay = false;
        if (avoidOtherCars)
            lane = AvoidOtherCars(lane, speedMs, out speedShare, out followCapKph, out givingWay);

        bool goingForCube = chasing != null && !givingWay;
        float steerAim = goingForCube
            ? Mathf.Clamp(speedMs * cubeLookAheadSeconds, lookAheadMinMetres * 0.6f, aimDistance)
            : aimDistance;
        currentLane = Mathf.MoveTowards(currentLane, lane, laneRate * Time.fixedDeltaTime);
        Vector3 target = PointAlongTrack(steerAim, currentLane);
        if (goingForCube)
        {
            float cubeAhead = MetresAhead(chasing);
            if (cubeAhead <= aimDistance)
            {
                target = CubeAimPoint(chasing);
                currentLane = Mathf.MoveTowards(currentLane, cubeLane, maxLaneChangeSpeed * Time.fixedDeltaTime);
            }
        }
        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
            return;

        float cornerLimit = CornerLimitKph(aimDistance + speedMs * cornerPreviewSeconds, lane);
        float wantedSpeed = Mathf.Min(Mathf.Min(speedKph, cornerLimit) * LaneLengthFactor() * CatchUpFactor() * speedShare,
            followCapKph);
        targetSpeedKph = targetSpeedKph < 0f
            ? wantedSpeed
            : Mathf.MoveTowards(targetSpeedKph, wantedSpeed, speedRampKph * Time.fixedDeltaTime);
        float desiredSpeed = targetSpeedKph;

        float yawRate = Vector3.Dot(rb.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
        float aimAngle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        float steer = Mathf.Clamp((aimAngle - yawRate * steerDamping) / fullSteerAtDegrees, -1f, 1f);
        Pedals(desiredSpeed, speed, out float throttle, out bool brake);
        // No boost while giving way or following, or it rams the other car.
        if (tank.Fart != null && tank.Fart.IsFarting && cornerLimit >= speedKph
            && speedShare >= 1f && followCapKph == float.MaxValue)
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
            float shift = Mathf.Max(Mathf.Abs(cubeLane - carLateral) - cubeHitTolerance, 0f);
            float time = Mathf.Max(secondsToLineUp, 0f);
            // Max sideways shift: accelerate half the time, decelerate the other half.
            float reachable = Mathf.Min(maxLaneChangeSpeed * time, 0.25f * cubeLateralAccel * time * time);
            if (shift > reachable)
                continue;
            nearest = ahead;
            nearestLane = cubeLane;
            chasing = cube;
        }
        if (chasing == null)
            return false;
        currentLane = carLateral;
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

    void EnsureTrackLengths(int count)
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
    }

    void RefreshCubes(int count)
    {
        EnsureTrackLengths(count);
        ProjectOnTrack(rb.position, currentWaypoint, out carAlong, out carLateral);
        if (cubes.Count > 0 || Time.time < nextCubeSearch)
            return;
        nextCubeSearch = Time.time + 1f;
        foreach (FuelPickup cube in FindObjectsByType<FuelPickup>(FindObjectsSortMode.None))
        {
            int near = NearestPoint(cube.transform.position, 0, count);
            ProjectOnTrack(cube.transform.position, near, out float along, out float lateral);
            cubes.Add(cube);
            cubeSpots[cube] = new Vector2(along, lateral);
            Vector3 ground = cube.transform.position;
            ground.y = rb.position.y;
            cubeGround[cube] = ground;
            cubeRoadForward[cube] = track.GetForward(near);
        }
    }

    // A point just past the cube, along the road.
    Vector3 CubeAimPoint(FuelPickup cube)
    {
        Vector3 roadForward = cubeRoadForward[cube];
        roadForward.y = 0f;
        return cubeGround[cube] + roadForward.normalized * aimPastCubeMetres;
    }

    // Adjusts the wanted lane around nearby cars. Also returns a speed fraction (when
    // yielding) and a speed cap (when following a slower car).
    float AvoidOtherCars(float wantedLane, float speedMs, out float speedShare, out float followCapKph, out bool givingWay)
    {
        speedShare = 1f;
        followCapKph = float.MaxValue;
        givingWay = false;
        int count = track.centerline.Count;
        EnsureTrackLengths(count);
        ProjectOnTrack(rb.position, currentWaypoint, out float myAlong, out float myLateral);
        float laneLimit = Mathf.Max(track.roadWidth * 0.5f - 1.2f, 0.5f);
        float requested = wantedLane;
        float followMetres = 4.5f + speedMs * followSeconds;

        foreach (CarDriver other in CarDriver.All)
        {
            if (other == null || other == driver || other.Body == null)
                continue;
            Rigidbody otherBody = other.Body;
            Vector3 offset = otherBody.position - rb.position;
            // Ignore cars on a different level (bridge).
            if (Mathf.Abs(offset.y) > 2.5f)
                continue;
            offset.y = 0f;
            if (offset.sqrMagnitude > 35f * 35f || ShortcutTracker.IsOnBranch(otherBody))
                continue;

            int near = NearestPoint(otherBody.position, currentWaypoint - 8, 21);
            ProjectOnTrack(otherBody.position, near, out float otherAlong, out float otherLateral);
            float ahead = Mathf.Repeat(otherAlong - myAlong + lapMetres * 0.5f, lapMetres) - lapMetres * 0.5f;
            float gap = otherLateral - myLateral;
            bool inLine = Mathf.Abs(gap) < sideGapMetres * 0.7f;
            if (inLine && ahead <= 0f)
                continue;

            // Close behind in the same line: match its speed.
            if (ahead > 0f && ahead < followMetres && Mathf.Abs(gap) < sideGapMetres * 0.8f)
            {
                float theirSpeed = Mathf.Max(Vector3.Dot(otherBody.linearVelocity, track.GetForward(near)), 0f);
                float tooClose = followMetres - ahead;
                followCapKph = Mathf.Min(followCapKph, Mathf.Max(theirSpeed - tooClose * 0.5f, 0f) * 3.6f);
            }

            if (Mathf.Abs(ahead) < alongsideMetres)
            {
                // Alongside: don't steer across it.
                float theirSide = Mathf.Abs(gap) > 0.5f ? Mathf.Sign(gap) : (requested >= otherLateral ? -1f : 1f);
                float before = wantedLane;
                wantedLane = theirSide > 0f
                    ? Mathf.Min(wantedLane, otherLateral - sideGapMetres)
                    : Mathf.Max(wantedLane, otherLateral + sideGapMetres);
                if (!Mathf.Approximately(before, wantedLane))
                {
                    givingWay = true;
                    // The car behind yields; if level, tie-break on instance ID.
                    bool theyGoFirst = ahead > levelMetres
                        || (Mathf.Abs(ahead) <= levelMetres && other.GetInstanceID() < driver.GetInstanceID());
                    if (theyGoFirst)
                        speedShare = Mathf.Min(speedShare, yieldSpeedShare);
                }
            }
            else if (ahead > 0f && ahead < followMetres + 10f && Mathf.Abs(gap) < sideGapMetres)
            {
                // Car ahead in our line: pass on the side closer to the wanted lane.
                if (Mathf.Abs(wantedLane - otherLateral) < sideGapMetres)
                {
                    float left = otherLateral + sideGapMetres;
                    float right = otherLateral - sideGapMetres;
                    bool leftFits = left <= laneLimit;
                    bool rightFits = right >= -laneLimit;
                    if (leftFits || rightFits)
                    {
                        wantedLane = leftFits && (!rightFits || Mathf.Abs(left - requested) <= Mathf.Abs(right - requested))
                            ? left : right;
                        givingWay = true;
                    }
                }
            }
        }
        if (givingWay)
            wantedLane = Mathf.Clamp(wantedLane, -laneLimit, laneLimit);
        return wantedLane;
    }

    // Triggers fire on disabled scripts too.
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

    // Local search: parts of the loop pass close to each other.
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
