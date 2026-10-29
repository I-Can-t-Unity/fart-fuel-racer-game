using TMPro;
using UnityEngine;

public class RivalMixHUD : MonoBehaviour
{
    public TMP_Text mixText;

    AIFuelTank rival;
    float nextSearch;

    void OnEnable()
    {
        rival = null;
        nextSearch = 0f;

        if (mixText != null)
            mixText.text = "RIVAL MIX\nConnecting...";
    }

    void LateUpdate()
    {
        if (mixText == null) return;

        if (rival == null)
        {
            if (Time.unscaledTime >= nextSearch)
            {
                rival = FindFirstObjectByType<AIFuelTank>();
                nextSearch = Time.unscaledTime + 0.5f;
            }

            if (rival == null) return;
        }

        string label;

        switch (rival.HeldMix)
        {
            case FuelMix.Premium:
                label = "PREMIUM";
                break;
            case FuelMix.Hybrid:
                label = "HYBRID";
                break;
            case FuelMix.SuperFart:
                label = "SUPER FART";
                break;
            case FuelMix.ToxicSludge:
                label = "TOXIC SLUDGE";
                break;
            default:
                label = "NO MIX";
                break;
        }

        string display = "RIVAL MIX\n" + label;

        if (mixText.text != display)
            mixText.text = display;
    }
}