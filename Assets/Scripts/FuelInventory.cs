using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    internal const float PickupBoostSeconds = 2f;
    internal const float ShieldSeconds = 8f;

    [Tooltip("The fuel this car runs on. Every other fuel type counts as wrong fuel.")]
    public FuelType carFuel = FuelType.Gasoline;

    [Tooltip("The car body. Cubes of the car's own fuel use this exact material so they match the car.")]
    public Renderer bodyRenderer;

    public int bodyMaterialSlot;

    [Header("Pickups")]
    public float pickupRefuel = 25f;

    [Header("Mixes")]
    public float premiumRefuel = 50f;
    public float hybridRefuel = 20f;
    public float superFartSeconds = 2.5f;
    public float chokeSeconds = 3f;

    [Header("Switching")]
    public CarDriver[] cars;
    public float switchImmunityTime = 4f;

    [Header("Charges (own colour = +1, max 3)")]
    public int maxCharges = 3;

    [Header("Shield (3 charges = shield for a few seconds)")]
    [Tooltip("Your ShieldBubble prefab. It is put on the car and tinted to the car's colour.")]
    public GameObject shieldVisual;
    [Tooltip("The ring flashes for this many seconds before it runs out (faster and faster).")]
    public float shieldBlinkSeconds = 3f;

    private CarDriver currentCar;
    private FuelType streakColor;
    private int streakNum = 0;
    private float immunityEndingTime = 0;
    private string lastResult = "";
    private CarFollowCamera followCamera;
    private GaugeAnimator gauge;
    private RaceManager race;

    // Charges belong to the CAR, so swapping into a car takes over its stash.
    readonly Dictionary<CarDriver, int> charges = new Dictionary<CarDriver, int>();
    // Shields also belong to the car: when each car's shield runs out, and its bubble.
    readonly Dictionary<CarDriver, float> shieldEnds = new Dictionary<CarDriver, float>();
    readonly Dictionary<CarDriver, GameObject> bubbles = new Dictionary<CarDriver, GameObject>();

    public CarDriver CurrentCar =>currentCar;
    public int Charges => ChargesOf(currentCar);
    public bool IsShielded => IsShieldedCar(currentCar);
    public int ChargesOf(CarDriver car) => car != null && charges.TryGetValue(car, out int c) ? c : 0;
    public bool IsShieldedCar(CarDriver car) => car != null && shieldEnds.TryGetValue(car, out float end) && Time.time < end;
    public float ShieldTimeLeft => currentCar != null && shieldEnds.TryGetValue(currentCar, out float end) ? Mathf.Max(0f, end - Time.time) : 0f;
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
    private string cause;
    public string Cause => cause;
    public event Action<bool> ShieldStarted;
    public event Action<bool> ShieldBlocked;
    public event Action TurboUsed;


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

    FuelType ColorForRacerIndex(int index) => race != null ? race.ColorForRacerIndex(index) : (FuelType)index;

    void OnControlledCarChanged(Transform previous, Transform next)
    {
        if (currentCar != null) currentCar.Respawned -= ResetStreak;
        currentCar = next != null ? next.GetComponent<CarDriver>() : null;
        if (currentCar == null) return;
        int index = Array.IndexOf(cars, currentCar);
        if (index >= 0) carFuel = ColorForRacerIndex(index);
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

    void Update() => UpdateShields();

    void SetCharges(CarDriver car, int value)
    {
        if (car == null) return;
        charges[car] = Mathf.Clamp(value, 0, maxCharges);
    }

    // +1 charge. The 3rd one turns into a shield and the charges start again from 0.
    void AddCharge(CarDriver car)
    {
        SetCharges(car, ChargesOf(car) + 1);
        if (ChargesOf(car) >= maxCharges) StartShield(car);
    }

    void StartShield(CarDriver car)
    {
        SetCharges(car, 0);
        shieldEnds[car] = Time.time + ShieldSeconds;
        ShowBubble(car, true);
        Announce(car == currentCar
            ? $"{Paint(car, "SHIELD ON!")}  {ShieldSeconds:0}s"
            : $"{Paint(car, NameOfCar(car) + " HAS A SHIELD!")}");
        ShieldStarted?.Invoke(car == currentCar);
        Changed?.Invoke();
    }

    void EndShield(CarDriver car, bool announce)
    {
        shieldEnds.Remove(car);
        ShowBubble(car, false);
        if (announce && car == currentCar) Announce(Paint(car, "SHIELD OFF"));
        Changed?.Invoke();
    }

    internal void ResetTutorialCharges(CarDriver car)
    {
        SetCharges(car, 0);
        if (IsShieldedCar(car)) EndShield(car, false);
        else Changed?.Invoke();
    }

    // Runs every frame: switches bubbles off when time is up, blinks them near the end.
    void UpdateShields()
    {
        if (shieldEnds.Count == 0) return;
        foreach (var car in new List<CarDriver>(shieldEnds.Keys))
        {
            float left = shieldEnds[car] - Time.time;
            if (left <= 0f) { EndShield(car, true); continue; }
            if (bubbles.TryGetValue(car, out GameObject bubble) && bubble != null)
            {
                // Flashes in the last few seconds, faster as it runs out (4 -> 12 flashes a second).
                bool visible = true;
                if (left < shieldBlinkSeconds)
                {
                    float rate = Mathf.Lerp(12f, 4f, left / shieldBlinkSeconds);
                    visible = Mathf.Repeat(Time.time * rate, 1f) < .55f;
                }
                // Hide only the visible parts, so the spin animation keeps running smoothly.
                foreach (var r in bubble.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled != visible) r.enabled = visible;
            }
        }
    }

    // Puts a copy of the ShieldBubble prefab on the car (once) and tints it to the car's colour.
    void ShowBubble(CarDriver car, bool on)
    {
        if (car == null) return;
        if (!bubbles.TryGetValue(car, out GameObject bubble) || bubble == null)
        {
            if (!on) return;
            if (shieldVisual == null) { Debug.LogWarning("Drag your ShieldBubble prefab into FuelInventory → Shield Visual.", this); return; }
            bubble = Instantiate(shieldVisual, car.transform);
            bubble.name = "ShieldBubble";
            bubble.transform.localPosition = shieldVisual.transform.localPosition;
            bubble.transform.localRotation = shieldVisual.transform.localRotation;
            foreach (var c in bubble.GetComponentsInChildren<Collider>()) Destroy(c);
            int index = cars != null ? Array.IndexOf(cars, car) : -1;
            if (index >= 0) Tint(bubble, FuelPalette.ColorOf(ColorForRacerIndex(index)));
            bubbles[car] = bubble;
        }
        bubble.SetActive(on);
        if (on) foreach (var r in bubble.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
    }

    // Colours the shield in the car's colour, whatever it is made of: a mesh (Renderer),
    // a Line Renderer or a Particle System. The prefab's see-through amount is kept.
    static void Tint(GameObject root, Color tint)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is LineRenderer line)
            {
                line.startColor = new Color(tint.r, tint.g, tint.b, line.startColor.a);
                line.endColor = new Color(tint.r, tint.g, tint.b, line.endColor.a);
            }
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)
            {
                foreach (var prop in new[] { "_BaseColor", "_Color" })
                    if (m.HasProperty(prop))
                    {
                        float a = m.GetColor(prop).a;
                        m.SetColor(prop, new Color(tint.r, tint.g, tint.b, a));
                    }
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", tint * 2f);
                }
            }
        }
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = tint;
        }
    }

    // Wraps a message in the car's colour (a bit lighter so dark blue still reads).
    string Paint(CarDriver car, string text)
    {
        int index = cars != null ? Array.IndexOf(cars, car) : -1;
        if (index < 0) return text;
        Color c = Color.Lerp(FuelPalette.ColorOf(ColorForRacerIndex(index)), Color.white, .3f);
        return $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";
    }

    string NameOfCar(CarDriver car)
    {
        int index = cars != null ? Array.IndexOf(cars, car) : -1;
        return index >= 0 ? NameOf(ColorForRacerIndex(index)) : "RIVAL";
    }

    /// Uses up a shield on something else (e.g. a bridge gate). False if not shielded.
    public bool TrySpendCharges(CarDriver car)
    {
        if (!IsShieldedCar(car)) return false;
        EndShield(car, false);
        return true;
    }

    /// Call from the AI when an AI car takes a block. Same rules as the player:
    /// its own colour = +1 charge; the player's colour = it swaps with the player.
    public void AIPickedUp(CarDriver aiCar, FuelType type)
    {
        int index = cars != null ? Array.IndexOf(cars, aiCar) : -1;
        if (index < 0) return;
        if (ColorForRacerIndex(index) == type)
        {
            AddCharge(aiCar);
            Changed?.Invoke();
        }
        else if (type == carFuel)
            ForceSwitchTo(aiCar);
    }

    // Change input ownership; positions, velocities and checkpoints stay on each car.
    bool Control(CarDriver next)
    {
        if (race == null || !race.IsRacing || next == null || currentCar == null
            || race.ResolveRacer(next.transform) != next) return false;
        if (race.IsProgressionRace && next.IsStopped) return false;
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

    // An AI grabbed a cube of the player's colour, so it takes the player's car and the
    // player ends up in the AI's. Goes through the normal switch, so the protection time works too.
    public bool ForceSwitchTo(CarDriver aiCar)
    {
        cause = "stolen";
        int index = cars != null ? Array.IndexOf(cars, aiCar) : -1;
        if (index < 0 || IsImmune || aiCar == currentCar)
            return false;
        // Either car's active shield can block the swap.
        FuelType rivalColor = ColorForRacerIndex(index);
        if (BlockedByShield(currentCar, aiCar, rivalColor)) return false;
        FuelType lost = carFuel;
        if (!SwitchCar(rivalColor))
            return false;
        Announce($"{NameOf(lost)} CAR STOLEN! YOU'RE {NameOf(rivalColor)} NOW");
        return true;
    }

    // Shield rule: while the car you're in, or the car you'd swap with, has its shield up,
    // swaps are blocked. The shield keeps going until its time runs out.
    bool BlockedByShield(CarDriver mine, CarDriver theirs, FuelType theirColor)
    {
        bool playerShield = IsShieldedCar(mine);
        if (!playerShield && !IsShieldedCar(theirs))
            return false;

        immunityEndingTime = Time.time + 1f;
        if (followCamera != null) followCamera.ShakeForBlockedSwap();
        Announce(playerShield ? Paint(mine, "SHIELD SAID NO!")
            : Paint(theirs, $"{NameOf(theirColor)}'S SHIELD BLOCKED YOU!"));
        Changed?.Invoke();
        ShieldBlocked?.Invoke(playerShield);
        return true;
    }

    bool SwitchCar(FuelType type)
    {
        if (race == null) return false;
        int index = race.RacerIndexForColor(type);
        if (cars == null || index < 0 || index >= cars.Length) return false;
        FuelType oldColor = carFuel;
        if (!Control(cars[index])) return false;
        streakNum = 0;
        immunityEndingTime = Time.time + switchImmunityTime;
        Announce($"SWITCHEROO! YOU'RE {NameOf(type)} NOW");
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
        // Rule 1: your colour = speed boost + 1 charge (max 3).
        if(type == carFuel)
        {
            if (fart != null) fart.Fart(PickupBoostSeconds);
            lastResult = "boost";
            if (Charges + 1 < maxCharges) Announce($"BOOST!  CHARGE {Charges + 1}/{maxCharges}");
            AddCharge(currentCar);   // the 3rd charge announces SHIELD ON itself
            TurboUsed?.Invoke();
        }
        // 4 s after a swap nothing can swap you again; the block stays on the track.
        else if(IsImmune)
        {
            lastResult = "protected";
            return false;
        }
        // Rule 2: a rival's colour = you swap cars with that rival (unless a shield blocks it).
        else
        {
            cause = "player";
            int index = race != null ? race.RacerIndexForColor(type) : (int)type;
            CarDriver target = cars != null && index < cars.Length ? cars[index] : null;
            if (BlockedByShield(currentCar, target, type))
            {
                lastResult = "shielded";
            }
            else
            {
                if (!SwitchCar(type)) return false;
                lastResult = "switch";
            }
        }
        Changed?.Invoke();
        return true;
    }

    // Spends the held mix at a shortcut if the requested mix is available.
    // Shortcut gates (e.g. the bridge jump) ask for "Super Fart": that now means a full set of charges.
    public bool TryConsumeMix(FuelMix mix)
    {
        if (mix == FuelMix.SuperFart) return TrySpendCharges(currentCar);
        if (mix == FuelMix.None || HeldMix != mix)
            return false;

        HeldMix = FuelMix.None;
        Changed?.Invoke();
        return true;
    }

    FuelMixType IFuelMixHolder.HeldMix => IsShielded ? FuelMixType.SuperFart : HeldMix.ToShortcutMix();

    bool IFuelMixHolder.TryConsumeMix(FuelMixType mix)
        => TryConsumeMix(mix.ToFuelMix());

    public void Announce(string message)
        => Announced?.Invoke(message);

    public Material MaterialOf(FuelType type) => materials[type];

    public Color ColorOf(FuelType type) => FuelPalette.ColorOf(type);

    public string NameOf(FuelType type) => SuperFartRecipe.ColorNameOf(type);

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
