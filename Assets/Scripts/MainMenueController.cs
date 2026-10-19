
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;

    public void PlayGame()
    {
        SceneManager.LoadScene("LoopRaceScene");
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
