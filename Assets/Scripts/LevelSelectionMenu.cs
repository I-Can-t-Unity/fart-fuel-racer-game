using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectionMenu : MonoBehaviour
{
    public GameObject selectionPanel;
    public CarSelectionMenu colorSelection;
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

        if (backButton != null)
            backButton.onClick.AddListener(Back);

        Close();
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
        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
        Focus(firstRaceButton);
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
    }
}
