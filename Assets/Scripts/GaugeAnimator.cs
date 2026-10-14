
using UnityEngine;
using UnityEngine.UI;

public class GaugeAnimator : MonoBehaviour
{
    public Rigidbody carBody;
    public RaceManager raceManager;

    public Image speedRing;
    public Image fuelRing;

    public float maxSpeedKph = 120f;

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
