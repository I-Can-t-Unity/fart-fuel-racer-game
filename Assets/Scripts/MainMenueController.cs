
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

}
