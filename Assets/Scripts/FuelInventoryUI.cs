using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Fuel bag HUD at the top centre of the screen: a coloured square for each
// carried fuel, or the mixed fuel and what it does, plus what Q will do next.
// Built at runtime under this canvas.
public class FuelInventoryUI : MonoBehaviour
{
    public FuelInventory inventory;
    [Tooltip("Offset from the top centre of the screen; shifted right to stay clear of the controls box in the top-left corner.")]
    public Vector2 offset = new Vector2(220, -20);
    public float slotSize = 80f;
    public float messageSeconds = 1.5f;

    static readonly Color EmptySlot = new Color(0, 0, 0, .45f);
    static readonly Color Frame = new Color(1, 1, 1, .9f);

    readonly Image[] slotFills = new Image[FuelInventory.Capacity];
    readonly TMP_Text[] slotLabels = new TMP_Text[FuelInventory.Capacity];
    GameObject slotsGroup, mixGroup;
    Image mixFill;
    TMP_Text mixName, mixEffect, hint, message;
    float messageUntil;

    void Start()
    {
        if (inventory == null)
        {
            enabled = false;
            return;
        }

        RectTransform bag = NewRect("FuelBag", transform, offset, new Vector2(520, 230));
        bag.anchorMin = bag.anchorMax = new Vector2(.5f, 1);
        bag.pivot = new Vector2(.5f, 1);
        bag.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .35f);
        bag.GetComponent<Image>().raycastTarget = false;

        NewText("Title", bag, new Vector2(0, 95), new Vector2(500, 30), 22, TextAlignmentOptions.Center).text = "FUEL BAG";

        slotsGroup = NewRect("Slots", bag, Vector2.zero, bag.sizeDelta).gameObject;
        for (int i = 0; i < FuelInventory.Capacity; i++)
        {
            float x = (i - .5f) * (slotSize + 30);
            slotFills[i] = NewSlot("Slot" + i, slotsGroup.transform, new Vector2(x, 30));
            slotLabels[i] = NewText("Label" + i, slotsGroup.transform, new Vector2(x, -30), new Vector2(160, 26), 18, TextAlignmentOptions.Center);
        }

        mixGroup = NewRect("Mix", bag, Vector2.zero, bag.sizeDelta).gameObject;
        mixFill = NewSlot("MixSlot", mixGroup.transform, new Vector2(-170, 20));
        mixName = NewText("MixName", mixGroup.transform, new Vector2(50, 40), new Vector2(340, 40), 32, TextAlignmentOptions.Left);
        mixName.fontStyle = FontStyles.Bold;
        mixEffect = NewText("MixEffect", mixGroup.transform, new Vector2(50, 0), new Vector2(340, 34), 22, TextAlignmentOptions.Left);

        hint = NewText("Hint", bag, new Vector2(0, -62), new Vector2(500, 32), 26, TextAlignmentOptions.Center);
        hint.color = new Color(1f, .85f, .2f);
        message = NewText("Message", bag, new Vector2(0, -95), new Vector2(500, 32), 26, TextAlignmentOptions.Center);
        message.fontStyle = FontStyles.Bold;

        inventory.Changed += Refresh;
        inventory.Announced += ShowMessage;
        Refresh();
    }

    void OnDestroy()
    {
        if (inventory == null) return;
        inventory.Changed -= Refresh;
        inventory.Announced -= ShowMessage;
    }

    void Update()
    {
        if (messageUntil > 0 && Time.time >= messageUntil)
        {
            message.text = "";
            messageUntil = 0;
        }
    }

    void Refresh()
    {
        bool holdingMix = inventory.HeldMix != FuelMix.None;
        slotsGroup.SetActive(!holdingMix);
        mixGroup.SetActive(holdingMix);

        if (holdingMix)
        {
            mixFill.color = inventory.ColorOf(inventory.HeldMix);
            mixName.text = inventory.NameOf(inventory.HeldMix);
            mixEffect.text = inventory.EffectOf(inventory.HeldMix);
            hint.text = "Q: USE";
            return;
        }

        for (int i = 0; i < FuelInventory.Capacity; i++)
        {
            bool filled = i < inventory.Slots.Count;
            slotFills[i].color = filled ? inventory.ColorOf(inventory.Slots[i]) : EmptySlot;
            slotLabels[i].text = filled ? inventory.NameOf(inventory.Slots[i]) : "EMPTY";
        }
        hint.text = inventory.Slots.Count == FuelInventory.Capacity ? "Q: MIX" : "";
    }

    void ShowMessage(string text)
    {
        message.text = text;
        messageUntil = Time.time + messageSeconds;
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
