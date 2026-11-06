using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs the standalone Drive and Stop lesson using the same car controller as the races.
public class AlphaTutorialManager : MonoBehaviour
{
    [SerializeField] CarDriver playerCar;
    [SerializeField] Transform steeringGate;
    [SerializeField] Transform stoppingBay;
    [SerializeField] TMP_Text tutorialMessage;
    [SerializeField] Button retryButton;
    [SerializeField] Button mainMenuButton;

    const float BrakePromptDistance = 14f;
    const float StoppedSpeedKmh = 1f;
    const float StopHoldSeconds = 0.35f;
    const float IntroTransitionSeconds = 0.4f;

    bool passedGate;
    bool complete;
    float stoppedFor;

    void Start()
    {
        playerCar.Respawned += ResetLesson;
        retryButton.onClick.AddListener(Retry);
        mainMenuButton.onClick.AddListener(ReturnToMenu);
        ResetLesson();
        ShowIntroduction();
    }

    void ShowIntroduction()
    {
        // Reuse the car's countdown lock so the player cannot move while reading the intro.
        playerCar.SetCountdownHold(true);

        // Save the scene-authored corner layout before expanding the panel into the intro.
        var panel = (RectTransform)tutorialMessage.transform.parent;
        Vector2 cornerAnchor = panel.anchorMin;
        Vector2 cornerPosition = panel.anchoredPosition;
        Vector2 cornerSize = panel.sizeDelta;
        Vector2 messagePosition = tutorialMessage.rectTransform.anchoredPosition;
        Vector2 messageSize = tutorialMessage.rectTransform.sizeDelta;
        bool showKeyboard = Gamepad.current == null;
        Vector2 introSize = new Vector2(640f, showKeyboard ? 410f : 290f);
        Vector2 introMessagePosition = new Vector2(0f, showKeyboard ? 116f : 38f);
        Vector2 introMessageSize = new Vector2(580f, showKeyboard ? 116f : 140f);
        Vector2 centerAnchor = new Vector2(0.5f, 0.5f);
        // The panel has a top-left pivot, so offset it by half its size to center it.
        Vector2 introPosition = new Vector2(-introSize.x, introSize.y) * 0.5f;

        panel.anchorMin = panel.anchorMax = centerAnchor;
        panel.anchoredPosition = introPosition;
        panel.sizeDelta = introSize;
        tutorialMessage.rectTransform.anchoredPosition = introMessagePosition;
        tutorialMessage.rectTransform.sizeDelta = introMessageSize;
        tutorialMessage.text = "<size=155%><b>Tutorial 1</b></size>\nFollow the guide marks. Stop in the bay.";
        RectTransform keyboard = null;
        if (showKeyboard)
            keyboard = DrawKeyboard(panel);
        else
            tutorialMessage.text += $"\n<size=80%>{Controls}</size>";

        // Clone the button for its styling and keep the original available for completion.
        Button okButton = Instantiate(retryButton, panel);
        okButton.name = "Tutorial OK";
        var buttonRect = (RectTransform)okButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 52f);
        buttonRect.sizeDelta = new Vector2(180f, 58f);
        okButton.targetGraphic.color = new Color(0.16f, 0.29f, 0.51f);
        TMP_Text label = okButton.GetComponentInChildren<TMP_Text>(true);
        label.text = "OK";
        label.color = Color.white;
        okButton.onClick.AddListener(() =>
        {
            okButton.gameObject.SetActive(false);
            Destroy(okButton.gameObject);
            if (keyboard != null)
                Destroy(keyboard.gameObject);
            ShowPrompt("Follow the guide marks");
            StartCoroutine(MoveToCorner());
        });
        okButton.gameObject.SetActive(true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(okButton.gameObject);

        IEnumerator MoveToCorner()
        {
            // Keep the car held until the panel clears the center of the driving view.
            float elapsed = 0f;
            while (elapsed < IntroTransitionSeconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / IntroTransitionSeconds);
                panel.anchorMin = panel.anchorMax = Vector2.Lerp(centerAnchor, cornerAnchor, t);
                panel.anchoredPosition = Vector2.Lerp(introPosition, cornerPosition, t);
                panel.sizeDelta = Vector2.Lerp(introSize, cornerSize, t);
                tutorialMessage.rectTransform.anchoredPosition = Vector2.Lerp(introMessagePosition, messagePosition, t);
                tutorialMessage.rectTransform.sizeDelta = Vector2.Lerp(introMessageSize, messageSize, t);
            }

            panel.anchorMin = panel.anchorMax = cornerAnchor;
            panel.anchoredPosition = cornerPosition;
            panel.sizeDelta = cornerSize;
            tutorialMessage.rectTransform.anchoredPosition = messagePosition;
            tutorialMessage.rectTransform.sizeDelta = messageSize;
            playerCar.SetCountdownHold(false);
        }
    }

    RectTransform DrawKeyboard(RectTransform panel)
    {
        // Positions are relative to the diagram so the keycaps and captions move together.
        RectTransform diagram = Place("Keyboard Controls", panel, 0f, -12f, 570f, 156f);

        Key("W", -125f, 27f, 46f);
        Key("A", -178f, -26f, 46f);
        Key("S", -125f, -26f, 46f);
        Key("D", -72f, -26f, 46f);
        Caption("Accelerate", -125f, 66f, 130f);
        Caption("Left", -235f, -26f, 62f);
        Caption("Reverse", -125f, -66f, 100f);
        Caption("Right", -15f, -26f, 62f);

        Key("SPACE", 98f, 27f, 114f);
        Key("R", 98f, -26f, 46f);
        Caption("Brake", 208f, 27f, 95f);
        Caption("Reset", 208f, -26f, 95f);
        return diagram;

        void Key(string key, float x, float y, float width)
        {
            RectTransform keycap = Place(key + " Key", diagram, x, y, width, 44f);
            Image face = keycap.gameObject.AddComponent<Image>();
            face.color = new Color(0.16f, 0.29f, 0.51f);
            face.raycastTarget = false;
            Label(key, keycap, 0f, 0f, width, 25f, Color.white, FontStyles.Bold);
        }

        void Caption(string action, float x, float y, float width)
            => Label(action, diagram, x, y, width, 21f, tutorialMessage.color, FontStyles.Normal);

        void Label(string value, RectTransform parent, float x, float y, float width,
            float fontSize, Color color, FontStyles style)
        {
            var label = Place(value, parent, x, y, width, 40f).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = tutorialMessage.font;
            label.text = value;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        RectTransform Place(string name, RectTransform parent, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.gameObject.layer = parent.gameObject.layer;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }

    void FixedUpdate()
    {
        if (complete || !playerCar.CanDrive)
            return;

        // Each scene marker's scale defines its footprint. Local X and Z of ±0.5 are its edges.
        Vector3 position = playerCar.transform.position;
        Vector3 gatePosition = steeringGate.InverseTransformPoint(position);
        Vector3 bayPosition = stoppingBay.InverseTransformPoint(position);

        if (!passedGate && Mathf.Abs(gatePosition.x) <= 0.5f && Mathf.Abs(gatePosition.z) <= 0.5f)
            passedGate = true;

        bool insideBay = Mathf.Abs(bayPosition.x) <= 0.5f && Mathf.Abs(bayPosition.z) <= 0.5f;
        string brakeKey = Gamepad.current != null ? "A" : "Space";
        // Require a sustained, grounded stop after the gate so a brief slowdown cannot pass.
        // IsBraking also includes the car controller's automatic braking when changing direction.
        bool brakingToStop = passedGate && insideBay && playerCar.IsBraking
            && playerCar.GroundedWheelCount > 0 && playerCar.SpeedKmh < StoppedSpeedKmh;
        stoppedFor = brakingToStop ? stoppedFor + Time.fixedDeltaTime : 0f;

        if (stoppedFor >= StopHoldSeconds)
        {
            ShowCompletion();
            return;
        }

        if (!passedGate)
        {
            ShowPrompt(gatePosition.z > 0.5f ? "Reverse to the striped gate" : "Follow the guide marks");
        }
        else if (insideBay)
        {
            ShowPrompt($"{brakeKey} · Brake in the bay");
        }
        else if (bayPosition.z > 0.5f)
        {
            ShowPrompt("Reverse into the bay");
        }
        else
        {
            float distanceToBay = Vector3.Dot(stoppingBay.position - position, stoppingBay.forward)
                - stoppingBay.localScale.z * 0.5f;
            ShowPrompt(distanceToBay <= BrakePromptDistance ? $"{brakeKey} · Slow for the bay" : "Head to the stop bay");
        }
    }

    void ShowCompletion()
    {
        complete = true;
        playerCar.StopDriving();

        var panel = (RectTransform)tutorialMessage.transform.parent;
        Vector2 size = new Vector2(640f, 250f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = new Vector2(-size.x, size.y) * 0.5f;
        panel.sizeDelta = size;
        tutorialMessage.rectTransform.anchoredPosition = new Vector2(0f, 44f);
        tutorialMessage.rectTransform.sizeDelta = new Vector2(580f, 116f);
        tutorialMessage.text = "<size=155%><b>Lesson Complete</b></size>\nYou stopped in the bay!";

        // Move the existing buttons into the result panel so they retain their click handlers.
        PlaceButton(retryButton, -145f);
        PlaceButton(mainMenuButton, 145f);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(retryButton.gameObject);

        void PlaceButton(Button button, float x)
        {
            var rect = (RectTransform)button.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 51f);
            rect.sizeDelta = new Vector2(260f, 62f);
            button.gameObject.SetActive(true);
        }
    }

    void ResetLesson()
    {
        // The car handles repositioning on respawn. Clear the progress from that attempt here.
        passedGate = false;
        complete = false;
        stoppedFor = 0f;
        retryButton.gameObject.SetActive(false);
        ShowPrompt("Follow the guide marks");
    }

    static string Controls => Gamepad.current != null
        ? "RT/LT Drive · Left stick Steer · A Brake · Start Reset"
        : "W/S Drive · A/D Steer · Space Brake · R Reset";

    void ShowPrompt(string action)
    {
        string text = $"<b>1 · DRIVE AND STOP</b>\n{action}\n<size=70%>{Controls}</size>";
        if (tutorialMessage.text != text)
            tutorialMessage.text = text;
    }

    void Retry() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    void ReturnToMenu() => SceneManager.LoadScene("MainMenuCanvas");

    void OnDestroy()
    {
        playerCar.Respawned -= ResetLesson;
        retryButton.onClick.RemoveListener(Retry);
        mainMenuButton.onClick.RemoveListener(ReturnToMenu);
    }
}
