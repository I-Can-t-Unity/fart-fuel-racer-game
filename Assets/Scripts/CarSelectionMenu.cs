using UnityEngine;
using UnityEngine.SceneManagement;

public class CarSelectionMenu : MonoBehaviour
{
    public GameObject selectionPanel;

    public LevelSelectionMenu levelSelection;

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

    // Preserve existing button OnClick bindings (indices 0, 1, 2).
    public void SelectCar(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex > 2) return;
        PlayerPrefs.SetInt("SelectedCarColor", colorIndex);
        // Compatibility only; Jason's color system should read SelectedCarColor.
        PlayerPrefs.SetInt("SelectedCarFuel", colorIndex);
        PlayerPrefs.Save();
        if (levelSelection == null) levelSelection = GetComponent<LevelSelectionMenu>();
        if (levelSelection == null) levelSelection = gameObject.AddComponent<LevelSelectionMenu>();
        CloseSelection();
        levelSelection.Open();
    }
}
