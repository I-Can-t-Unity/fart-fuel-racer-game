using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class DrivingControlsHUD : MonoBehaviour
{
    const float Height = 56f;
    const string KeyboardHints =
        "<b>W / Up</b> Accelerate     <b>S / Down</b> Reverse     <b>A D / Left Right</b> Steer     <b>Space</b> Brake     <b>R</b> Reset";
    const string GamepadHints =
        "<b>RT</b> Accelerate     <b>LT</b> Reverse     <b>Left Stick</b> Steer     <b>A</b> Brake     <b>Start</b> Reset";

    RectTransform content;
    TMP_Text hints;

    internal static void CreateStandalone(Transform driver)
    {
        var root = new GameObject("Driving HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(driver, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        Create((RectTransform)root.transform, null);
    }

    internal static DrivingControlsHUD Create(RectTransform canvas, TMP_FontAsset font)
    {
        // Keep each HUD element's existing anchors relative to the space above the bar.
        int childCount = canvas.childCount;
        var content = new GameObject("Race HUD Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(canvas, false);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;
        for (int i = 0; i < childCount; i++)
            canvas.GetChild(0).SetParent(content, false);

        var bar = new GameObject("Driving Controls Bar", typeof(RectTransform));
        var rect = (RectTransform)bar.transform;
        rect.SetParent(canvas, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.right;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(0, Height);
        var plate = bar.AddComponent<Image>();
        plate.color = new Color(.06f, .09f, .15f, .92f);
        plate.raycastTarget = false;

        var hints = new GameObject("Control Hints", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        RectTransform label = hints.rectTransform;
        label.SetParent(rect, false);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(20f, 0);
        label.offsetMax = new Vector2(-20f, 0);
        if (font != null) hints.font = font;
        hints.alignment = TextAlignmentOptions.Center;
        hints.textWrappingMode = TextWrappingModes.NoWrap;
        hints.enableAutoSizing = true;
        hints.fontSizeMin = 8f;
        hints.fontSizeMax = 26f;
        hints.raycastTarget = false;

        var controls = bar.AddComponent<DrivingControlsHUD>();
        controls.content = content;
        controls.hints = hints;
        controls.SetVisible(true);
        return controls;
    }

    internal void SetVisible(bool visible)
    {
        content.offsetMin = new Vector2(0, visible ? Height : 0);
        gameObject.SetActive(visible);
        if (visible) RefreshHints();
    }

    internal static Transform ContentOf(Transform canvas)
    {
        var bar = canvas.GetComponentInChildren<DrivingControlsHUD>(true);
        return bar != null ? bar.content : canvas;
    }

    void Update() => RefreshHints();

    void RefreshHints()
    {
        string text = Gamepad.current != null ? GamepadHints : KeyboardHints;
        if (hints.text != text) hints.text = text;
    }
}
