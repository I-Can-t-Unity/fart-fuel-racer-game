using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectionMenu : MonoBehaviour
{
    const string MenuScene = "MainMenuCanvas";
    const string ProgressKey = "HighestUnlockedRaceLevel";
    const int NoDestination = -2;
    const int LevelPicker = -1;
    static readonly string[] RaceScenes = { "TutorialRace", "Level1", "Level2", "Level3", "Level4", "Level5" };
    static readonly Color LockedColor = new Color(.35f, .35f, .35f);

    public GameObject selectionPanel;
    public CarSelectionMenu colorSelection;
    [SerializeField] Button[] levelButtons;
    [SerializeField] Button playButton;
    public Button backButton;

    static int menuDestination = NoDestination;
    Color[] unlockedColors;

    static int HighestUnlocked => Mathf.Clamp(PlayerPrefs.GetInt(ProgressKey), 0, RaceScenes.Length - 1);

    internal static int IndexOfScene(string sceneName) => System.Array.IndexOf(RaceScenes, sceneName);

    internal static bool IsUnlocked(int level) => level >= 0 && level <= HighestUnlocked;

    internal static void CompleteLevel(int level)
    {
        if (level != HighestUnlocked || level >= RaceScenes.Length - 1) return;
        PlayerPrefs.SetInt(ProgressKey, level + 1);
        PlayerPrefs.Save();
    }

    internal static void StartRace(int level)
    {
        if (!IsUnlocked(level)) return;
        Time.timeScale = 1f;
        if (level == 0)
            TutorialRace.Open();
        else
            SceneManager.LoadScene(RaceScenes[level]);
    }

    public static void ReturnFromGameplay() => LoadMenu(LevelPicker);

    internal static void ContinueFromGameplay(int level)
    {
        int nextLevel = level + 1;
        LoadMenu(IsUnlocked(nextLevel) ? nextLevel : LevelPicker);
    }

    static void LoadMenu(int destination)
    {
        menuDestination = destination;
        Time.timeScale = 1f;
        SceneManager.LoadScene(MenuScene);
    }

    void Awake()
    {
        if (colorSelection == null)
            colorSelection = GetComponent<CarSelectionMenu>();

        if (backButton != null)
            backButton.onClick.AddListener(Back);

        if (levelButtons != null)
        {
            unlockedColors = new Color[levelButtons.Length];
            for (int i = 0; i < levelButtons.Length; i++)
                unlockedColors[i] = levelButtons[i].targetGraphic.color;
        }
        Close();
    }

    void Start()
    {
        int destination = menuDestination;
        menuDestination = NoDestination;
        if (destination == NoDestination) return;
        if (destination == LevelPicker)
            Open();
        else
            SelectLevel(destination);
    }

    public void Open()
    {
        if (selectionPanel == null || levelButtons == null || levelButtons.Length != RaceScenes.Length)
        {
            Debug.LogError("Assign the level selection panel and all six race buttons.", this);
            return;
        }

        int highest = HighestUnlocked;
        for (int i = 0; i < levelButtons.Length; i++)
        {
            Button button = levelButtons[i];
            button.interactable = i <= highest;
            button.targetGraphic.color = button.interactable ? unlockedColors[i] : LockedColor;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.alpha = button.interactable ? 1f : .55f;
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? levelButtons[i - 1] : null,
                selectOnDown = i < highest ? levelButtons[i + 1] : backButton
            };
        }

        if (backButton != null)
            backButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = levelButtons[highest] };

        colorSelection?.CloseSelection();
        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
        Focus(levelButtons[highest]);
    }

    public void SelectLevel(int level)
    {
        if (!IsUnlocked(level) || colorSelection == null) return;
        Close();
        colorSelection.OpenSelection(level);
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
        Close();
        Focus(playButton);
    }

    void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(Back);
    }
}
