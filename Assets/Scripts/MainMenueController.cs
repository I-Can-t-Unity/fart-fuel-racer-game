using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;
    [SerializeField] LevelSelectionMenu levelSelectionMenu;

    public void PlayGame()
    {
        levelSelectionMenu.Open();
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
}
