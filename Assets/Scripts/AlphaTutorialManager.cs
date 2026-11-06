using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs the practice lessons using the same driving and pickup logic as the races.
public class AlphaTutorialManager : MonoBehaviour
{
    // The old CompletedTutorial preference only meant that one lesson had been finished.
    const string CompletedLessonsKey = "CompletedTutorialLessons";
    const int AllLessonsMask = (1 << ((int)Lesson.Turbo + 1)) - 1;
    internal static bool HasCompletedAllTutorials =>
        (PlayerPrefs.GetInt(CompletedLessonsKey, 0) & AllLessonsMask) == AllLessonsMask;

    [SerializeField] CarDriver playerCar;
    [SerializeField] Transform steeringGate;
    [SerializeField] Transform stoppingBay;
    [SerializeField] TMP_Text tutorialMessage;
    [SerializeField] Button retryButton;
    [SerializeField] Button mainMenuButton;
    [SerializeField] RaceManager practiceRace;
    [SerializeField] Transform drivingGuide;
    [SerializeField] GameObject practiceFinish;
    [SerializeField] Transform practiceRoad;
    [SerializeField] Transform practiceGround;

    const float BrakePromptDistance = 14f;
    const float StoppedSpeedKmh = 1f;
    const float StopHoldSeconds = 0.35f;
    const float IntroTransitionSeconds = 0.4f;
    const float ChargeRoadLength = 270f;
    const float ChargeBlockSpacing = 24f;
    const float ShieldTargetDistance = 86f;
    const float ChargedCheckpoint = 70f;
    const float ChargeFinishDistance = 147f;

    bool passedGate;
    bool complete;
    float stoppedFor;
    enum Lesson { Drive, Swap, Shield, Turbo }

    // Lessons share the practice road. Consume the next lesson choice once on scene load.
    static Lesson nextLesson;
    Lesson lesson;
    bool UsesPracticeRace => lesson != Lesson.Drive;
    bool IsChargeLesson => lesson == Lesson.Turbo || lesson == Lesson.Shield;
    bool swapped;
    CarDriver blueCar;
    FuelInventory swapInventory;
    GameObject chargeBlocks;
    GameObject shieldTarget;
    Image[] chargePips;
    LineRenderer shieldRing;
    bool chargeActionDone;
    Vector3 chargeStart;
    Quaternion chargeHeading;

    void Start()
    {
        lesson = nextLesson;
        nextLesson = Lesson.Drive;
        if (UsesPracticeRace)
            SetUpPracticeRace();
        else
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
        bool showKeyboard = lesson == Lesson.Drive && Gamepad.current == null;
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
        tutorialMessage.text = lesson switch
        {
            Lesson.Swap => "<size=155%><b>Tutorial 2 · Swap</b></size>\nCollect a block matching another car's color to take control of that car.",
            Lesson.Shield => "<size=155%><b>Tutorial 3 · Shield</b></size>\nEach block of your color gives you one charge.\nThree charges automatically shield you from one swap.",
            Lesson.Turbo => $"<size=155%><b>Tutorial 4 · Turbo</b></size>\nThree charges give you a shield.\n{swapInventory.turboKey} spends that shield on turbo.",
            _ => "<size=155%><b>Tutorial 1 · Drive</b></size>\nFollow the guide marks and stop in the bay."
        };
        RectTransform keyboard = null;
        if (showKeyboard)
            keyboard = DrawKeyboard(panel);
        else if (lesson == Lesson.Drive)
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
        label.text = UsesPracticeRace ? "Next" : "OK";
        label.color = Color.white;
        bool showObjective = UsesPracticeRace;
        okButton.onClick.AddListener(() =>
        {
            if (showObjective)
            {
                showObjective = false;
                tutorialMessage.text = lesson switch
                {
                    Lesson.Swap => "<size=155%><b>Try a Swap</b></size>\nGrab the blue block. Then drive the blue car across the finish.",
                    Lesson.Turbo => $"<size=155%><b>Try Turbo</b></size>\nCollect 3 red blocks to fill the charge meter.\nTurbo can be activated by pressing {swapInventory.turboKey}.",
                    _ => "<size=155%><b>Try Your Shield</b></size>\nA blue block normally swaps you into the blue car.\nWith a shield, you stay in the red car."
                };
                label.text = "OK";
                return;
            }

            okButton.gameObject.SetActive(false);
            Destroy(okButton.gameObject);
            if (keyboard != null)
                Destroy(keyboard.gameObject);
            ShowStartingPrompt();
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
            if (UsesPracticeRace)
            {
                // The race initializes the roster one frame after it is enabled.
                while (!practiceRace.RacersInitialized)
                    yield return null;
                if (blueCar != null)
                    blueCar.SetCountdownHold(false);
                if (chargePips != null)
                    chargePips[0].transform.parent.gameObject.SetActive(true);
            }
            playerCar.SetCountdownHold(false);
        }
    }

    void SetUpPracticeRace()
    {
        drivingGuide.gameObject.SetActive(false);
        if (lesson != Lesson.Turbo)
        {
            float blueLane = lesson == Lesson.Shield ? 4f : 0f;
            // The shield lesson keeps the parked rival in view and out of the player's lane.
            float blueDistance = lesson == Lesson.Shield ? ShieldTargetDistance + 8f : 40f;
            blueCar = Instantiate(playerCar, new Vector3(blueLane, playerCar.transform.position.y, blueDistance),
                playerCar.transform.rotation);
            blueCar.name = "Blue Practice Car";
        }

        swapInventory = playerCar.GetComponent<FuelInventory>();
        swapInventory.cars = blueCar != null ? new[] { playerCar, blueCar } : new[] { playerCar };
        swapInventory.enabled = true;

        playerCar.gameObject.AddComponent<ApplySelectedCar>();
        practiceRace.ConfigureSwapPractice(swapInventory.cars);
        practiceRace.gameObject.SetActive(true);
        if (blueCar != null)
        {
            blueCar.gameObject.AddComponent<ApplySelectedCar>();
            // The handoff needs AI drivers. With no route, the uncontrolled car stays parked.
            playerCar.gameObject.AddComponent<AICarWaypointDriver>();
            blueCar.gameObject.AddComponent<AICarWaypointDriver>();
        }
        practiceRace.RaceEnded += OnPracticeFinished;
        if (lesson == Lesson.Swap)
        {
            swapInventory.Switched += OnPracticeSwap;
            CreatePracticeBlock(FuelType.Diesel, 18f, null);
        }
        else
        {
            SetUpChargeLesson();
        }
    }

    void CreatePracticeBlock(FuelType color, float distance, Transform group, float respawnSeconds = 0f)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = $"{swapInventory.NameOf(color)} Practice Block";
        if (color == FuelType.Diesel)
            block.name = "Blue Practice Block";
        block.transform.SetParent(group, false);
        block.transform.position = new Vector3(0f, 1.1f, distance);
        block.transform.localScale = Vector3.one * 1.4f;
        block.GetComponent<Collider>().isTrigger = true;
        block.GetComponent<Renderer>().sharedMaterial = swapInventory.MaterialOf(color);
        FuelPickup pickup = block.AddComponent<FuelPickup>();
        pickup.type = color;
        pickup.inventory = swapInventory;
        pickup.respawnSeconds = respawnSeconds;
    }

    void SetUpChargeLesson()
    {
        chargeStart = playerCar.transform.position;
        chargeHeading = playerCar.transform.rotation;
        // The race boost can chain across the whole practice strip. Give each pickup
        // a short nudge here so the player has time to read the next instruction.
        swapInventory.pickupFartSeconds = 0.18f;
        var boost = playerCar.GetComponent<FartBoost>();
        boost.acceleration = 5f;
        boost.maxBoostKph = 55f;

        // Keep the start fixed while extending the road and ground for this lesson.
        float extension = ChargeRoadLength - practiceRoad.localScale.z;
        foreach (Transform surface in new[] { practiceRoad, practiceGround })
        {
            surface.position += Vector3.forward * (extension * 0.5f);
            surface.localScale += Vector3.forward * extension;
        }
        var finishTrigger = practiceFinish.GetComponent<BoxCollider>();
        float finishCenter = practiceFinish.transform.TransformPoint(finishTrigger.center).z;
        practiceFinish.transform.position += Vector3.forward * (ChargeFinishDistance - finishCenter);

        chargeBlocks = new GameObject("Practice Charge Blocks");
        for (int i = 0; i < swapInventory.maxCharges; i++)
            CreatePracticeBlock(FuelType.Gasoline, 14f + i * ChargeBlockSpacing, chargeBlocks.transform, 3f);

        if (lesson == Lesson.Shield)
        {
            shieldTarget = new GameObject("Shield Practice Target");
            CreatePracticeBlock(FuelType.Diesel, ShieldTargetDistance, shieldTarget.transform);
            shieldTarget.SetActive(false);
        }
        CreateShieldRing();

        var row = new GameObject("Charge Pips", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(tutorialMessage.transform.parent, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0f);
        row.anchoredPosition = new Vector2(0f, 13f);
        row.sizeDelta = new Vector2(100f, 14f);
        chargePips = new Image[swapInventory.maxCharges];
        for (int i = 0; i < chargePips.Length; i++)
        {
            Image pip = new GameObject($"Charge {i + 1}", typeof(RectTransform)).AddComponent<Image>();
            pip.transform.SetParent(row, false);
            pip.rectTransform.anchoredPosition = new Vector2((i - (chargePips.Length - 1) * 0.5f) * 30f, 0f);
            pip.rectTransform.sizeDelta = new Vector2(24f, 10f);
            pip.raycastTarget = false;
            chargePips[i] = pip;
        }
        row.gameObject.SetActive(false);

        swapInventory.Changed += OnChargesChanged;
        playerCar.Respawned += ReturnToChargeExercise;
        UpdateChargeDisplay();
    }

    void CreateShieldRing()
    {
        // A ground-level ring gives the shield a visible cue outside the HUD.
        shieldRing = new GameObject("Practice Shield Ring").AddComponent<LineRenderer>();
        shieldRing.transform.SetParent(playerCar.transform, false);
        shieldRing.sharedMaterial = swapInventory.MaterialOf(FuelType.Diesel);
        shieldRing.useWorldSpace = false;
        shieldRing.loop = true;
        shieldRing.widthMultiplier = 0.12f;
        shieldRing.positionCount = 48;
        for (int i = 0; i < shieldRing.positionCount; i++)
        {
            float angle = 2f * Mathf.PI * i / shieldRing.positionCount;
            shieldRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * 1.65f, 0.3f, Mathf.Sin(angle) * 2.15f));
        }
        shieldRing.enabled = false;
    }

    void ReturnToChargeExercise()
    {
        var body = playerCar.GetComponent<Rigidbody>();
        Vector3 position = chargeStart;
        if (chargeActionDone || swapInventory.IsShielded)
            position.z = ChargedCheckpoint;
        body.position = position;
        body.rotation = chargeHeading;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        playerCar.GetComponent<FartBoost>().ClearBoost();
        if (chargeActionDone)
            ShowPrompt("Cross the finish");
    }

    void UpdateChargeDisplay()
    {
        for (int i = 0; i < chargePips.Length; i++)
            chargePips[i].color = i < swapInventory.Charges
                ? new Color(0.85f, 0.12f, 0.15f) : new Color(0.72f, 0.74f, 0.77f);
        if (shieldRing != null)
            shieldRing.enabled = swapInventory.IsShielded;
    }

    void OnChargesChanged()
    {
        UpdateChargeDisplay();
        if (chargeActionDone || !playerCar.CanDrive)
            return;

        // The real pickup system reports the action that spent the charges. Each
        // lesson unlocks the finish only after the player demonstrates its action.
        bool teachingShield = lesson == Lesson.Shield;
        if (swapInventory.LastResult == (teachingShield ? "shielded" : "turbo"))
        {
            chargeActionDone = true;
            chargeBlocks.SetActive(false);
            practiceFinish.SetActive(true);
            ShowPrompt(teachingShield ? "Still red! Shield spent. Cross the finish." : "Turbo! Cross the finish");
            return;
        }

        if (teachingShield)
        {
            // Reveal blue only while the player has a full shield.
            shieldTarget.SetActive(swapInventory.IsShielded);
            ShowPrompt(swapInventory.IsShielded ? "Shield ready. A blue block will spend it\nand keep you in the red car."
                : swapInventory.LastResult == "turbo" ? "Shield spent. Recharge with red blocks"
                : "Collect three red blocks");
        }
        else
        {
            ShowPrompt(swapInventory.IsShielded
                ? $"Shield ready. Press {swapInventory.turboKey} to spend it for turbo!"
                : "Collect three red blocks");
        }
    }

    void ShowStartingPrompt()
    {
        ShowPrompt(lesson switch
        {
            Lesson.Swap => "Grab the blue block",
            Lesson.Turbo or Lesson.Shield => "Collect three red blocks",
            _ => "Follow the guide marks"
        });
    }

    void OnPracticeSwap(FuelType previous, FuelType next)
    {
        if (next != FuelType.Diesel)
            return;

        swapped = true;
        // The red car has served its purpose. Hold it in place for the rest of this exercise.
        playerCar.SetCountdownHold(true);
        practiceFinish.SetActive(true);
        ShowPrompt("You're blue! Drive through the finish");
    }

    void OnPracticeFinished(bool won, string message)
    {
        if (won && (swapped || chargeActionDone))
            ShowCompletion();
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
        if (UsesPracticeRace)
        {
            if (IsChargeLesson && !complete && playerCar.CanDrive)
            {
                Vector3 chargePosition = playerCar.transform.position;
                // Recover before the car falls off the end of the practice road.
                // Keep a usable finish stretch ahead until the required action is done.
                float end = chargeActionDone ? ChargeRoadLength - 3f : ChargeFinishDistance - 8f;
                if (Mathf.Abs(chargePosition.x) > 7f || chargePosition.z > end
                    || chargePosition.z < -2f || chargePosition.y < -2f)
                    ReturnToChargeExercise();
            }
            if (lesson == Lesson.Swap && !complete && !swapped && playerCar.CanDrive)
                ShowPrompt(playerCar.transform.position.z > 20f ? "Reverse to the blue block" : "Grab the blue block");
            return;
        }
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
        int completedLessons = PlayerPrefs.GetInt(CompletedLessonsKey, 0);
        int updatedLessons = completedLessons | (1 << (int)lesson);
        if (updatedLessons != completedLessons)
        {
            PlayerPrefs.SetInt(CompletedLessonsKey, updatedLessons);
            PlayerPrefs.Save();
        }

        if (!UsesPracticeRace)
            playerCar.StopDriving();
        if (chargePips != null)
            chargePips[0].transform.parent.gameObject.SetActive(false);

        var panel = (RectTransform)tutorialMessage.transform.parent;
        Vector2 size = new Vector2(640f, 250f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = new Vector2(-size.x, size.y) * 0.5f;
        panel.sizeDelta = size;
        tutorialMessage.rectTransform.anchoredPosition = new Vector2(0f, 44f);
        tutorialMessage.rectTransform.sizeDelta = new Vector2(580f, 116f);
        tutorialMessage.text = lesson switch
        {
            Lesson.Swap => "<size=155%><b>Lesson Complete</b></size>\nYou swapped cars and crossed the finish!",
            Lesson.Turbo => "<size=155%><b>Lesson Complete</b></size>\nYou spent three charges on turbo!",
            Lesson.Shield => "<size=155%><b>Lesson Complete</b></size>\nYour shield blocked the swap!",
            _ => "<size=155%><b>Lesson Complete</b></size>\nYou stopped in the bay!"
        };

        bool finalLesson = lesson == Lesson.Turbo;
        Button next = Instantiate(retryButton, panel);
        next.name = finalLesson ? "Start Game" : "Next Tutorial";
        TMP_Text nextLabel = next.GetComponentInChildren<TMP_Text>();
        nextLabel.text = next.name;
        nextLabel.fontSize = 27f;
        if (finalLesson)
            next.onClick.AddListener(MainMenuController.StartGameFromTutorial);
        else
            next.onClick.AddListener(() => LoadLesson(lesson + 1));

        PlaceButton(next, -200f);
        PlaceButton(retryButton, 0f);
        PlaceButton(mainMenuButton, 200f);
        Button defaultButton = finalLesson ? mainMenuButton : next;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(defaultButton.gameObject);

        void PlaceButton(Button button, float x)
        {
            var rect = (RectTransform)button.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 51f);
            rect.sizeDelta = new Vector2(185f, 62f);
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
        ShowStartingPrompt();
    }

    static string Controls => Gamepad.current != null
        ? "RT/LT Drive · Left stick Steer · A Brake · Start Reset"
        : "W/S Drive · A/D Steer · Space Brake · R Reset";

    void ShowPrompt(string action)
    {
        string title = lesson switch
        {
            Lesson.Swap => "2 · STEAL A CAR",
            Lesson.Shield => "3 · BLOCK A SWAP",
            Lesson.Turbo => "4 · TURBO",
            _ => "1 · DRIVE AND STOP"
        };
        string text = $"<b>{title}</b>\n{action}";
        if (lesson == Lesson.Drive)
            text += $"\n<size=70%>{Controls}</size>";
        if (tutorialMessage.text != text)
            tutorialMessage.text = text;
    }

    internal static void StartTutorial() => LoadLesson(Lesson.Drive);

    void Retry() => LoadLesson(lesson);

    static void LoadLesson(Lesson destination)
    {
        nextLesson = destination;
        Time.timeScale = 1f;
        SceneManager.LoadScene("AlphaRaceScene");
    }
    void ReturnToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenuCanvas");
    }

    void OnDestroy()
    {
        playerCar.Respawned -= ResetLesson;
        if (UsesPracticeRace)
        {
            practiceRace.RaceEnded -= OnPracticeFinished;
            swapInventory.Switched -= OnPracticeSwap;
            swapInventory.Changed -= OnChargesChanged;
            playerCar.Respawned -= ReturnToChargeExercise;
        }
        retryButton.onClick.RemoveListener(Retry);
        mainMenuButton.onClick.RemoveListener(ReturnToMenu);
    }
}
