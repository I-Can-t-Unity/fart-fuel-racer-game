using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum FuelType { Correct, Green, Purple }

public enum FuelMix { None, Premium, Hybrid, SuperFart, ToxicSludge }

// The car's fuel bag. Each pickup takes a slot; with both slots full, Q mixes
// them and Q again uses the mix. There is no way to throw fuel away, so grabbing
// the wrong cubes means having to burn off whatever they mix into.
// F dumps the whole tank for a boost that lasts longer the more fuel was dumped.
[RequireComponent(typeof(FartBoost))]
public class FuelInventory : MonoBehaviour
{
    public const int Capacity = 2;

    [Tooltip("The car body. The correct fuel cube uses this exact material so it matches the car.")]
    public Renderer bodyRenderer;
    public int bodyMaterialSlot;

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
    // Short messages for the HUD, such as "+25 FUEL" or "BAG FULL".
    public event Action<string> Announced;

    public IReadOnlyList<FuelType> Slots => slots;
    public FuelMix HeldMix { get; private set; }
    public bool IsFull => HeldMix != FuelMix.None || slots.Count >= Capacity;

    readonly List<FuelType> slots = new List<FuelType>();
    readonly Dictionary<FuelType, Material> materials = new Dictionary<FuelType, Material>();
    FartBoost fart;
    bool tankWasEmpty;

    void Awake()
    {
        fart = GetComponent<FartBoost>();

        CarDriver driver = GetComponent<CarDriver>();
        if (driver != null)
            driver.AddControlsHint("Q: mix / use fuel    F: dump fuel");

        Material body = bodyRenderer != null ? bodyRenderer.sharedMaterials[bodyMaterialSlot] : null;
        materials[FuelType.Correct] = body != null ? body : MakeMaterial(Color.red);
        materials[FuelType.Green] = MakeMaterial(new Color(.3f, .9f, .25f));
        materials[FuelType.Purple] = MakeMaterial(new Color(.6f, .25f, .95f));
    }

    void Update()
    {
        bool tankEmpty = RaceManager.Instance != null && RaceManager.Instance.fuel <= 0;
        if (tankEmpty && !tankWasEmpty)
            Announced?.Invoke("OUT OF FUEL - COASTING");
        tankWasEmpty = tankEmpty;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.fKey.wasPressedThisFrame)
            DumpFuel();

        if (keyboard.qKey.wasPressedThisFrame)
        {
            if (HeldMix != FuelMix.None)
                UseMix();
            else if (slots.Count == Capacity)
                MixSlots();
        }
    }

    void DumpFuel()
    {
        float dumped = RaceManager.Instance != null ? RaceManager.Instance.DumpFuel() : 0;
        if (dumped <= 0)
            return;

        fart.Fart(Mathf.Lerp(dumpMinSeconds, dumpFullTankSeconds, dumped / 100f));
        tankWasEmpty = true;
        Announced?.Invoke($"DUMPED {dumped:0} FUEL!");
    }

    // Returns false when the bag is full, in which case the pickup stays on the track.
    public bool TryCollect(FuelType type)
    {
        if (IsFull)
        {
            Announced?.Invoke(HeldMix != FuelMix.None ? "BAG FULL - Q TO USE" : "BAG FULL - Q TO MIX");
            return false;
        }

        slots.Add(type);
        if (type == FuelType.Correct)
        {
            Refuel(pickupRefuel);
        }
        else
        {
            fart.Fart(pickupFartSeconds);
            Announced?.Invoke("FART BOOST!");
        }
        Changed?.Invoke();
        return true;
    }

    void MixSlots()
    {
        int correct = 0;
        foreach (FuelType type in slots)
            if (type == FuelType.Correct)
                correct++;

        if (correct == 2) HeldMix = FuelMix.Premium;
        else if (correct == 1) HeldMix = FuelMix.Hybrid;
        else if (slots[0] == slots[1]) HeldMix = FuelMix.SuperFart;
        else HeldMix = FuelMix.ToxicSludge;

        slots.Clear();
        Changed?.Invoke();
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

    void Refuel(float amount)
    {
        if (RaceManager.Instance != null)
            RaceManager.Instance.AddFuel(amount);
        Announced?.Invoke($"+{amount:0} FUEL");
    }

    public Material MaterialOf(FuelType type) => materials[type];

    public Color ColorOf(FuelType type) => materials[type].color;

    public string NameOf(FuelType type) => type == FuelType.Correct ? "CAR FUEL" : type.ToString().ToUpper();

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
        return new Material(shader) { color = color };
    }
}
