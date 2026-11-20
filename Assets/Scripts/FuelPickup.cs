using System.Collections.Generic;
using UnityEngine;

public class FuelPickup : MonoBehaviour
{
    const float GuideBlinkPeriod = 1f;
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    public FuelType type;
    public FuelInventory inventory;
    public float respawnSeconds = 10f;
    public float spinSpeed = 90f;
    // Which row of cubes this is in and which lane, counted from 0 (set by the spawner).
    public int row = -1, lane = -1;

    // The player's car hit this cube; true if the fuel went into the bag (for analytics).
    public static event System.Action<FuelPickup, bool> PlayerTouched;
    internal bool RequiresShield { get; set; }

    Vector3 basePosition;
    Renderer pickupRenderer;
    Collider trigger;
    float respawnAt = -1;
    Material emissiveMaterial;
    Color peakEmission;
    float blinkStartedAt;
    bool guideBlinkActive;
    readonly HashSet<Collider> overlaps = new HashSet<Collider>();
    CarDriver attemptedCar;
    bool attemptedDuringImmunity;

    static Mesh jerryCanMesh;

    // Looked up in Awake, not Start: a car can touch a cube in the same frame it is
    // spawned, before Start has run.
    void Awake()
    {
        pickupRenderer = GetComponent<Renderer>();
        trigger = GetComponent<Collider>();
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

    void Start()
    {
        basePosition = transform.position;
    }

    internal void SetGuideBlink(bool active)
    {
        if (guideBlinkActive == active) return;
        guideBlinkActive = active;
        if (emissiveMaterial == null)
        {
            Material source = pickupRenderer.sharedMaterial;
            // The referenced Resources material preserves the exact Lit emission
            // keyword combination used by this runtime tutorial effect in builds.
            Material template = Resources.Load<Material>("TutorialGuideGlow");
            emissiveMaterial = new Material(template != null && template.shader == source.shader ? template : source);
            emissiveMaterial.CopyPropertiesFromMaterial(source);
            peakEmission = Color.Lerp(emissiveMaterial.color, Color.white, .4f) * 4f;
            emissiveMaterial.EnableKeyword("_EMISSION");
            pickupRenderer.sharedMaterial = emissiveMaterial;
        }
        emissiveMaterial.SetColor(EmissionColor, Color.black);
        blinkStartedAt = Time.time;
    }

    void OnDestroy()
    {
        if (emissiveMaterial != null)
            Destroy(emissiveMaterial);
    }

    void Update()
    {
        if (respawnAt >= 0 && Time.time >= respawnAt)
        {
            respawnAt = -1;
            SetAvailable(true);
        }

        if (guideBlinkActive && IsAvailable)
        {
            float phase = Mathf.Repeat(Time.time - blinkStartedAt, GuideBlinkPeriod) / GuideBlinkPeriod;
            float intensity = .5f - .5f * Mathf.Cos(phase * 2f * Mathf.PI);
            emissiveMaterial.SetColor(EmissionColor, Color.Lerp(Color.black, peakEmission, intensity));
        }

        transform.Rotate(0, spinSpeed * Time.deltaTime, 0, Space.World);
        transform.position = basePosition + Vector3.up * Mathf.Sin(Time.time * 2f + basePosition.x) * .15f;
    }

    // False while the cube has been taken and is waiting to respawn. The AI uses this to pick a lane.
    public bool IsAvailable => trigger != null && trigger.enabled;

    void OnTriggerEnter(Collider other)
    {
        overlaps.Add(other);
        TryCollectFrom(other);
    }

    void OnTriggerStay(Collider other)
    {
        // Control can change while this car is already inside the trigger.
        overlaps.Add(other);
        TryCollectFrom(other);
    }

    void OnTriggerExit(Collider other)
    {
        overlaps.Remove(other);
        if (attemptedCar == null) return;
        foreach (Collider overlap in overlaps)
            if (overlap != null && overlap.attachedRigidbody == attemptedCar.Body)
                return;
        attemptedCar = null;
    }

    void OnDisable()
    {
        overlaps.Clear();
        attemptedCar = null;
    }

    void TryCollectFrom(Collider other)
    {
        if(!IsAvailable)
        {
            return;
        }
        if (inventory == null || inventory.CurrentCar == null)
            return;
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return;
        Transform controlled = inventory.CurrentCar.transform;
        if (other.transform != controlled && !other.transform.IsChildOf(controlled))
            return;
        if (RequiresShield && !inventory.IsShielded)
            return;
        // One attempt per controlled car/overlap, with a retry if swap immunity
        // expires while still touching the block. Multiple car colliders must not
        // duplicate collection attempts or analytics events each physics tick.
        if (attemptedCar == inventory.CurrentCar && attemptedDuringImmunity == inventory.IsImmune)
            return;
        attemptedCar = inventory.CurrentCar;
        attemptedDuringImmunity = inventory.IsImmune;
        bool collected = inventory.TryCollect(type);
        PlayerTouched?.Invoke(this, collected);

        if (!collected)
            return;

        SetAvailable(false);
        if (respawnSeconds > 0)
            respawnAt = Time.time + respawnSeconds;
    }

    internal void RestoreForTutorial()
    {
        respawnAt = -1;
        SetAvailable(true);
    }

    internal void HideForTutorial()
    {
        respawnAt = -1;
        SetAvailable(false);
    }

    // For AI cars. Same as the player grabbing it: hide it and start the respawn timer.
    public bool TakeForAI()
    {
        if (!IsAvailable)
            return false;
        SetAvailable(false);
        if (respawnSeconds > 0)
            respawnAt = Time.time + respawnSeconds;
        return true;
    }

    void SetAvailable(bool available)
    {
        overlaps.Clear();
        attemptedCar = null;
        if (available && emissiveMaterial != null)
        {
            blinkStartedAt = Time.time;
            emissiveMaterial.SetColor(EmissionColor, Color.black);
        }
        pickupRenderer.enabled = available;
        trigger.enabled = available;
    }
}
