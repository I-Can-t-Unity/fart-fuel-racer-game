using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectionMenu : MonoBehaviour
{
    [Serializable]
    public class LevelEntry
    {
        public string title;
        public string sceneName;
    }

    public GameObject selectionPanel;
    public CarSelectionMenu colorSelection;

    public Button raceButton;
    public Button tutorialButton;
    public Button gainPositionButton;
    public Button protectLeadButton;
    public Button backButton;

    public LevelEntry[] levels = new[]
    {
        new LevelEntry { title = "Tutorial", sceneName = "" },
        new LevelEntry { title = "Race", sceneName = "LoopRaceScene" },
        new LevelEntry { title = "Gain position", sceneName = "" },
        new LevelEntry { title = "Protect your lead", sceneName = "" }
    };

    void Awake()
    {
        if (colorSelection == null)
            colorSelection = GetComponent<CarSelectionMenu>();

        if (raceButton != null)
            raceButton.onClick.AddListener(PlayRace);

        if (tutorialButton != null)
            tutorialButton.onClick.AddListener(PlayTutorial);

        if (gainPositionButton != null)
            gainPositionButton.onClick.AddListener(PlayGainPosition);

        if (protectLeadButton != null)
            protectLeadButton.onClick.AddListener(PlayProtectLead);

        if (backButton != null)
            backButton.onClick.AddListener(Back);

        Close();
    }

    public void Open()
    {
        if (selectionPanel == null)
        {
            Debug.LogError("Assign LevelSelectionPanel.", this);
            return;
        }

        SetAvailable(tutorialButton, 0);
        SetAvailable(raceButton, 1);
        SetAvailable(gainPositionButton, 2);
        SetAvailable(protectLeadButton, 3);

        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
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

    void PlayRace() => LoadEntry(1);
    void PlayTutorial() => LoadEntry(0);
    void PlayGainPosition() => LoadEntry(2);
    void PlayProtectLead() => LoadEntry(3);

    void SetAvailable(Button button, int index)
    {
        if (button != null)
            button.interactable = IsAvailable(index);
    }

    bool IsAvailable(int index)
    {
        return levels != null
            && index >= 0
            && index < levels.Length
            && levels[index] != null
            && !string.IsNullOrWhiteSpace(levels[index].sceneName)
            && Application.CanStreamedLevelBeLoaded(levels[index].sceneName);
    }

    void LoadEntry(int index)
    {
        if (IsAvailable(index))
            LoadLevel(levels[index].sceneName);
    }

    public void LoadLevel(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)
            || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning("Scene is not in the active Build Profile: " + sceneName);
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    void OnDestroy()
    {
        if (raceButton != null)
            raceButton.onClick.RemoveListener(PlayRace);

        if (tutorialButton != null)
            tutorialButton.onClick.RemoveListener(PlayTutorial);

        if (gainPositionButton != null)
            gainPositionButton.onClick.RemoveListener(PlayGainPosition);

        if (protectLeadButton != null)
            protectLeadButton.onClick.RemoveListener(PlayProtectLead);

        if (backButton != null)
            backButton.onClick.RemoveListener(Back);
    }
}