using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;
    public CarSelectionMenu carSelectionMenu;
    [SerializeField] GameObject tutorialHint;
    [SerializeField] Button tutorialsButton;
    [SerializeField] Button playButton;

    void Start()
    {
        bool completedTutorial = AlphaTutorialManager.HasCompletedTutorial;
        if (tutorialHint != null)
            tutorialHint.SetActive(!completedTutorial);

        SetButtonEmphasis(tutorialsButton, !completedTutorial);
        SetButtonEmphasis(playButton, completedTutorial);
    }

    static void SetButtonEmphasis(Button button, bool emphasized)
    {
        if (button == null)
            return;

        button.targetGraphic.color = emphasized
            ? new Color32(255, 210, 63, 255) : new Color32(30, 43, 69, 255);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.color = emphasized ? new Color32(14, 22, 37, 255) : new Color32(230, 230, 230, 255);
    }

    public void PlayGame()
    {
        carSelectionMenu.OpenSelection();
    }

    public void OpenInstruction()
    {
        instructionPanel.SetActive(true);
    }

    public void CloseInstruction()
    {
        instructionPanel.SetActive(false);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void OpenTutorialSelection() => carSelectionMenu.levelSelection.OpenTutorials();

    // NEW: lets a button load any scene by name (used by the Level 1/2/3 buttons)
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
