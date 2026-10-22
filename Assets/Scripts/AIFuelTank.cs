using System.Collections.Generic;
using UnityEngine;

// The AI rival's fuel. It works like the player's: a tank that drains with speed and
// throttle, a two-slot bag, and the same mixes. The difference is which fuel is "good":
// cubes of the rival's own fuel type refuel it (set from the main menu car choice by
// AICarWaypointDriver; Diesel, the green cubes, if that is turned off), every
// other cube is a fart boost. Like the player's bag, the second pickup mixes straight away;
// the AI decides for itself when to use the mix.
// AICarWaypointDriver adds this component at runtime, so nothing has to be set up in the scene.
public class AIFuelTank : MonoBehaviour
{
    public const int Capacity = FuelInventory.Capacity;

    // Old fuel names (Correct/Green/Purple), before the cars ran on Gasoline/Diesel/Ethanol:
    // [Tooltip("The cube colour that refuels the AI. The AI car is painted this colour.")]
    // public FuelType ownFuel = FuelType.Green;
    [Tooltip("The fuel the AI runs on; its cubes refuel it and the AI car is painted their colour. Diesel cubes are green.")]
    public FuelType ownFuel = FuelType.Diesel;
    public float fuel = 100f;

    [Header("Amounts (copied from the player's bag at the start, so both cars play by the same rules)")]
    public float pickupRefuel = 25f;
    public float pickupFartSeconds = .7f;
    public float premiumRefuel = 50f;
    public float hybridRefuel = 20f;
    public float superFartSeconds = 2.5f;
    public float chokeSeconds = 3f;

    [Header("Fuel drain (copied from the RaceManager at the start)")]
    public float drainPerSecond = 0f;
    public float drainPerSpeed = .025f;
    public float drainPerThrottle = 1.2f;

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

    // Set by AICarWaypointDriver every physics step: how sharp the road ahead is, 0 to 1.
    [HideInInspector] public float cornerAhead;

    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;
    public IReadOnlyList<FuelType> Slots => slots;
    public float FuelFraction => fuel / 100f;
    public FartBoost Fart => fart;

    readonly List<FuelType> slots = new List<FuelType>();
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

        RaceManager race = RaceManager.Instance;
        if (race != null)
        {
            drainPerSecond = race.fuelDrainPerSecond;
            drainPerSpeed = race.fuelDrainPerSpeed;
            drainPerThrottle = race.fuelDrainPerThrottle;
        }
    }

    void Update()
    {
        bool raceOver = RaceManager.Instance != null && RaceManager.Instance.IsFinished;
        if (!raceOver)
        {
            float speedKph = body != null ? body.linearVelocity.magnitude * 3.6f : 0;
            float throttle = driver != null ? Mathf.Abs(driver.Throttle) : 0;
            fuel = Mathf.Max(0, fuel -
                (drainPerSecond + speedKph * drainPerSpeed + throttle * drainPerThrottle) * Time.deltaTime);
        }

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
        }

        if (HeldMix != FuelMix.None && ShouldUseMix())
            UseMix();
    }

    // Returns false when the bag is full, in which case the cube stays on the track.
    public bool TryCollect(FuelType type)
    {
        if (IsFull)
            return false;

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
        else if (slots[0] == slots[1]) HeldMix = FuelMix.SuperFart;
        else HeldMix = FuelMix.ToxicSludge;

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
    }

    void AddFuel(float amount)
    {
        if (RaceManager.Instance != null && RaceManager.Instance.IsFinished)
            return;
        fuel = Mathf.Min(100f, fuel + amount);
    }
}
