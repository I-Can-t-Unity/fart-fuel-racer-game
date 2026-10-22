using UnityEngine;

[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(FuelInventory))]
public class ApplySelectedCar : MonoBehaviour
{
    public Material dieselBody;
    public Material ethanolBody;

    void Awake()
    {
        FuelInventory inventory = GetComponent<FuelInventory>();

        int choice = Mathf.Clamp(
            PlayerPrefs.GetInt("SelectedCarFuel", 0), 0, 2);

        inventory.carFuel = (FuelType)choice;

        Material selectedBody = null;

        if (choice == 1)
            selectedBody = dieselBody;
        else if (choice == 2)
            selectedBody = ethanolBody;

        if (selectedBody == null || inventory.bodyRenderer == null)
            return;

        Material[] materials =
            inventory.bodyRenderer.sharedMaterials;

        int slot = inventory.bodyMaterialSlot;

        if (slot >= 0 && slot < materials.Length)
        {
            materials[slot] = selectedBody;
            inventory.bodyRenderer.sharedMaterials = materials;
        }
    }
}