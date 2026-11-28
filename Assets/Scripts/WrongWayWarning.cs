using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "WRONG WAY!" banner at the top of the screen, built at runtime from plain UI objects (no art assets).
// Wrong-way detection calls WrongWayWarning.SetVisible(true) when the player drives backwards
// and SetVisible(false) once they turn around.
public class WrongWayWarning : MonoBehaviour
{
    // Below the top-centre pickup message so the two never overlap.
    public Vector2 topOffset = new Vector2(0, -150);
    public Color iconColor = new Color(1f, .8f, .1f);
    public Color textColor = new Color(1f, .25f, .25f);

    static WrongWayWarning instance;
    GameObject banner;

    public static void SetVisible(bool visible)
    {
        if (instance != null) instance.banner.SetActive(visible);
    }

    void Awake()
    {
        instance = this;
        Build();
        banner.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Build()
    {
        banner = new GameObject("Wrong Way Banner", typeof(RectTransform));
        var rect = (RectTransform)banner.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = topOffset;
        rect.sizeDelta = new Vector2(720f, 110f);
        var background = banner.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, .65f);
        background.raycastTarget = false;

        // Warning icon: a yellow diamond (a square turned 45°) with a black "!" on top.
        RectTransform icon = Child("Icon", rect, new Vector2(-280f, 0f), new Vector2(62f, 62f));
        icon.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.color = iconColor;
        iconImage.raycastTarget = false;
        Label("Mark", rect, new Vector2(-280f, 0f), new Vector2(60f, 90f), "!", 66f, Color.black);

        Label("Text", rect, new Vector2(40f, 0f), new Vector2(540f, 100f), "WRONG WAY!", 60f, textColor);
    }

    static RectTransform Child(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static void Label(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize, Color color)
    {
        var label = Child(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
    }
}
