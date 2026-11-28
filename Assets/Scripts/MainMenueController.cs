using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class MainMenuController : MonoBehaviour
{
    public GameObject instructionPanel;
    [SerializeField] LevelSelectionMenu levelSelectionMenu;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void RequestGameFullscreen();
#endif

    public void PlayGame()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        RequestGameFullscreen();
#endif
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
