using UnityEngine;

public class CarSelectionMenu : MonoBehaviour
{
    public GameObject selectionPanel;

    void Start()
    {
        CloseSelection();
    }

    public void OpenSelection()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(true);
    }

    public void CloseSelection()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);
    }

    public void SelectCar(int fuelType)
    {
        // 0 = Gasoline, 1 = Diesel, 2 = Ethanol
        if (fuelType < 0 || fuelType > 2) return;

        PlayerPrefs.SetInt("SelectedCarFuel", fuelType);
        PlayerPrefs.Save();
        CloseSelection();
    }
}