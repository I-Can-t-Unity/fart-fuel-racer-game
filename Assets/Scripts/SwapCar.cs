using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put on BOTH cars (PlayerCar and AICar). Holds the car's colour, its charges
/// (0-3, a full set = shield), and does the boosts. The colour belongs to the
/// car's current paint: when a swap happens the SwapManager repaints the cars.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SwapCar : MonoBehaviour
{
    [Header("Identity")]
    public CarColor color = CarColor.Red;
    [Tooltip("Tick on PlayerCar only.")]
    public bool isPlayer = false;

    [Header("Charges")]
    public int maxCharges = 3;
    [Tooltip("Fart boost length when you grab your own colour (seconds).")]
    public float pickupBoostSeconds = 0.7f;

    [Header("Turbo (spend a full set)")]
    public Key turboKey = Key.Q;
    [Tooltip("Fart boost length of the turbo (seconds).")]
    public float turboSeconds = 2.5f;
    [Tooltip("Only used if the car has no FartBoost script: speed added (m/s).")]
    public float fallbackBoostSpeed = 8f;

    [Header("Paint")]
    [Tooltip("Body parts to recolour. Leave empty = every mesh on the car except wheels/glass/lights.")]
    public Renderer[] paintRenderers;

    public int Charges { get; private set; }
    public bool Shielded => Charges >= maxCharges;
    public float ImmuneUntil { get; set; }
    public bool Immune => Time.time < ImmuneUntil;
    public Rigidbody Body { get; private set; }

    /// Fires whenever colour, charges or shield change (for the HUD).
    public static event System.Action<SwapCar> Changed;

    GameObject shieldBubble;
    FartBoost fart;
    MaterialPropertyBlock block;
    static readonly string[] skipWords = { "wheel", "tire", "tyre", "glass", "window", "light", "shadow", "shield", "puff", "poof" };

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        block = new MaterialPropertyBlock();
        BuildShieldBubble();
    }

    IEnumerator Start()
    {
        // Other scripts (car choice, AI paint job) set up the body in their own Start,
        // so wait a moment, then find the body parts and paint over them.
        yield return null;
        yield return new WaitForSeconds(0.1f);
        fart = GetComponent<FartBoost>();
        if (paintRenderers == null || paintRenderers.Length == 0) paintRenderers = FindBodyRenderers();
        Paint();
        Changed?.Invoke(this);
    }

    void Update()
    {
        if (isPlayer && Keyboard.current != null && Keyboard.current[turboKey].wasPressedThisFrame && Shielded)
            Turbo();
    }

    // ── Charges ───────────────────────────────────────────────────────────────
    public void AddCharge()
    {
        Charges = Mathf.Min(maxCharges, Charges + 1);
        Refresh();
    }

    public void ClearCharges()
    {
        Charges = 0;
        Refresh();
    }

    /// Used by the swap: the stash stays with the car's paint.
    public void SetCharges(int value)
    {
        Charges = Mathf.Clamp(value, 0, maxCharges);
        Refresh();
    }

    /// Spends a full set. Returns false if the car doesn't have one (e.g. bridge gate check).
    public bool TrySpendFullSet()
    {
        if (!Shielded) return false;
        ClearCharges();
        return true;
    }

    public void Turbo()
    {
        if (!TrySpendFullSet()) return;
        Boost(turboSeconds);
        SwapHUD.Banner(this, "TURBO!", 0.9f);
    }

    /// Fart-boosts the car for this many seconds (uses the team's FartBoost: puffs + speed).
    public void Boost(float seconds)
    {
        if (fart == null) fart = GetComponent<FartBoost>();
        if (fart != null) fart.Fart(seconds);
        else StartCoroutine(HoldBoost(fallbackBoostSpeed, seconds));
    }

    // Keeps the car above (current speed + boost) for a while, so the engine's top-speed cap doesn't eat it.
    IEnumerator HoldBoost(float add, float seconds)
    {
        float target = Vector3.Dot(Body.linearVelocity, transform.forward) + add;
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            if (!Body.isKinematic)
            {
                Vector3 fwd = transform.forward;
                float cur = Vector3.Dot(Body.linearVelocity, fwd);
                if (cur < target && cur > 2f) Body.linearVelocity += fwd * Mathf.Min(target - cur, 40f * Time.fixedDeltaTime);
            }
            yield return new WaitForFixedUpdate();
        }
    }

    // ── Paint + visuals ───────────────────────────────────────────────────────
    public void SetColor(CarColor c)
    {
        color = c;
        Paint();
        Refresh();
    }

    public void Paint()
    {
        Color c = Switcheroo.ToColor(color);
        if (paintRenderers == null) return;
        foreach (var r in paintRenderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
        if (shieldBubble)
            shieldBubble.GetComponent<Renderer>().sharedMaterial.color = new Color(c.r, c.g, c.b, 0.22f);
    }

    void Refresh()
    {
        if (shieldBubble) shieldBubble.SetActive(Shielded);
        Changed?.Invoke(this);
    }

    Renderer[] FindBodyRenderers()
    {
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            bool skip = false;
            foreach (var w in skipWords) if (n.Contains(w)) { skip = true; break; }
            if (!skip) list.Add(r);
        }
        return list.ToArray();
    }

    void BuildShieldBubble()
    {
        shieldBubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shieldBubble.name = "ShieldBubble";
        Destroy(shieldBubble.GetComponent<Collider>());
        shieldBubble.transform.SetParent(transform, false);
        shieldBubble.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        shieldBubble.transform.localScale = new Vector3(3.6f, 2.6f, 5f);
        var r = shieldBubble.GetComponent<Renderer>();
        r.sharedMaterial = Switcheroo.Transparent(new Color(1f, 1f, 1f, 0.22f));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shieldBubble.SetActive(false);
    }
}
