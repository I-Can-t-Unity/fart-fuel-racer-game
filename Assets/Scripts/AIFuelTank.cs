using System;
using System.Collections.Generic;
using UnityEngine;

// The AI rival's fuel. It works like the player's: a tank that drains with speed and
// throttle, a two-slot bag, and the same mixes. The difference is which fuel is "good":
// cubes of the rival's own fuel type refuel it (set from the main menu car choice by
// AICarWaypointDriver; Diesel, the green cans, if that is turned off), every
// other cube is a fart boost. Like the player's bag, the second pickup mixes straight away;
// the AI decides for itself when to use the mix.
// AICarWaypointDriver adds this component at runtime, so nothing has to be set up in the scene.
public class AIFuelTank : MonoBehaviour, IFuelMixHolder
{
    public const int Capacity = FuelInventory.Capacity;

    // Old fuel names (Correct/Green/Purple), before the cars ran on Gasoline/Diesel/Ethanol:
    // [Tooltip("The cube colour that refuels the AI. The AI car is painted this colour.")]
    // public FuelType ownFuel = FuelType.Green;
    [Tooltip("The fuel the AI runs on; its cubes refuel it and the AI car is painted their colour. Diesel cans are green.")]
    public FuelType ownFuel = FuelType.Diesel;
    public float fuel = 100f;

    // Like the player's car: two of this one wrong fuel in a row make Super Fart.
    public FuelType SuperFartFuel => SuperFartRecipe.FuelFor(ownFuel);

    [Header("Amounts (copied from the player's bag at the start, so both cars play by the same rules)")]
    public float pickupRefuel = 25f;
    public float pickupFartSeconds = .7f;
    public float premiumRefuel = 50f;
    public float hybridRefuel = 20f;
    public float superFartSeconds = 2.5f;
    public float chokeSeconds = 3f;

    [Header("Fuel drain (copied from the RaceManager at the start)")]
    public float drainPerSecond = 0f;
    public float drainPerSpeed = .05f;
    public float drainPerThrottle = 2.4f;

    [Header("AI decisions")]
    // Old: the player pressed Q to mix, so the AI waited a moment before mixing too.
    // [Tooltip("Pause before the AI mixes a full bag, in seconds, so it doesn't look instant.")]
    // public float mixDelay = .6f;
    [Tooltip("Pause before the AI mixes a full bag, in seconds. 0 mixes straight away, like the player's bag now does.")]
    public float mixDelay = 0f;
    [Tooltip("Below this fuel level the AI uses any refuelling mix straight away.")]
    public float lowFuel = 30f;
    [Tooltip("Corner sharpness ahead (0 straight, 1 sharp) below which the AI fires a super fart.")]
    public float superFartOnStraightBelow = .2f;
    [Tooltip("Corner sharpness ahead above which the AI burns toxic sludge, since it is slow there anyway.")]
    public float sludgeInCornerAbove = .5f;
    [Tooltip("The longest the AI holds a boost or sludge mix before using it anyway, in seconds.")]
    public float maxHoldSeconds = 4f;

    [Header("Saving Super Fart for the bridge")]
    [Tooltip("Keep a held Super Fart for the Super Fart jump while its split is coming up: neither the straight-road rule nor the hold timeout may spend it.")]
    public bool reserveSuperFart = true;
    [Tooltip("Reserve it when the split is this many track centre-line points ahead or closer (about 4.3 m each on Bridge Circuit).")]
    public int reservePointsAhead = 110;
    [Tooltip("Below this fuel level the reservation is dropped, so the bag can be freed to refuel.")]
    public float keepReserveAboveFuel = 15f;

    [Tooltip("With the jump split coming up (within Reserve Points Ahead), the AI goes for two cans of its Super Fart fuel instead of its own fuel, so it can take the jump.")]
    public bool craftSuperFartForBridge = true;
    [Tooltip("The AI only goes for Super Fart with at least this much fuel, since those cans don't refuel it.")]
    public float craftAboveFuel = 40f;
    [Tooltip("Logs every can the AI picks up, and when it switches between hunting Super Fart fuel and its own fuel (for play-testing).")]
    public bool logFuelDecisions = true;
    bool wantedSuperFart;

    [Tooltip("The AI only collects the fuel it wants right now (its Super Fart fuel while collecting for the jump, else its own fuel). Any other can it brushes past stays on the track, so it never grabs two cans at once by mistake.")]
    public bool onlyCollectWantedFuel = true;
    float lastSkipLog = -1f;

    // The fuel the AI is after right now.
    public FuelType WantedFuel => WantsSuperFart ? SuperFartFuel : ownFuel;

    // Set by AICarWaypointDriver every physics step: the AI has committed to the jump or is on
    // its branch, so the Super Fart is kept for the entry gate whatever the track position says.
    [HideInInspector] public bool usingShortcut;

    // Set by AICarWaypointDriver every physics step: how sharp the road ahead is, 0 to 1.
    [HideInInspector] public float cornerAhead;

    // The bag or held mix changed, like FuelInventory.Changed (for the rival's HUD icon).
    public event Action Changed;

    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;
    public IReadOnlyList<FuelType> Slots => slots;
    public float FuelFraction => fuel / 100f;
    public FartBoost Fart => fart;

    readonly List<FuelType> slots = new List<FuelType>();
    // For the Super Fart reservation: the track, where the jump splits off it, and where the AI is.
    LoopTrackGenerator track;
    int splitIndex = -1, trackIndex = -1;
    bool splitSearched, splitLogged;
    float raceStartTime = -1f;
    Rigidbody body;
    CarDriver driver;
    FartBoost fart;
    float mixAt = -1, heldSince;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        driver = GetComponent<CarDriver>();
        fart = GetComponent<FartBoost>();
        if (fart == null)
            fart = gameObject.AddComponent<FartBoost>();
    }

    void Start()
    {
        // Use the player's numbers so a cube or a mix is worth the same to both cars.
        FuelInventory playerBag = FindFirstObjectByType<FuelInventory>();
        if (playerBag != null)
        {
            pickupRefuel = playerBag.pickupRefuel;
            pickupFartSeconds = playerBag.pickupFartSeconds;
            premiumRefuel = playerBag.premiumRefuel;
            hybridRefuel = playerBag.hybridRefuel;
            superFartSeconds = playerBag.superFartSeconds;
            chokeSeconds = playerBag.chokeSeconds;
        }

        // Same fart boost strength as the player's car.
        FartBoost playerFart = playerBag != null ? playerBag.GetComponent<FartBoost>() : null;
        if (playerFart != null)
        {
            fart.acceleration = playerFart.acceleration;
            fart.maxBoostKph = playerFart.maxBoostKph;
            fart.chokeMaxKph = playerFart.chokeMaxKph;
        }

        RaceManager race = RaceManager.Instance;
        if (race != null)
        {
            // Starts with the same tank as the player.
            fuel = race.fuel;
            drainPerSecond = race.fuelDrainPerSecond;
            drainPerSpeed = race.fuelDrainPerSpeed;
            drainPerThrottle = race.fuelDrainPerThrottle;
        }
    }

    void Update()
    {
        bool raceInactive = RaceManager.Instance != null && !RaceManager.Instance.IsRacing;
        if (raceInactive)
            return;
        float speedKph = body != null ? body.linearVelocity.magnitude * 3.6f : 0;
        float throttle = driver != null ? Mathf.Abs(driver.Throttle) : 0;
        fuel = Mathf.Max(0, fuel -
            (drainPerSecond + speedKph * drainPerSpeed + throttle * drainPerThrottle) * Time.deltaTime);

        // Same as the player: an empty tank kills the engine and a low one weakens it.
        if (driver != null)
        {
            driver.EngineRunning = fuel > 0;
            driver.FuelFraction = FuelFraction;
        }

        if (mixAt >= 0 && Time.time >= mixAt)
        {
            mixAt = -1;
            MixSlots();
            Changed?.Invoke();
        }

        if (raceStartTime < 0)
            raceStartTime = Time.time;
        UpdateTrackIndex();

        if (logFuelDecisions && WantsSuperFart != wantedSuperFart)
        {
            wantedSuperFart = !wantedSuperFart;
            Debug.Log(wantedSuperFart
                ? $"[AI] Bridge split {((splitIndex - trackIndex) % track.centerline.Count + track.centerline.Count) % track.centerline.Count} points ahead: hunting {SuperFartFuel} for Super Fart (track point {trackIndex}, fuel {fuel:0})."
                : $"[AI] Back to hunting its own fuel {ownFuel} (track point {trackIndex}, fuel {fuel:0}, bag {(HeldMix != FuelMix.None ? HeldMix.ToString() : slots.Count == 0 ? "empty" : slots[0].ToString())}).", this);
        }
        if (logFuelDecisions && !splitLogged && splitSearched)
        {
            splitLogged = true;
            Debug.Log(splitIndex >= 0
                ? $"[AI] Super Fart jump split found at track point {splitIndex}."
                : "[AI] No Super Fart jump split found, so the AI won't hunt Super Fart.", this);
        }

        if (HeldMix != FuelMix.None && ShouldUseMix())
            UseMix();
    }

    // True while a held Super Fart is being saved for the jump: the split is close ahead and
    // there is enough fuel to keep the bag full until then. Only the jump gate can spend it.
    public bool IsReservingSuperFart
    {
        get
        {
            if (!reserveSuperFart || HeldMix != FuelMix.SuperFart)
                return false;
            if (usingShortcut)
                return true;
            if (fuel < keepReserveAboveFuel)
                return false;
            // Old: the same check written out here.
            // if (splitIndex < 0 || trackIndex < 0)
            //     return false;
            // int count = track.centerline.Count;
            // int ahead = ((splitIndex - trackIndex) % count + count) % count;
            // return ahead <= reservePointsAhead;
            return SplitAhead();
        }
    }

    // True while the AI should collect Super Fart fuel rather than its own: the jump split is
    // coming up, it has fuel to spare, and the bag is empty or holds one Super Fart can already.
    public bool WantsSuperFart =>
        craftSuperFartForBridge && reserveSuperFart && HeldMix == FuelMix.None && fuel >= craftAboveFuel
        && (slots.Count == 0 || slots[0] == SuperFartFuel) && SplitAhead();

    // The jump split is within Reserve Points Ahead, and the race doesn't end at the
    // start/finish line before it.
    bool SplitAhead()
    {
        if (splitIndex < 0 || trackIndex < 0)
            return false;
        int count = track.centerline.Count;
        int ahead = ((splitIndex - trackIndex) % count + count) % count;
        if (ahead > reservePointsAhead)
            return false;
        // Old: also counted the start line just ahead of the grid, so at the start of the race
        // the AI hunted its own fuel for its first metres.
        // if (RaceManager.Instance != null)
        // {
        //     int toFinish = ((track.startIndex - trackIndex) % count + count) % count;
        //     if (toFinish > 0 && toFinish < ahead)
        //         return false;
        // }
        // The race only ends at the line once the lap is nearly done, not right after the start.
        if (RaceManager.Instance != null && Time.time - raceStartTime > 10f)
        {
            int toFinish = ((track.startIndex - trackIndex) % count + count) % count;
            if (toFinish > 0 && toFinish < ahead)
                return false;
        }
        return true;
    }

    // Finds the Super Fart jump's split once, then follows the AI's nearest centre-line point,
    // looking only near the last one (the loop passes close to itself in places).
    void UpdateTrackIndex()
    {
        if (!reserveSuperFart)
            return;
        if (!splitSearched)
        {
            splitSearched = true;
            track = FindFirstObjectByType<LoopTrackGenerator>();
            if (track == null || track.centerline.Count == 0)
                return;
            Vector3? split = FindSplit();
            if (split.HasValue)
                splitIndex = NearestTrackPoint(split.Value, 0, track.centerline.Count);
        }
        if (splitIndex < 0)
            return;
        trackIndex = trackIndex < 0
            ? NearestTrackPoint(transform.position, 0, track.centerline.Count)
            : NearestTrackPoint(transform.position, trackIndex - 5, 26);
    }

    // Where the Super Fart shortcut leaves the main road: the bridge builder's entry anchor,
    // else the start of the branch, else the jump gate itself.
    static Vector3? FindSplit()
    {
        foreach (ShortcutBranch branch in FindObjectsByType<ShortcutBranch>(FindObjectsSortMode.None))
        {
            if (branch.requiredMix != FuelMixType.SuperFart)
                continue;
            BrokenBridgeBuilder builder = branch.GetComponent<BrokenBridgeBuilder>();
            if (builder != null && builder.entryAnchor != null)
                return builder.entryAnchor.position;
            if (branch.waypoints.Count > 0 && branch.waypoints[0].point != null)
                return branch.waypoints[0].point.position;
        }
        SuperFartJumpGate gate = FindFirstObjectByType<SuperFartJumpGate>();
        return gate != null ? gate.transform.position : (Vector3?)null;
    }

    int NearestTrackPoint(Vector3 position, int from, int span)
    {
        int count = track.centerline.Count;
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < span; i++)
        {
            int index = ((from + i) % count + count) % count;
            Vector3 difference = track.GetPoint(index) - position;
            difference.y = 0f;
            if (difference.sqrMagnitude < bestDistance)
            {
                bestDistance = difference.sqrMagnitude;
                best = index;
            }
        }
        return best;
    }

    // Returns false when the bag is full, in which case the cube stays on the track.
    public bool TryCollect(FuelType type)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return false;
        if (IsFull)
            return false;
        // Brushed past a can it doesn't want (e.g. the one next to its target): leave it there.
        if (onlyCollectWantedFuel && type != WantedFuel)
        {
            if (logFuelDecisions && Time.time - lastSkipLog > .5f)
            {
                lastSkipLog = Time.time;
                Debug.Log($"[AI] Left the {type} can it touched at track point {trackIndex} (wants {WantedFuel}).", this);
            }
            return false;
        }

        if (logFuelDecisions)
            Debug.Log($"[AI] Picked up {type} at track point {trackIndex} (own {ownFuel}, Super Fart fuel {SuperFartFuel}, " +
                      $"bag before: {(slots.Count == 0 ? "empty" : slots[0].ToString())}, fuel {fuel:0}).", this);
        slots.Add(type);
        if (type == ownFuel)
            AddFuel(pickupRefuel);
        else
            fart.Fart(pickupFartSeconds);

        // Old: always waited mixDelay before mixing.
        // if (slots.Count == Capacity)
        //     mixAt = Time.time + mixDelay;
        if (slots.Count == Capacity)
        {
            if (mixDelay <= 0f)
                MixSlots();
            else
                mixAt = Time.time + mixDelay;
        }
        Changed?.Invoke();
        return true;
    }

    // The player's mixing rules, with the AI's own fuel type standing in for the car's fuel.
    void MixSlots()
    {
        int own = 0;
        foreach (FuelType type in slots)
            if (type == ownFuel)
                own++;

        if (own == 2) HeldMix = FuelMix.Premium;
        else if (own == 1) HeldMix = FuelMix.Hybrid;
        // Old: any two of the same wrong fuel made Super Fart.
        // else if (slots[0] == slots[1]) HeldMix = FuelMix.SuperFart;
        else if (slots[0] == SuperFartFuel && slots[1] == SuperFartFuel) HeldMix = FuelMix.SuperFart;
        else HeldMix = FuelMix.ToxicSludge;

        if (HeldMix == FuelMix.SuperFart)
            Debug.Log($"[AI] Made Super Fart from two {SuperFartFuel} cans" +
                      (IsReservingSuperFart ? ", saving it for the jump." : "."), this);
        slots.Clear();
        heldSince = Time.time;
    }

    bool ShouldUseMix()
    {
        bool heldTooLong = Time.time - heldSince >= maxHoldSeconds;
        switch (HeldMix)
        {
            // Refuel when none of it would be wasted, or straight away if running low.
            case FuelMix.Premium:
                return fuel <= 100f - premiumRefuel || fuel < lowFuel;
            case FuelMix.Hybrid:
                return fuel <= 100f - hybridRefuel || fuel < lowFuel;
            // Save the big boost for a straight, where it gains the most.
            case FuelMix.SuperFart:
                // Old: a straight or the 4 s hold timeout always burnt it, even with the jump just ahead.
                // return cornerAhead < superFartOnStraightBelow || heldTooLong;
                if (IsReservingSuperFart)
                {
                    heldSince = Time.time;   // the hold timeout counts from when the reservation ends
                    return false;
                }
                return cornerAhead < superFartOnStraightBelow || heldTooLong;
            // Sludge has to be burnt to free the bag; a corner is where it costs the least.
            case FuelMix.ToxicSludge:
                return cornerAhead > sludgeInCornerAbove || heldTooLong;
            default:
                return false;
        }
    }

    void UseMix()
    {
        switch (HeldMix)
        {
            case FuelMix.Premium:
                AddFuel(premiumRefuel);
                break;
            case FuelMix.Hybrid:
                AddFuel(hybridRefuel);
                fart.Fart(pickupFartSeconds);
                break;
            case FuelMix.SuperFart:
                fart.Fart(superFartSeconds);
                break;
            case FuelMix.ToxicSludge:
                fart.Choke(chokeSeconds);
                break;
        }
        HeldMix = FuelMix.None;
        Changed?.Invoke();
    }

    // Same as FuelInventory.TryConsumeMix: spends the held mix on a shortcut instead of
    // its usual effect, and only if that exact mix is held.
    public bool TryConsumeMix(FuelMix mix)
    {
        if (mix == FuelMix.None || HeldMix != mix)
            return false;
        HeldMix = FuelMix.None;
        Changed?.Invoke();
        return true;
    }

    // What AIShortcutRouter reads to decide whether the AI may take a shortcut (see FuelMixConversion).
    FuelMixType IFuelMixHolder.HeldMix => HeldMix.ToShortcutMix();
    bool IFuelMixHolder.TryConsumeMix(FuelMixType mix) => TryConsumeMix(mix.ToFuelMix());

    void AddFuel(float amount)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return;
        fuel = Mathf.Min(100f, fuel + amount);
    }
}
