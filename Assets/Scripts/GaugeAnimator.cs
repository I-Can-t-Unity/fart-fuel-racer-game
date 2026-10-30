
using UnityEngine;
using UnityEngine.UI;

public class GaugeAnimator : MonoBehaviour
{
    public Rigidbody carBody;
    public RaceManager raceManager;

    public Image speedRing;
    public Image fuelRing;

    public float maxSpeedKph = 120f;

    [Header("Low fuel warning")]
    [Tooltip("Below this much fuel (percent) the fuel ring and number flash the low fuel colour.")]
    public float lowFuel = 25f;
    [Tooltip("Below this they flash red, and faster. The engine starts to sputter at 10%.")]
    public float criticalFuel = 10f;
    public Color lowFuelColor = new Color(1f, .75f, 0f);
    public Color criticalFuelColor = new Color(1f, .1f, .1f);
    [Tooltip("Flashes per second when low and when critical.")]
    public float lowFlashRate = 2f;
    public float criticalFlashRate = 4f;
    [Tooltip("Also puts LOW FUEL on screen once each time the tank drops below Low Fuel.")]
    public bool announceLowFuel = true;

    FuelInventory inventory;
    Color ringColor = Color.white, textColor = Color.white;
    bool lowFuelAnnounced;

    void Start()
    {
        inventory = FindFirstObjectByType<FuelInventory>();
        if (inventory != null)
        {
            Color fuelColor = FuelPalette.ColorOf(inventory.carFuel);
            if (fuelRing != null) fuelRing.color = fuelColor;
            if (raceManager != null && raceManager.fuelText != null)
                raceManager.fuelText.color = fuelColor;
        }

        // The colours to go back to between flashes.
        if (fuelRing != null) ringColor = fuelRing.color;
        if (raceManager != null && raceManager.fuelText != null)
            textColor = raceManager.fuelText.color;
    }

    void Update()
    {
        if (carBody != null && speedRing != null)
        {
            float speedKph =
                carBody.linearVelocity.magnitude * 3.6f;

            speedRing.fillAmount =
                Mathf.Clamp01(speedKph / maxSpeedKph);
        }

        if (raceManager != null && fuelRing != null)
        {
            fuelRing.fillAmount =
                Mathf.Clamp01(raceManager.fuel / 100f);
        }

        if (raceManager != null)
            WarnLowFuel(raceManager.fuel);
    }

    // Flashes between the normal colours and a warning colour: amber when low, red
    // and faster when nearly empty. It snaps rather than fades so it reads as a warning.
    void WarnLowFuel(float fuel)
    {
        bool low = raceManager.IsRacing && fuel < lowFuel;
        bool critical = fuel < criticalFuel;
        bool flashOn = low && Mathf.Repeat(Time.time * (critical ? criticalFlashRate : lowFlashRate), 1f) < .5f;
        Color warning = critical ? criticalFuelColor : lowFuelColor;

        if (fuelRing != null)
            fuelRing.color = flashOn ? warning : ringColor;
        if (raceManager.fuelText != null)
            raceManager.fuelText.color = flashOn ? warning : textColor;

        // Fuel only goes up in whole pickups, so it can't hover at the line and repeat this.
        // An empty tank (run dry or dumped with F) has its own message instead.
        if (!low)
            lowFuelAnnounced = false;
        else if (!lowFuelAnnounced)
        {
            lowFuelAnnounced = true;
            if (announceLowFuel && inventory != null && fuel > 0)
                inventory.Announce("LOW FUEL!");
        }
    }
}
