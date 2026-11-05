using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum FuelType { Gasoline, Diesel, Ethanol }

public static class FuelPalette
{
    public static Color ColorOf(FuelType type)
    {
        switch (type)
        {
            case FuelType.Gasoline: return new Color32(255, 0, 0, 255); // red
            case FuelType.Diesel: return new Color32(0, 0, 255, 255); // blue
            case FuelType.Ethanol: return new Color32(255, 255, 0, 255); // yellow
            default: return Color.white;
        }
    }
}

public enum FuelMix { None, Premium, Hybrid, SuperFart, ToxicSludge }

public static class SuperFartRecipe
{
    public static FuelType FuelFor(FuelType carFuel)
    {
        int count = Enum.GetValues(typeof(FuelType)).Length;
        return (FuelType)(((int)carFuel + 2) % count);
    }

    public static string ColorNameOf(FuelType type)
    {
        switch (type)
        {
            case FuelType.Gasoline: return "RED";
            case FuelType.Diesel: return "BLUE";
            case FuelType.Ethanol: return "YELLOW";
            default: return type.ToString().ToUpper();
        }
    }
}

public static class FuelMixConversion
{
    public static FuelMixType ToShortcutMix(this FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.SuperFart:
                return FuelMixType.SuperFart;
            case FuelMix.Hybrid:
                return FuelMixType.Hybrid;
            case FuelMix.Premium:
                return FuelMixType.Premium;
            default:
                return FuelMixType.None;
        }
    }

    public static FuelMix ToFuelMix(this FuelMixType mix)
    {
        switch (mix)
        {
            case FuelMixType.SuperFart:
                return FuelMix.SuperFart;
            case FuelMixType.Hybrid:
                return FuelMix.Hybrid;
            case FuelMixType.Premium:
                return FuelMix.Premium;
            default:
                return FuelMix.None;
        }
    }
}

[RequireComponent(typeof(FartBoost))]
public class FuelInventory : MonoBehaviour, IFuelMixHolder
{
    public const int Capacity = 2;

    [Tooltip("The fuel this car runs on. Every other fuel type counts as wrong fuel.")]
    public FuelType carFuel = FuelType.Gasoline;

    [Tooltip("The car body. Cubes of the car's own fuel use this exact material so they match the car.")]
    public Renderer bodyRenderer;

    public int bodyMaterialSlot;

    [Header("Labels")]
    [Tooltip("Dark plate behind the fuel-colored labels so they stand out against the ground.")]
    public Color labelBackground = new Color(0, 0, 0, .55f);

    [Header("Pickups")]
    public float pickupRefuel = 25f;
    public float pickupFartSeconds = .7f;

    [Header("Mixes")]
    public float premiumRefuel = 50f;
    public float hybridRefuel = 20f;
    public float superFartSeconds = 2.5f;
    public float chokeSeconds = 3f;

    [Header("Switching")]
    public CarDriver[] cars;
    public float switchImmunityTime = 4f;

    private CarDriver currentCar;
    private FuelType streakColor;
    private int streakNum = 0;
    private float immunityEndingTime = 0;
    private string lastResult = "";
    private CarFollowCamera followCamera;
    private GaugeAnimator gauge;
    private RaceManager race;

    public CarDriver CurrentCar =>currentCar;
    public FuelType StreakColor =>streakColor;
    public int StreakNum =>streakNum;
    public bool IsImmune => Time.time < immunityEndingTime;
    public string LastResult => lastResult;

    public event Action Changed;
    public event Action<string> Announced;

    public event Action<FuelType, FuelType> Switched;

    public IReadOnlyList<FuelType> Slots => slots;
    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;

    readonly List<FuelType> slots = new List<FuelType>();
    readonly Dictionary<FuelType, Material> materials =
        new Dictionary<FuelType, Material>();

    FartBoost fart;

    void Awake()
    {
        fart = GetComponent<FartBoost>();
        materials[FuelType.Gasoline] = MakeMaterial(FuelPalette.ColorOf(FuelType.Gasoline));
        materials[FuelType.Diesel] = MakeMaterial(FuelPalette.ColorOf(FuelType.Diesel));
        materials[FuelType.Ethanol] = MakeMaterial(FuelPalette.ColorOf(FuelType.Ethanol));
    }

    public bool IsSuitable(FuelType type) => type == carFuel;

    public FuelType SuperFartFuel => SuperFartRecipe.FuelFor(carFuel);

    IEnumerator Start()
    {
        if (cars == null || cars.Length == 0)
        {
            currentCar = GetComponent<CarDriver>();
            yield break;
        }

        race = RaceManager.Instance;
        if (race == null)
        {
            Debug.LogError("The switching controller needs a RaceManager.", this);
            yield break;
        }
        while (!race.RacersInitialized) yield return null;
        if (cars.Length != race.Racers.Count)
        {
            Debug.LogError("FuelInventory.Cars and RaceManager.Racers must match in RED/BLUE/YELLOW order.", this);
            yield break;
        }
        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null || cars[i] != race.Racers[i])
            {
                Debug.LogError("Switching and race roster order differs at color " + i, this);
                yield break;
            }
        }
        followCamera = FindFirstObjectByType<CarFollowCamera>();
        gauge = FindFirstObjectByType<GaugeAnimator>();
        race.ControlledCarChanged += OnControlledCarChanged;
        OnControlledCarChanged(null, race.CurrentPlayerCar);
    }

    void OnControlledCarChanged(Transform previous, Transform next)
    {
        if (currentCar != null) currentCar.Respawned -= ResetStreak;
        currentCar = next != null ? next.GetComponent<CarDriver>() : null;
        if (currentCar == null) return;
        int index = Array.IndexOf(cars, currentCar);
        if (index >= 0) carFuel = (FuelType)index;
        fart = currentCar.GetComponent<FartBoost>();
        currentCar.Respawned += ResetStreak;
        Changed?.Invoke();
    }

    void ResetStreak()
    {
        streakNum = 0;
        lastResult = "reset";
        Changed?.Invoke();
    }

    // Change input ownership; positions, velocities and checkpoints stay on each car.
    bool Control(CarDriver next)
    {
        if (race == null || !race.IsRacing || next == null || currentCar == null
            || race.ResolveRacer(next.transform) != next) return false;
        if (next == currentCar) return false;
        AICarWaypointDriver nextAI = next.GetComponent<AICarWaypointDriver>();
        AICarWaypointDriver previousAI = currentCar.GetComponent<AICarWaypointDriver>();
        if (nextAI == null || previousAI == null)
        {
            Debug.LogError("Every switchable car needs an AI driver for handoff.", this);
            return false;
        }
        CarDriver previous = currentCar;
        next.SetInput(0, 0, false);
        previous.SetInput(0, 0, false);
        nextAI.PrepareControlHandoff();
        nextAI.enabled = false;
        next.ExternalInput = false;
        previous.ExternalInput = true;
        previousAI.PrepareControlHandoff();
        previousAI.enabled = true;
        // Single notification updates speed, placement, cameras and inventory identity.
        if (!race.SetControlledCar(next.transform)) return false;
        if (followCamera != null) followCamera.SetTarget(next.transform);
        if (gauge != null) gauge.carBody = next.GetComponent<Rigidbody>();
        return true;
    }

    // An AI grabbed two of the player's cubes in a row, so it takes the player's car and the
    // player ends up in the AI's. Goes through the normal switch, so the protection time works too.
    public bool ForceSwitchTo(CarDriver aiCar)
    {
        int index = cars != null ? Array.IndexOf(cars, aiCar) : -1;
        if (index < 0 || IsImmune || aiCar == currentCar)
            return false;
        FuelType lost = carFuel;
        if (!SwitchCar((FuelType)index))
            return false;
        Announce($"{NameOf(lost)} CAR TAKEN! YOU ARE NOW {NameOf((FuelType)index)}");
        return true;
    }

    bool SwitchCar(FuelType type)
    {
        int index = (int)type;
        if (cars == null || index < 0 || index >= cars.Length) return false;
        FuelType oldColor = carFuel;
        if (!Control(cars[index])) return false;
        streakNum = 0;
        immunityEndingTime = Time.time + switchImmunityTime;
        Announce($"YOU ARE NOW {NameOf(type)}");
        Switched?.Invoke(oldColor, type);
        return true;
    }

    void OnDestroy()
    {
        if (race != null) race.ControlledCarChanged -= OnControlledCarChanged;
        if (currentCar != null) currentCar.Respawned -= ResetStreak;
    }

    public bool TryCollect(FuelType type)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return false;
        if (currentCar == null || (int)type < 0 || (int)type > 2) return false;
        if(type == carFuel)
        {
            if (fart != null) fart.Fart(pickupFartSeconds);
            streakNum = 0;
            lastResult = "boost";
            Announce("BOOST!");
        }
        else if(IsImmune)
        {
            lastResult = "protected";
            return false;
        }
        else if(streakNum == 1 && type == streakColor)
        {
            if (!SwitchCar(streakColor)) return false;
            lastResult = "switch";
        }
        else
        {
            streakColor = type;
            streakNum = 1;
            lastResult = "streak";
            Announce($"{NameOf(type)} 1/2");
        }
        Changed?.Invoke();
        return true;
    }

    // Spends the held mix on something other than its Q effect, such as unlocking
    // a shortcut. False, with the bag left as it is, unless that exact mix is held.
    // Q goes through HeldMix too, so a single mix can only ever be spent once.
    public bool TryConsumeMix(FuelMix mix)
    {
        if (mix == FuelMix.None || HeldMix != mix)
            return false;

        HeldMix = FuelMix.None;
        Changed?.Invoke();
        return true;
    }

    FuelMixType IFuelMixHolder.HeldMix => HeldMix.ToShortcutMix();

    bool IFuelMixHolder.TryConsumeMix(FuelMixType mix)
        => TryConsumeMix(mix.ToFuelMix());

    public void Announce(string message)
        => Announced?.Invoke(message);

    public Material MaterialOf(FuelType type) => materials[type];

    public Color ColorOf(FuelType type) => FuelPalette.ColorOf(type);

    public string NameOf(FuelType type) => SuperFartRecipe.ColorNameOf(type);

    public Color LabelColorOf(FuelType type) => FuelPalette.ColorOf(type);

    public string PickupEffectOf(FuelType type) =>
        IsSuitable(type)
            ? $"+{pickupRefuel:0} fuel"
            : type == SuperFartFuel
                ? "Super Fart fuel"
                : "Fart boost";

    public string SuperFartRecipeText =>
        $"SUPER FART: 2 {SuperFartRecipe.ColorNameOf(SuperFartFuel)} ({NameOf(SuperFartFuel)}) IN A ROW";

    public Color ColorOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium:
                return new Color(1f, .8f, .1f);
            case FuelMix.Hybrid:
                return new Color(.2f, .85f, .9f);
            case FuelMix.SuperFart:
                return new Color(1f, .35f, .7f);
            case FuelMix.ToxicSludge:
                return new Color(.25f, .25f, .2f);
            default:
                return Color.clear;
        }
    }

    public string NameOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium:
                return "PREMIUM FUEL";
            case FuelMix.Hybrid:
                return "HYBRID FUEL";
            case FuelMix.SuperFart:
                return "SUPER FART";
            case FuelMix.ToxicSludge:
                return "TOXIC SLUDGE";
            default:
                return "";
        }
    }

    public string EffectOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium:
                return $"+{premiumRefuel:0} fuel";
            case FuelMix.Hybrid:
                return $"+{hybridRefuel:0} fuel and a small fart boost";
            case FuelMix.SuperFart:
                return "Huge fart boost";
            case FuelMix.ToxicSludge:
                return $"Engine chokes for {chokeSeconds:0}s";
            default:
                return "";
        }
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader) { color = color };

        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0f);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0f);

        if (material.HasProperty("_SpecularHighlights"))
            material.SetFloat("_SpecularHighlights", 0f);

        if (material.HasProperty("_EnvironmentReflections"))
            material.SetFloat("_EnvironmentReflections", 0f);

        if (material.HasProperty("_GlossyReflections"))
            material.SetFloat("_GlossyReflections", 0f);

        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");

        return material;
    }
}