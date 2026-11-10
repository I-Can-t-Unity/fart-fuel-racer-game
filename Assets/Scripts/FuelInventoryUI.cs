using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FuelInventoryUI : MonoBehaviour
{
    public FuelInventory inventory;
    [Tooltip("Offset from the bottom centre of the screen. The default lines the bag up with the speed and fuel gauges.")]
    public Vector2 bottomOffset = new Vector2(0, 20);
    public float slotSize = 80f;
    public float messageSeconds = 1.5f;
    [Tooltip("Offset of messages such as \"BAG FULL\" from the top centre of the screen, clear of the timer in the top right.")]
    public Vector2 messageTopOffset = new Vector2(0, -60);
    public float messageFontSize = 44f;

    static readonly Color EmptySlot = new Color(0, 0, 0, .45f);
    static readonly Color Frame = new Color(1, 1, 1, .9f);

    // One pip per charge (3).
    Image[] slotFills;
    TMP_Text[] slotLabels;
    GameObject slotsGroup, mixGroup;
    Image mixFill;
    // Removed: the Super Fart recipe line above the bag.
    // TMP_Text mixName, mixEffect, hint, message, recipe;
    TMP_Text mixName, mixEffect, hint, message, title;
    float messageUntil;
    float messageShownAt;
    Image messagePlate;
    RectTransform chargeBar;
    bool showChargeBar = true;

    internal void SetChargeBarVisible(bool visible)
    {
        showChargeBar = visible;
        if (chargeBar != null) chargeBar.gameObject.SetActive(visible);
    }

    void Start()
    {
        if (inventory == null)
        {
            enabled = false;
            return;
        }

        RectTransform bag = NewRect("ColorStreakHUD", transform, bottomOffset, new Vector2(620, 200));
        chargeBar = bag;
        bag.anchorMin = bag.anchorMax = new Vector2(.5f, 0);
        bag.pivot = new Vector2(.5f, 0);
        bag.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .35f);
        bag.GetComponent<Image>().raycastTarget = false;

        title = NewText("Title", bag, new Vector2(0, 80), new Vector2(600, 30), 22, TextAlignmentOptions.Center);

        // Removed: always on screen just above the bag, which fuel makes this car's Super Fart.
        // recipe = NewText("SuperFartRecipe", bag, new Vector2(0, 140), new Vector2(700, 36), 26, TextAlignmentOptions.Center);
        // recipe.fontStyle = FontStyles.Bold;
        // recipe.outlineWidth = .2f;
        // recipe.outlineColor = Color.black;

        slotsGroup = NewRect("Slots", bag, Vector2.zero, bag.sizeDelta).gameObject;
        int pips = Mathf.Max(1, inventory.maxCharges);
        slotFills = new Image[pips];
        slotLabels = new TMP_Text[pips];
        for (int i = 0; i < pips; i++)
        {
            float x = (i - (pips - 1) * .5f) * (slotSize + 30);
            slotFills[i] = NewSlot("Slot" + i, slotsGroup.transform, new Vector2(x, 15));
            slotLabels[i] = NewText("Label" + i, slotsGroup.transform, new Vector2(x, -30), new Vector2(160, 26), 18, TextAlignmentOptions.Center);
        }

        mixGroup = NewRect("Mix", bag, Vector2.zero, bag.sizeDelta).gameObject;
        mixFill = NewSlot("MixSlot", mixGroup.transform, new Vector2(-170, 20));
        mixName = NewText("MixName", mixGroup.transform, new Vector2(50, 40), new Vector2(340, 40), 32, TextAlignmentOptions.Left);
        mixName.fontStyle = FontStyles.Bold;
        mixEffect = NewText("MixEffect", mixGroup.transform, new Vector2(50, 0), new Vector2(340, 34), 22, TextAlignmentOptions.Left);

        // One line, inside the panel: smaller text and no wrapping.
        hint = NewText("Hint", bag, new Vector2(0, -70), new Vector2(600, 30), 22, TextAlignmentOptions.Center);
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.color = Color.white;
        // Old: messages sat in the bag, under the hint, where they were easy to miss.
        // message = NewText("Message", bag, new Vector2(0, -95), new Vector2(500, 32), 26, TextAlignmentOptions.Center);
        // At the top centre of the screen, with an outline so they read over sky and road alike.
        // Dark plate behind the message so it pops over bright sky or grey road.
        messagePlate = NewRect("MessagePlate", transform, messageTopOffset + new Vector2(0, 12), new Vector2(600, messageFontSize * 2f)).gameObject.AddComponent<Image>();
        messagePlate.rectTransform.anchorMin = messagePlate.rectTransform.anchorMax = new Vector2(.5f, 1);
        messagePlate.rectTransform.pivot = new Vector2(.5f, 1);
        messagePlate.color = new Color(0, 0, 0, .65f);
        messagePlate.raycastTarget = false;
        messagePlate.enabled = false;
        message = NewText("Message", transform, messageTopOffset, new Vector2(1000, messageFontSize * 1.6f), messageFontSize, TextAlignmentOptions.Center);
        message.rectTransform.anchorMin = message.rectTransform.anchorMax = new Vector2(.5f, 1);
        message.rectTransform.pivot = new Vector2(.5f, 1);
        message.fontStyle = FontStyles.Bold;
        message.outlineWidth = .3f;
        message.outlineColor = Color.black;
        message.textWrappingMode = TextWrappingModes.NoWrap;

        inventory.Changed += Refresh;
        inventory.Announced += ShowMessage;
        Refresh();
        SetChargeBarVisible(showChargeBar);
    }

    void OnDestroy()
    {
        if (inventory == null) return;
        inventory.Changed -= Refresh;
        inventory.Announced -= ShowMessage;
    }

    void Update()
    {
        // Count the shield down on the HUD.
        if (inventory != null && inventory.IsShielded)
            UpdateShieldHint();
        if (messageUntil > 0 && Time.time >= messageUntil)
        {
            message.text = "";
            messageUntil = 0;
            messagePlate.enabled = false;
        }
        // Pop-in: starts 40% bigger and settles in 0.2 s.
        if (messageUntil > 0)
        {
            float t = Mathf.Clamp01((Time.time - messageShownAt) / .2f);
            float scale = Mathf.Lerp(1.4f, 1f, 1f - (1f - t) * (1f - t));
            message.rectTransform.localScale = Vector3.one * scale;
            messagePlate.rectTransform.localScale = new Vector3(scale, 1f, 1f);
        }
    }

    void Refresh()
    {
        // YOU: BLUE, three charge pips in your colour, and what a full set does.
        Color mine = inventory.ColorOf(inventory.carFuel);
        title.text = "YOU: " + inventory.NameOf(inventory.carFuel) + "   CHARGES";
        title.color = mine;
        slotsGroup.SetActive(true);
        mixGroup.SetActive(false);
        for (int i = 0; i < slotFills.Length; i++)
        {
            bool filled = i < inventory.Charges;
            slotFills[i].color = filled ? mine : EmptySlot;
            slotLabels[i].text = "";
        }
        if (inventory.IsShielded)
            UpdateShieldHint();
        else
            hint.text = $"{inventory.NameOf(inventory.carFuel)} = CHARGE   ·   OTHERS = SWAP";
    }

    void UpdateShieldHint()
    {
        hint.text = $"SHIELD ON  {Mathf.CeilToInt(inventory.ShieldTimeLeft)}s";
        if (inventory.TurboKeyEnabled)
            hint.text += $"  ·  {inventory.turboKey} = TURBO";
    }

    void ShowMessage(string text)
    {
        message.text = text;
        messageUntil = Time.time + messageSeconds;
        messageShownAt = Time.time;
        // Size the plate to the text, with some padding.
        message.ForceMeshUpdate();
        float width = Mathf.Min(message.preferredWidth + 60f, 1100f);
        messagePlate.rectTransform.sizeDelta = new Vector2(width, messageFontSize * 1.6f + 24f);
        messagePlate.enabled = !string.IsNullOrEmpty(text);
    }

    // A white frame with a coloured square inside; returns the square.
    Image NewSlot(string name, Transform parent, Vector2 position)
    {
        NewImage(name, parent, position, slotSize + 12, Frame);
        return NewImage(name + "Fill", parent, position, slotSize, EmptySlot);
    }

    static Image NewImage(string name, Transform parent, Vector2 position, float size, Color color)
    {
        Image image = NewRect(name, parent, position, Vector2.one * size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text NewText(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewRect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
