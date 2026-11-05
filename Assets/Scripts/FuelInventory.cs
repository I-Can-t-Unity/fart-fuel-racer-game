using System;
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
            case FuelType.Gasoline:
                return new Color32(95, 58, 41, 255);
            case FuelType.Diesel:
                return new Color32(75, 230, 65, 255);
            case FuelType.Ethanol:
                return new Color32(153, 64, 242, 255);
            default:
                return Color.white;
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
            case FuelType.Gasoline:
                return "BROWN";
            case FuelType.Diesel:
                return "GREEN";
            case FuelType.Ethanol:
                return "PURPLE";
            default:
                return type.ToString().ToUpper();
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

    [Header("Dump fuel (F)")]
    [Tooltip("Boost length for dumping an almost empty tank and a full one; it scales with the fuel dumped.")]
    public float dumpMinSeconds = .5f;
    public float dumpFullTankSeconds = 3f;

    public event Action Changed;
    public event Action<string> Announced;

    public IReadOnlyList<FuelType> Slots => slots;
    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;

    readonly List<FuelType> slots = new List<FuelType>();
    readonly Dictionary<FuelType, Material> materials =
        new Dictionary<FuelType, Material>();

    FartBoost fart;
    bool tankWasEmpty;

    void Awake()
    {
        fart = GetComponent<FartBoost>();

        materials[FuelType.Gasoline] =
            MakeMaterial(FuelPalette.ColorOf(FuelType.Gasoline));

        materials[FuelType.Diesel] =
            MakeMaterial(FuelPalette.ColorOf(FuelType.Diesel));

        materials[FuelType.Ethanol] =
            MakeMaterial(FuelPalette.ColorOf(FuelType.Ethanol));

        Material body = bodyRenderer != null
            ? bodyRenderer.sharedMaterials[bodyMaterialSlot]
            : null;

        if (body != null)
            materials[carFuel] = body;
    }

    public bool IsSuitable(FuelType type) => type == carFuel;

    public FuelType SuperFartFuel => SuperFartRecipe.FuelFor(carFuel);

    void Update()
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return;

        bool tankEmpty = RaceManager.Instance != null
            && RaceManager.Instance.fuel <= 0;

        if (tankEmpty && !tankWasEmpty)
            Announced?.Invoke("OUT OF FUEL - COASTING");

        tankWasEmpty = tankEmpty;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.fKey.wasPressedThisFrame)
            DumpFuel();

        if (keyboard.qKey.wasPressedThisFrame && HeldMix != FuelMix.None)
            UseMix();
    }

    void DumpFuel()
    {
        float dumped = RaceManager.Instance != null
            ? RaceManager.Instance.DumpFuel()
            : 0;

        if (dumped <= 0)
            return;

        fart.Fart(Mathf.Lerp(
            dumpMinSeconds,
            dumpFullTankSeconds,
            dumped / 100f));

        tankWasEmpty = true;
        Announced?.Invoke($"DUMPED {dumped:0} FUEL!");
    }

    public bool TryCollect(FuelType type)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return false;

        if (IsFull)
        {
            Announced?.Invoke("BAG FULL - Q TO USE");
            return false;
        }

        slots.Add(type);

        if (IsSuitable(type))
        {
            Refuel(pickupRefuel);
        }
        else
        {
            fart.Fart(pickupFartSeconds);
            Announced?.Invoke("FART BOOST!");
        }

        if (slots.Count == Capacity)
            MixSlots();

        Changed?.Invoke();
        return true;
    }

    void MixSlots()
    {
        int suitable = 0;

        foreach (FuelType type in slots)
        {
            if (IsSuitable(type))
                suitable++;
        }

        if (suitable == 2)
            HeldMix = FuelMix.Premium;
        else if (suitable == 1)
            HeldMix = FuelMix.Hybrid;
        else if (slots[0] == SuperFartFuel && slots[1] == SuperFartFuel)
            HeldMix = FuelMix.SuperFart;
        else
            HeldMix = FuelMix.ToxicSludge;

        slots.Clear();
    }

    void UseMix()
    {
        switch (HeldMix)
        {
            case FuelMix.Premium:
                Refuel(premiumRefuel);
                break;

            case FuelMix.Hybrid:
                Refuel(hybridRefuel);
                fart.Fart(pickupFartSeconds);
                break;

            case FuelMix.SuperFart:
                fart.Fart(superFartSeconds);
                Announced?.Invoke("SUPER FART!");
                break;

            case FuelMix.ToxicSludge:
                fart.Choke(chokeSeconds);
                Announced?.Invoke("ENGINE CHOKED!");
                break;
        }

        HeldMix = FuelMix.None;
        Changed?.Invoke();
    }

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

    void Refuel(float amount)
    {
        if (RaceManager.Instance != null)
            RaceManager.Instance.AddFuel(amount);

        Announced?.Invoke($"+{amount:0} FUEL");
    }

    public Material MaterialOf(FuelType type) => materials[type];

    public Color ColorOf(FuelType type) => FuelPalette.ColorOf(type);

    public string NameOf(FuelType type) => type.ToString().ToUpper();

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