using UnityEngine;

[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(FuelInventory))]
public class ApplySelectedCar : MonoBehaviour
{
    // Preserve existing serialized material assignments.
    public Material dieselBody;
    public Material ethanolBody;
    [Range(0, 2)] public int physicalColorIndex;
    Material originalBody;
    Material runtimeBody;
    readonly System.Collections.Generic.List<Material> createdMaterials = new System.Collections.Generic.List<Material>();

    void Awake()
    {
        FuelInventory inventory = GetComponent<FuelInventory>();
        if (inventory.bodyRenderer != null)
        {
            Material[] materials = inventory.bodyRenderer.sharedMaterials;
            if (inventory.bodyMaterialSlot >= 0 && inventory.bodyMaterialSlot < materials.Length)
                originalBody = materials[inventory.bodyMaterialSlot];
        }
        ApplyColor(physicalColorIndex);
    }

    public void ApplyColor(int colorIndex)
    {
        physicalColorIndex = Mathf.Clamp(colorIndex, 0, 2);
        FuelInventory inventory = GetComponent<FuelInventory>();
        inventory.carFuel = (FuelType)physicalColorIndex;
        if (inventory.bodyRenderer == null) return;
        Material[] materials = inventory.bodyRenderer.sharedMaterials;
        int slot = inventory.bodyMaterialSlot;
        if (slot < 0 || slot >= materials.Length) return;
        Material selected = physicalColorIndex == 1 ? dieselBody : physicalColorIndex == 2 ? ethanolBody : originalBody;
        runtimeBody = null;
        if (selected == null) selected = originalBody;
        if (selected == null) return;
        runtimeBody = new Material(selected);
        createdMaterials.Add(runtimeBody);
        Color color = FuelPalette.ColorOf((FuelType)physicalColorIndex);
        if (runtimeBody.HasProperty("_BaseColor")) runtimeBody.SetColor("_BaseColor", color);
        if (runtimeBody.HasProperty("_Color")) runtimeBody.SetColor("_Color", color);
        materials[slot] = runtimeBody;
        inventory.bodyRenderer.sharedMaterials = materials;
    }

    void OnDestroy()
    {
        foreach (Material material in createdMaterials)
            if (material != null) Destroy(material);
    }
}
