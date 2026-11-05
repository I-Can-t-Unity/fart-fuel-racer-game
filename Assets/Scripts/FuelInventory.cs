using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum FuelType { Gasoline, Diesel, Ethanol }

// Shared fuel identity colors for vehicles, pickups and UI.
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

// Each car has one wrong fuel that is its Super Fart fuel: two pickups of it in a row
// make Super Fart. Brown (Gasoline) cars use purple (Ethanol), green (Diesel) cars use
// brown (Gasoline), purple (Ethanol) cars use green (Diesel). The player's bag and the
// AI's tank both use this, so every car plays by the same rule.
public static class SuperFartRecipe
{
    public static FuelType FuelFor(FuelType carFuel)
    {
        int count = Enum.GetValues(typeof(FuelType)).Length;
        return (FuelType)(((int)carFuel + 2) % count);
    }

    // The colour players know each fuel by, for HUD text.
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

// The shortcut system (ShortcutCore) names mixes with its own FuelMixType. These convert
// between the two so both bags can be read through IFuelMixHolder. No shortcut takes
// Toxic Sludge, so to the shortcuts it is no mix at all.
public static class FuelMixConversion
{
    public static FuelMixType ToShortcutMix(this FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.SuperFart: return FuelMixType.SuperFart;
            case FuelMix.Hybrid: return FuelMixType.Hybrid;
            case FuelMix.Premium: return FuelMixType.Premium;
            default: return FuelMixType.None;
        }
    }

    public static FuelMix ToFuelMix(this FuelMixType mix)
    {
        switch (mix)
        {
            case FuelMixType.SuperFart: return FuelMix.SuperFart;
            case FuelMixType.Hybrid: return FuelMix.Hybrid;
            case FuelMixType.Premium: return FuelMix.Premium;
            default: return FuelMix.None;
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

    public CarDriver CurrentCar =>currentCar;
    public FuelType StreakColor =>streakColor;
    public int StreakNum =>streakNum;
    public bool IsImmune => Time.time < immunityEndingTime;
    public string LastResult => lastResult;

    public event Action Changed;
    // Short messages for the HUD, such as "+25 FUEL" or "BAG FULL".
    public event Action<string> Announced;

    public event Action<FuelType, FuelType> Switched;

    public IReadOnlyList<FuelType> Slots => slots;
    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;

    readonly List<FuelType> slots = new List<FuelType>();
    readonly Dictionary<FuelType, Material> materials = new Dictionary<FuelType, Material>();
    FartBoost fart;

    void Awake()
    {
        fart = GetComponent<FartBoost>();
        materials[FuelType.Gasoline] = MakeMaterial(FuelPalette.ColorOf(FuelType.Gasoline));
        materials[FuelType.Diesel] = MakeMaterial(FuelPalette.ColorOf(FuelType.Diesel));
        materials[FuelType.Ethanol] = MakeMaterial(FuelPalette.ColorOf(FuelType.Ethanol));
    }

    public bool IsSuitable(FuelType type) => type == carFuel;

    // The one wrong fuel that makes Super Fart for this car (two in a row).
    public FuelType SuperFartFuel => SuperFartRecipe.FuelFor(carFuel);

    void Start()
    {
        if(cars.Length == 0)
        {
            currentCar = GetComponent<CarDriver>();
            return;
        }
        followCamera = FindFirstObjectByType<CarFollowCamera>();
        gauge = FindFirstObjectByType<GaugeAnimator>();
        for(int i = 0; i < cars.Length; i++)
        {
            if(i != (int)carFuel)
            {
                cars[i].ExternalInput = true;
                cars[i].GetComponent<AICarWaypointDriver>().enabled = true;
            }
        }
        Control(cars[(int)carFuel]);
        Changed?.Invoke();
    }

    void Control(CarDriver car)
    {
        car.ExternalInput = false;
        car.GetComponent<AICarWaypointDriver>().enabled = false;
        currentCar = car;
        carFuel = (FuelType)Array.IndexOf(cars, car);
        fart = car.GetComponent<FartBoost>();
        followCamera.SetTarget(car.transform);
        RaceManager.Instance.car = car.transform;
        RaceManager.Instance.carBody = car.GetComponent<Rigidbody>();
        gauge.carBody = car.GetComponent<Rigidbody>();
    }

    void SwitchCar(FuelType type)
    {
        FuelType oldColor = carFuel;
        CarDriver oldCar = currentCar;
        Control(cars[(int)type]);
        oldCar.ExternalInput = true;
        oldCar.GetComponent<AICarWaypointDriver>().enabled = true;
        streakNum = 0;
        immunityEndingTime = Time.time + switchImmunityTime;
        Announce($"YOU ARE NOW {NameOf(type)}");
        Switched?.Invoke(oldColor, type);
    }

    public bool TryCollect(FuelType type)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return false;
        if(type == carFuel)
        {
            fart.Fart(pickupFartSeconds);
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
            lastResult = "switch";
            SwitchCar(streakColor);
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

    // The shortcut system's view of the bag (see FuelMixConversion).
    FuelMixType IFuelMixHolder.HeldMix => HeldMix.ToShortcutMix();
    bool IFuelMixHolder.TryConsumeMix(FuelMixType mix) => TryConsumeMix(mix.ToFuelMix());

    // Lets other parts of the race (such as shortcut gates) put a message on the HUD.
    public void Announce(string message) => Announced?.Invoke(message);

    public Material MaterialOf(FuelType type) => materials[type];

    public Color ColorOf(FuelType type) => FuelPalette.ColorOf(type);

    public string NameOf(FuelType type) => SuperFartRecipe.ColorNameOf(type);

    // Fuel identity stays consistent; the effect text explains suitability.
    public Color LabelColorOf(FuelType type) => FuelPalette.ColorOf(type);

    // What picking up this fuel does on its own, for the label above each cube.
    // Old: every wrong fuel just said "Fart boost".
    // public string PickupEffectOf(FuelType type) => IsSuitable(type) ? $"+{pickupRefuel:0} fuel" : "Fart boost";
    public string PickupEffectOf(FuelType type) =>
        IsSuitable(type) ? $"+{pickupRefuel:0} fuel" : type == SuperFartFuel ? "Super Fart fuel" : "Fart boost";

    // HUD line naming this car's Super Fart fuel, e.g. "SUPER FART: 2 PURPLE (ETHANOL) IN A ROW".
    public string SuperFartRecipeText =>
        $"SUPER FART: 2 {SuperFartRecipe.ColorNameOf(SuperFartFuel)} ({NameOf(SuperFartFuel)}) IN A ROW";

    public Color ColorOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium: return new Color(1f, .8f, .1f);
            case FuelMix.Hybrid: return new Color(.2f, .85f, .9f);
            case FuelMix.SuperFart: return new Color(1f, .35f, .7f);
            case FuelMix.ToxicSludge: return new Color(.25f, .25f, .2f);
            default: return Color.clear;
        }
    }

    public string NameOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium: return "PREMIUM FUEL";
            case FuelMix.Hybrid: return "HYBRID FUEL";
            case FuelMix.SuperFart: return "SUPER FART";
            case FuelMix.ToxicSludge: return "TOXIC SLUDGE";
            default: return "";
        }
    }

    public string EffectOf(FuelMix mix)
    {
        switch (mix)
        {
            case FuelMix.Premium: return $"+{premiumRefuel:0} fuel";
            case FuelMix.Hybrid: return $"+{hybridRefuel:0} fuel and a small fart boost";
            case FuelMix.SuperFart: return "Huge fart boost";
            case FuelMix.ToxicSludge: return $"Engine chokes for {chokeSeconds:0}s";
            default: return "";
        }
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader) { color = color };
        // Match the matte vehicle and tutorial pickup materials.
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
        if (material.HasProperty("_GlossyReflections")) material.SetFloat("_GlossyReflections", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        return material;
    }
}
