using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectionMenu : MonoBehaviour
{
    const string MenuScene = "MainMenuCanvas";
    const string ProgressKey = "HighestUnlockedRaceLevel";
    static readonly string[] RaceScenes = { "TutorialRace", "Level1", "Level2", "Level3", "Level4", "Level5" };
    static readonly Color LockedColor = new Color(.086f, .102f, .125f); // #161A20, dark card like the reference

    public GameObject selectionPanel;
    public CarSelectionMenu colorSelection;
    [SerializeField] Button[] levelButtons;
    [SerializeField] Button playButton;
    public Button backButton;

    static bool openLevelPickerOnStart;
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
        if (level == 0)
        {
            TutorialRace.Open();
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(RaceScenes[level]);
    }

    internal static bool CanContinue(int level) => level >= 0 && IsUnlocked(level + 1);

    internal static void ContinueFromGameplay(int level, FuelType color)
    {
        if (!CanContinue(level)) return;
        CarSelectionMenu.SaveSelectedColor(color);
        StartRace(level + 1);
    }

    public static void ReturnFromGameplay()
    {
        openLevelPickerOnStart = true;
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
        if (!openLevelPickerOnStart) return;
        openLevelPickerOnStart = false;
        Open();
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
            // Locked cards show the padlock instead of the level number.
            Transform lockIcon = button.transform.Find("Lock");
            if (lockIcon != null) lockIcon.gameObject.SetActive(!button.interactable);
            Transform number = button.transform.Find("Number");
            if (number != null) number.gameObject.SetActive(button.interactable);
            // Cards sit in a row, so arrow keys move left/right and Down goes to Back.
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = i > 0 ? levelButtons[i - 1] : null,
                selectOnRight = i < highest ? levelButtons[i + 1] : null,
                selectOnDown = backButton
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
