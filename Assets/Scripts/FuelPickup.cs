using TMPro;
using UnityEngine;

// A floating jerry can. Driving through it puts the fuel in the car's bag;
// if the bag is full the can stays where it is. A label above the can names
// the fuel and what it does, using the same color as its fuel type.
public class FuelPickup : MonoBehaviour
{
    public FuelType type;
    public FuelInventory inventory;
    public float respawnSeconds = 10f;
    public float spinSpeed = 90f;
    [Tooltip("Gap between the top of the can and the bottom of its label, in metres.")]
    public float labelGap = .4f;
    public float labelFontSize = 8f;
    [Tooltip("Beyond this distance from the camera the label grows, so it stays readable from further away.")]
    public float labelReadableDistance = 30f;
    [Tooltip("Largest the label grows to. Above about 2.2, long names in neighbouring lanes start to overlap.")]
    public float labelMaxScale = 1.4f;
    [Tooltip("Lift this label above its neighbours' so labels in the same row never overlap.")]
    public bool raiseLabel;
    // Which row of cubes this is in and which lane, counted from 0 (set by the spawner).
    public int row = -1, lane = -1;

    // The player's car hit this cube; true if the fuel went into the bag (for analytics).
    public static event System.Action<FuelPickup, bool> PlayerTouched;

    Vector3 basePosition;
    Renderer pickupRenderer;
    Collider trigger;
    TextMeshPro label;
    float labelLift;
    float respawnAt = -1;

    static Sprite plateSprite;
    static Mesh jerryCanMesh;

    // Looked up in Awake, not Start: a car can touch a cube in the same frame it is
    // spawned, before Start has run.
    void Awake()
    {
        pickupRenderer = GetComponent<Renderer>();
        trigger = GetComponent<Collider>();
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter != null)
            filter.sharedMesh = LoadJerryCanMesh();
    }

    // One Blender mesh shared by race and tutorial pickups. Keep the existing
    // material, trigger and unit-height bounds so colors and gameplay stay consistent.
    static Mesh LoadJerryCanMesh()
    {
        if (jerryCanMesh != null) return jerryCanMesh;

        GameObject model = Resources.Load<GameObject>("FuelJerryCan");
        MeshFilter source = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        if (source == null || source.sharedMesh == null)
        {
            Debug.LogError("FuelJerryCan model is missing from Resources.");
            return null;
        }

        jerryCanMesh = Instantiate(source.sharedMesh);
        jerryCanMesh.name = "Fuel Jerry Can";
        Vector3[] vertices = jerryCanMesh.vertices;
        Matrix4x4 importedTransform = source.transform.localToWorldMatrix;
        Bounds bounds = new Bounds(importedTransform.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = importedTransform.MultiplyPoint3x4(vertices[i]);
            bounds.Encapsulate(vertices[i]);
        }
        float height = Mathf.Max(bounds.size.y, .001f);
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = (vertices[i] - bounds.center) / height;
        jerryCanMesh.vertices = vertices;
        jerryCanMesh.RecalculateNormals();
        jerryCanMesh.RecalculateBounds();
        return jerryCanMesh;
    }

    public void SetPlacement(Vector3 position) { basePosition = position; transform.position = position; }

    void Start()
    {
        basePosition = transform.position;
        if (inventory != null)
            label = CreateLabel();
    }

    TextMeshPro CreateLabel()
    {
        // A sibling rather than a child, so the text doesn't spin with the cube.
        var text = new GameObject(name + "_Label").AddComponent<TextMeshPro>();
        text.transform.SetParent(transform.parent, false);
        text.rectTransform.sizeDelta = new Vector2(6, 2);
        text.rectTransform.pivot = new Vector2(.5f, 0);
        text.alignment = TextAlignmentOptions.Bottom;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.fontSize = labelFontSize;
        text.fontStyle = FontStyles.Bold;
        text.text = $"{inventory.NameOf(type)}\n<size=80%>{inventory.PickupEffectOf(type)}</size>";
        text.color = inventory.LabelColorOf(type);
        text.outlineWidth = .25f;
        text.outlineColor = Color.black;

        // A dark plate just behind the text keeps both colours readable against
        // grass, road and sky alike.
        text.ForceMeshUpdate();
        Bounds bounds = text.textBounds;
        Vector2 plateSize = new Vector2(bounds.size.x + .6f, bounds.size.y + .3f);
        if (plateSprite == null)
            plateSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4);
        var plate = new GameObject("Plate").AddComponent<SpriteRenderer>();
        plate.sprite = plateSprite;
        plate.color = inventory.labelBackground;
        plate.sortingOrder = -1;
        plate.transform.SetParent(text.transform, false);
        plate.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, .05f);
        plate.transform.localScale = new Vector3(plateSize.x, plateSize.y, 1);

        // A raised label sits one whole plate higher, clear of the labels beside it.
        if (raiseLabel)
            labelLift = plateSize.y + .2f;
        return text;
    }

    void Update()
    {
        if (respawnAt >= 0 && Time.time >= respawnAt)
        {
            respawnAt = -1;
            SetAvailable(true);
        }

        transform.Rotate(0, spinSpeed * Time.deltaTime, 0, Space.World);
        transform.position = basePosition + Vector3.up * Mathf.Sin(Time.time * 2f + basePosition.x) * .15f;

        if (label != null)
        {
            float scale = 1;
            Camera view = Camera.main;
            if (view != null)
            {
                label.transform.rotation = view.transform.rotation;
                float distance = Vector3.Distance(view.transform.position, transform.position);
                scale = Mathf.Clamp(distance / labelReadableDistance, 1, labelMaxScale);
            }
            label.transform.localScale = Vector3.one * scale;
            label.transform.position = transform.position
                + Vector3.up * (transform.localScale.y * .5f + labelGap + labelLift * scale);
        }
    }

    // False while the cube has been taken and is waiting to respawn. The AI uses this to pick a lane.
    public bool IsAvailable => gameObject.activeInHierarchy && trigger != null && trigger.enabled;

    void OnTriggerEnter(Collider other)
    {
        if (!IsAvailable) return; // A queued second collider cannot collect the same can twice.
        // Old: only the player's bag could pick up fuel.
        // if (inventory == null || !other.transform.IsChildOf(inventory.transform))
        //     return;
        // if (!inventory.TryCollect(type))
        //     return;

        // The player and the AI rival share the cubes, so whoever gets there first takes it.
        bool collected;
        if (inventory != null && other.transform.IsChildOf(inventory.transform))
        {
            collected = inventory.TryCollect(type);
            PlayerTouched?.Invoke(this, collected);
        }
        else
        {
            AIFuelTank rival = other.GetComponentInParent<AIFuelTank>();
            if (rival == null)
                return;
            collected = rival.TryCollect(type);
        }
        if (!collected)
            return;

        SetAvailable(false);
        if (respawnSeconds > 0)
            respawnAt = Time.time + respawnSeconds;
    }

    void SetAvailable(bool available)
    {
        pickupRenderer.enabled = available;
        trigger.enabled = available;
        if (label != null)
            label.gameObject.SetActive(available);
    }
}
