using System.Collections;
using UnityEngine;

/// <summary>
/// A red or blue block on the road.
///   Same colour as the car  → +1 charge and a small boost.
///   Other colour            → SWITCHEROO (swap cars), unless the car is shielded,
///                             in which case the block is smashed and the shield used up.
/// Disappears when taken and comes back after a few seconds.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ColorBlock : MonoBehaviour
{
    public CarColor color = CarColor.Red;
    public float respawnSeconds = 6f;
    public float spinSpeed = 60f;
    public float size = 2.2f;

    [SerializeField, HideInInspector] Transform visual;
    BoxCollider trigger;
    bool available = true;

    void Awake()
    {
        trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(size + 0.6f, size + 1f, size + 0.6f);
        if (!visual) BuildVisual();
    }

    public void BuildVisual()
    {
        var old = transform.Find("Visual");
        if (old) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        var v = GameObject.CreatePrimitive(PrimitiveType.Cube);
        v.name = "Visual";
        if (Application.isPlaying) Destroy(v.GetComponent<Collider>()); else DestroyImmediate(v.GetComponent<Collider>());
        v.transform.SetParent(transform, false);
        v.transform.localScale = Vector3.one * size;
        v.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        v.GetComponent<Renderer>().sharedMaterial = Switcheroo.Glow(Switcheroo.ToColor(color));
        visual = v.transform;
    }

    void Update()
    {
        if (visual && available)
        {
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = Vector3.up * (Mathf.Sin(Time.time * 2.5f + transform.position.x) * 0.15f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!available) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;
        SwapCar car = rb.GetComponent<SwapCar>();
        if (car == null) return;

        if (car.color == color)
        {
            car.AddCharge();
            car.Boost(car.pickupBoostSeconds);
            SwapHUD.Banner(car, car.Shielded ? "SHIELD UP!" : "+1 CHARGE", 0.6f);
            Take();
            return;
        }

        if (car.Shielded)
        {
            car.ClearCharges();
            PoofCloud.Spawn(transform.position, Color.white, 8, 1.6f);
            SwapHUD.Banner(car, "SHIELD SAID NO", 1f);
            Take();
            return;
        }

        if (SwapManager.Instance != null && SwapManager.Instance.TrySwap(car))
            Take();
        // Otherwise (immune / can't swap here) the block stays for later.
    }

    void Take()
    {
        available = false;
        if (visual) visual.gameObject.SetActive(false);
        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        available = true;
        if (visual) visual.gameObject.SetActive(true);
    }
}
