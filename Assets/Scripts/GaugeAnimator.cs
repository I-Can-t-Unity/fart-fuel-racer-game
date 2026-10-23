
using UnityEngine;
using UnityEngine.UI;

public class GaugeAnimator : MonoBehaviour
{
    public Rigidbody carBody;
    public RaceManager raceManager;

    public Image speedRing;
    public Image fuelRing;

    public float maxSpeedKph = 120f;

    void Start()
    {
        FuelInventory inventory = FindFirstObjectByType<FuelInventory>();
        if (inventory == null) return;

        Color fuelColor = FuelPalette.ColorOf(inventory.carFuel);
        if (fuelRing != null) fuelRing.color = fuelColor;
        if (raceManager != null && raceManager.fuelText != null)
            raceManager.fuelText.color = fuelColor;
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
    }
}
