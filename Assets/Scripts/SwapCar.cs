using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put on BOTH cars (PlayerCar and AICar). Holds the car's colour, its charges
/// (0-3, a full set = shield), and does the boosts. The colour belongs to the
/// car's current paint: when a swap happens the SwapManager repaints the cars.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(FartBoost))]
public class SwapCar : MonoBehaviour
{
    [Header("Identity")]
    public CarColor color = CarColor.Red;
    [Tooltip("Tick on PlayerCar only.")]
    public bool isPlayer = false;

    [Header("Charges")]
    public int maxCharges = 3;

    [Header("Paint")]
    [Tooltip("Body parts to recolour. Leave empty = every mesh on the car except wheels/glass/lights.")]
    public Renderer[] paintRenderers;

    public int Charges { get; private set; }
    public bool Shielded => Time.time < shieldUntil;
    public float ImmuneUntil { get; set; }
    public bool Immune => Time.time < ImmuneUntil;
    public Rigidbody Body { get; private set; }

    /// Fires whenever colour, charges or shield change (for the HUD).
    public static event System.Action<SwapCar> Changed;

    GameObject shieldBubble;
    float shieldUntil;
    FartBoost fart;
    MaterialPropertyBlock block;
    static readonly string[] skipWords = { "wheel", "tire", "tyre", "glass", "window", "light", "shadow", "shield", "puff", "poof" };

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        fart = GetComponent<FartBoost>();
        // RequireComponent applies to newly added components; older scenes may lack it.
        if (fart == null)
            fart = gameObject.AddComponent<FartBoost>();
        block = new MaterialPropertyBlock();
        BuildShieldBubble();
    }

    IEnumerator Start()
    {
        // Other scripts (car choice, AI paint job) set up the body in their own Start,
        // so wait a moment, then find the body parts and paint over them.
        yield return null;
        yield return new WaitForSeconds(0.1f);
        if (paintRenderers == null || paintRenderers.Length == 0) paintRenderers = FindBodyRenderers();
        Paint();
        Changed?.Invoke(this);
    }

    void Update()
    {
        if (shieldBubble.activeSelf && !Shielded)
            Refresh();
    }

    // ── Charges ───────────────────────────────────────────────────────────────
    public void AddCharge()
    {
        Charges++;
        if (Charges >= maxCharges)
        {
            Charges = 0;
            shieldUntil = Time.time + FuelInventory.ShieldSeconds;
        }
        Refresh();
    }

    /// Used by the swap: the stash stays with the car's paint.
    public void SetCharges(int value)
    {
        Charges = Mathf.Clamp(value, 0, maxCharges);
        Refresh();
    }

    public void Boost() => fart.Fart(FuelInventory.PickupBoostSeconds);

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
