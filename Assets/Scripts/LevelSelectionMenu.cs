using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectionMenu : MonoBehaviour
{
    public GameObject selectionPanel;
    public CarSelectionMenu colorSelection;
    [SerializeField] GameObject tutorialPanel;
    [SerializeField] Button[] tutorialButtons;
    [SerializeField] Button tutorialBackButton;
    [SerializeField] Button titleTutorialButton;
    [SerializeField] Button firstRaceButton;
    public Button backButton;

    // Carry the destination through the scene load, then consume it once.
    static bool returnToLevelSelection;

    public static void ReturnFromGameplay()
    {
        returnToLevelSelection = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenuCanvas");
    }

    void Awake()
    {
        if (colorSelection == null)
            colorSelection = GetComponent<CarSelectionMenu>();

        for (int i = 0; tutorialButtons != null && i < tutorialButtons.Length; i++)
        {
            int tutorialNumber = i + 1;
            if (tutorialButtons[i] != null)
                tutorialButtons[i].onClick.AddListener(() => AlphaTutorialManager.StartTutorial(tutorialNumber));
        }

        if (backButton != null)
            backButton.onClick.AddListener(Back);
        if (tutorialBackButton != null)
            tutorialBackButton.onClick.AddListener(CloseTutorials);

        Close();
        tutorialPanel?.SetActive(false);
    }

    void Start()
    {
        if (!returnToLevelSelection)
            return;

        returnToLevelSelection = false;
        Open();
    }

    public void Open()
    {
        if (selectionPanel == null)
        {
            Debug.LogError("Assign LevelSelectionPanel.", this);
            return;
        }

        colorSelection?.CloseSelection();
        tutorialPanel?.SetActive(false);
        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
        Focus(firstRaceButton);
    }

    public void OpenTutorials()
    {
        Close();
        colorSelection?.CloseSelection();
        tutorialPanel.SetActive(true);
        tutorialPanel.transform.SetAsLastSibling();
        if (tutorialButtons.Length > 0)
            Focus(tutorialButtons[0]);
    }

    void CloseTutorials()
    {
        tutorialPanel.SetActive(false);
        Focus(titleTutorialButton);
    }

    static void Focus(Button button)
    {
        if (button != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    public void Close()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);
    }

    public void Back()
    {
        if (colorSelection == null)
        {
            Debug.LogError("Assign the CarSelectionMenu reference.", this);
            return;
        }

        Close();
        colorSelection.OpenSelection();
    }

    void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(Back);
        if (tutorialBackButton != null)
            tutorialBackButton.onClick.RemoveListener(CloseTutorials);
    }
}
