using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;
    public CarSelectionMenu carSelectionMenu;

    // Carry the final lesson's Start Game action through the menu scene load.
    static bool playOnLoad;

    internal static void StartGameFromTutorial()
    {
        playOnLoad = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenuCanvas");
    }

    void Start()
    {
        if (!playOnLoad)
            return;

        playOnLoad = false;
        PlayGame();
    }

    public void PlayGame()
    {
        if (AlphaTutorialManager.HasCompletedAllTutorials)
            carSelectionMenu.OpenSelection();
        else
            AlphaTutorialManager.StartTutorial();
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

    // NEW: lets a button load any scene by name (used by the Level 1/2/3 buttons)
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
