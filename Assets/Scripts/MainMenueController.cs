using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;
    public CarSelectionMenu carSelectionMenu;

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

    public void StartTutorial()
    {
        SceneManager.LoadScene("AlphaRaceScene");
    }

    // NEW: lets a button load any scene by name (used by the Level 1/2/3 buttons)
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}