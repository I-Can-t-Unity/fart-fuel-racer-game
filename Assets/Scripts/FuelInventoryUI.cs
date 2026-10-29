using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Fuel bag HUD at the bottom centre of the screen, between the speed and fuel
// gauges: a coloured square for each carried fuel, or the mixed fuel and what it
// does. Built at runtime under this canvas.
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

    readonly Image[] slotFills = new Image[FuelInventory.Capacity];
    readonly TMP_Text[] slotLabels = new TMP_Text[FuelInventory.Capacity];
    GameObject slotsGroup, mixGroup;
    Image mixFill;
    // Removed: the Super Fart recipe line above the bag.
    // TMP_Text mixName, mixEffect, hint, message, recipe;
    TMP_Text mixName, mixEffect, hint, message;
    float messageUntil;

    void Start()
    {
        if (inventory == null)
        {
            enabled = false;
            return;
        }

        RectTransform bag = NewRect("FuelBag", transform, bottomOffset, new Vector2(520, 230));
        bag.anchorMin = bag.anchorMax = new Vector2(.5f, 0);
        bag.pivot = new Vector2(.5f, 0);
        bag.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .35f);
        bag.GetComponent<Image>().raycastTarget = false;

        NewText("Title", bag, new Vector2(0, 95), new Vector2(500, 30), 22, TextAlignmentOptions.Center).text = "FUEL BAG";

        // Removed: always on screen just above the bag, which fuel makes this car's Super Fart.
        // recipe = NewText("SuperFartRecipe", bag, new Vector2(0, 140), new Vector2(700, 36), 26, TextAlignmentOptions.Center);
        // recipe.fontStyle = FontStyles.Bold;
        // recipe.outlineWidth = .2f;
        // recipe.outlineColor = Color.black;

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
        hint.color = Color.white;
        // Old: messages sat in the bag, under the hint, where they were easy to miss.
        // message = NewText("Message", bag, new Vector2(0, -95), new Vector2(500, 32), 26, TextAlignmentOptions.Center);
        // At the top centre of the screen, with an outline so they read over sky and road alike.
        message = NewText("Message", transform, messageTopOffset, new Vector2(1000, messageFontSize * 1.6f), messageFontSize, TextAlignmentOptions.Center);
        message.rectTransform.anchorMin = message.rectTransform.anchorMax = new Vector2(.5f, 1);
        message.rectTransform.pivot = new Vector2(.5f, 1);
        message.fontStyle = FontStyles.Bold;
        message.outlineWidth = .2f;
        message.outlineColor = Color.black;

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
        // Removed: the recipe line above the bag.
        // recipe.text = inventory.SuperFartRecipeText;
        // recipe.color = inventory.LabelColorOf(inventory.SuperFartFuel);

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
            slotLabels[i].color = filled ? inventory.LabelColorOf(inventory.Slots[i]) : Color.white;
        }
        // Old: hint.text = "";
        // Halfway to Super Fart: say which pickup completes it.
        bool halfway = inventory.Slots.Count == 1 && inventory.Slots[0] == inventory.SuperFartFuel;
        hint.text = halfway
            ? $"1 MORE {SuperFartRecipe.ColorNameOf(inventory.SuperFartFuel)} = SUPER FART"
            : "";
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
