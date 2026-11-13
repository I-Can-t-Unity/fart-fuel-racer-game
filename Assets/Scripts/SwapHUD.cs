using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small Switcheroo HUD, built in code (no prefab needed). Put it on any object
/// (e.g. RaceHUD). Shows, bottom-centre:  YOU: RED   CHARGES 2/3   (+ SHIELD when full)
/// and big short banners in the middle of the screen ("SWITCHEROO! YOU'RE BLUE NOW").
/// Also shows the two rules strip at the top for the first seconds of the race.
/// </summary>
public class SwapHUD : MonoBehaviour
{
    static SwapHUD instance;

    [Tooltip("Leave empty to use the car with 'Is Player' ticked.")]
    public SwapCar player;
    public float rulesSeconds = 8f;

    TextMeshProUGUI badge, banner, rules;
    Coroutine bannerRoutine;

    void Awake() { instance = this; Build(); }
    void OnEnable() { SwapCar.Changed += OnChanged; }
    void OnDisable() { SwapCar.Changed -= OnChanged; }

    void Start()
    {
        if (!player)
            foreach (var c in FindObjectsByType<SwapCar>(FindObjectsSortMode.None))
                if (c.isPlayer) player = c;
        if (player) OnChanged(player);
        StartCoroutine(HideRulesLater());
    }

    /// Show a short message — only for the player's car.
    public static void Banner(SwapCar car, string text, float seconds)
    {
        if (!instance || car == null || !car.isPlayer) return;
        if (instance.bannerRoutine != null) instance.StopCoroutine(instance.bannerRoutine);
        instance.bannerRoutine = instance.StartCoroutine(instance.ShowBanner(text, seconds, Switcheroo.ToColor(car.color)));
    }

    internal static void ShowBlockedSwap(SwapCar playerCar, string text, float seconds)
    {
        if (!playerCar.isPlayer) return;
        Camera camera = Camera.main;
        if (camera != null && camera.TryGetComponent(out CarFollowCamera followCamera))
            followCamera.ShakeForBlockedSwap();
        Banner(playerCar, text, seconds);
    }

    void OnChanged(SwapCar car)
    {
        if (car != player || !badge) return;
        string hex = ColorUtility.ToHtmlStringRGB(Switcheroo.ToColor(car.color));
        badge.text = $"YOU: <color=#{hex}>{Switcheroo.Name(car.color)}</color>   CHARGES {car.Charges}/{car.maxCharges}" +
                     (car.Shielded ? "   <color=#FFFFFF><size=75%>SHIELD ON</size></color>" : "");
        if (rules) rules.text = RulesText(car.color);
    }

    static string RulesText(CarColor mine)
    {
        string me = ColorUtility.ToHtmlStringRGB(Switcheroo.ToColor(mine));
        string them = ColorUtility.ToHtmlStringRGB(Switcheroo.ToColor(Switcheroo.Other(mine)));
        return $"<color=#{me}>{Switcheroo.Name(mine)}</color> = BOOST      <color=#{them}>{Switcheroo.Name(Switcheroo.Other(mine))}</color> = SWAP CARS";
    }

    IEnumerator ShowBanner(string text, float seconds, Color c)
    {
        banner.text = text;
        banner.color = c;
        banner.gameObject.SetActive(true);
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float pop = 1f + 0.25f * Mathf.Max(0f, 1f - t * 6f);     // quick pop-in
            banner.transform.localScale = Vector3.one * pop;
            yield return null;
        }
        banner.gameObject.SetActive(false);
    }

    IEnumerator HideRulesLater()
    {
        yield return new WaitForSeconds(rulesSeconds);
        if (rules) rules.gameObject.SetActive(false);
    }

    // ── UI built in code ─────────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = new GameObject("SwitcherooHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        badge = MakeText(canvasGo.transform, "Badge", new Vector2(0.5f, 0f), new Vector2(0f, 150f), 40, new Vector2(900, 70), true);
        banner = MakeText(canvasGo.transform, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 72, new Vector2(1400, 220), true);
        banner.gameObject.SetActive(false);
        rules = MakeText(canvasGo.transform, "Rules", new Vector2(0.5f, 1f), new Vector2(0f, -60f), 34, new Vector2(1300, 60), true);
        rules.text = RulesText(CarColor.Red);
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 anchor, Vector2 pos, float size, Vector2 box, bool shadow)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.richText = true;
        t.raycastTarget = false;
        if (shadow)
        {
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 200);
        }
        return t;
    }
}
