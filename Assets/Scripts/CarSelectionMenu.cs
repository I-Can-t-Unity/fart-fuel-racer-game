using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CarSelectionMenu : MonoBehaviour
{
    public GameObject selectionPanel;
    [SerializeField] Button firstCarButton;
    int selectedLevel = -1;

    internal static FuelType SelectedColor => (FuelType)Mathf.Clamp(
        PlayerPrefs.GetInt("SelectedCarColor", PlayerPrefs.GetInt("SelectedCarFuel", 0)), 0, 2);

    void Awake()
    {
        CloseSelection();
    }

    public void OpenSelection(int level)
    {
        if (selectionPanel == null || !LevelSelectionMenu.IsUnlocked(level))
            return;

        selectedLevel = level;
        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
        if (firstCarButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(firstCarButton.gameObject);
    }

    public void CloseSelection()
    {
        selectedLevel = -1;
        if (selectionPanel != null)
            selectionPanel.SetActive(false);
    }

    // Preserve existing button OnClick bindings (indices 0, 1, 2).
    public void SelectCar(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex > 2 || !LevelSelectionMenu.IsUnlocked(selectedLevel)) return;
        SaveSelectedColor((FuelType)colorIndex);
        int level = selectedLevel;
        CloseSelection();
        LevelSelectionMenu.StartRace(level);
    }

    internal static void SaveSelectedColor(FuelType color)
    {
        int colorIndex = (int)color;
        PlayerPrefs.SetInt("SelectedCarColor", colorIndex);
        // Compatibility only; Jason's color system should read SelectedCarColor.
        PlayerPrefs.SetInt("SelectedCarFuel", colorIndex);
        PlayerPrefs.Save();
    }
}
