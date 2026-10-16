using UnityEngine;

// A floating fuel cube. Driving through it puts the fuel in the car's bag;
// if the bag is full the cube stays where it is.
public class FuelPickup : MonoBehaviour
{
    public FuelType type;
    public FuelInventory inventory;
    public float respawnSeconds = 10f;
    public float spinSpeed = 90f;

    Vector3 basePosition;
    Renderer cubeRenderer;
    Collider trigger;
    float respawnAt = -1;

    void Start()
    {
        basePosition = transform.position;
        cubeRenderer = GetComponent<Renderer>();
        trigger = GetComponent<Collider>();
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
    }

    void OnTriggerEnter(Collider other)
    {
        if (inventory == null || !other.transform.IsChildOf(inventory.transform))
            return;
        if (!inventory.TryCollect(type))
            return;

        SetAvailable(false);
        if (respawnSeconds > 0)
            respawnAt = Time.time + respawnSeconds;
    }

    void SetAvailable(bool available)
    {
        cubeRenderer.enabled = available;
        trigger.enabled = available;
    }
}
