using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class TutorialRace : MonoBehaviour
{
    const string SceneName = "TutorialRace";
    const float RedLane = 1.8f;
    const float BlueLane = -2.8f;
    const float ThirdLane = 5.2f;
    const float SwapCueDistance = 54f;
    const float ChargeStart = 205f;
    const float FirstMatchDistance = 258f;
    const float SecondMatchDistance = 332f;
    const float ThirdMatchDistance = 380f;
    const float ShieldTestDistance = 446f;
    const float RowsStart = 495f;

    [SerializeField] CarDriver redCar;
    [SerializeField] RaceManager race;
    [SerializeField] Transform ground;
    [SerializeField] GameObject shieldVisual;
    [SerializeField] RacePositionHUD standings;
    [SerializeField] FuelInventoryUI fuelHUD;

    static bool replay;

    LoopTrackGenerator track;
    FuelInventory inventory;
    AICarWaypointDriver[] drivers;
    readonly List<float> metres = new List<float>();
    readonly List<FuelPickup> swapBlocks = new List<FuelPickup>();
    readonly List<FuelPickup> chargeBlocks = new List<FuelPickup>();
    FuelPickup shieldBlock, activeGuideBlock;
    TMP_Text cue;
    RectTransform keyboardControls, gamepadControls;
    bool ready, chargeSection, rowsStarted;
    float overturnedFor, swapCueUntil, shieldCueUntil;

    internal static void Open()
    {
        replay = false;
        Load();
    }

    internal static void Retry()
    {
        replay = true;
        Load();
    }

    static void Load()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneName);
    }

    void Start()
    {
        ready = replay;
        replay = false;
        FuelType startingColor = CarSelectionMenu.SelectedColor;
        FuelType rivalColor = (FuelType)(((int)startingColor + 1) % 3);
        FuelType thirdColor = (FuelType)(((int)startingColor + 2) % 3);
        BuildTrack();

        PlaceCar(redCar, 6f, RedLane);
        CarDriver rival = CreateRival(13f, BlueLane, rivalColor);
        CarDriver thirdRacer = CreateRival(6f, ThirdLane, thirdColor);

        inventory = redCar.GetComponent<FuelInventory>();
        inventory.cars = new[] { redCar, rival, thirdRacer };
        inventory.shieldVisual = shieldVisual;
        inventory.enabled = true;

        foreach (CarDriver car in inventory.cars)
        {
            FartBoost boost = car.GetComponent<FartBoost>();
            boost.acceleration = 6f;
            boost.maxBoostKph = 64f;
        }

        drivers = new[]
        {
            AddRivalDriver(redCar, RedLane, startingColor),
            AddRivalDriver(rival, BlueLane, rivalColor),
            AddRivalDriver(thirdRacer, ThirdLane, thirdColor)
        };

        swapBlocks.Add(AddBlock(125f, RedLane, rivalColor, 4f));
        swapBlocks.Add(AddBlock(164f, RedLane, rivalColor, 4f));
        AddPracticeRows();
        fuelHUD.SetChargeBarVisible(false);
        fuelHUD.ShowPickupMessages = false;
        BuildUI();
        standings.track = track;
        race.ConfigureTutorial(inventory.cars, startingColor, () => ready);
        race.RaceEnded += ShowResults;
        inventory.Switched += OnSwap;
        FuelPickup.PlayerTouched += OnPickup;
        foreach (CarDriver car in inventory.cars) car.Respawned += OnPlayerReset;
        race.gameObject.SetActive(true);
    }

    void OnDestroy()
    {
        FuelPickup.PlayerTouched -= OnPickup;
        if (inventory != null) inventory.Switched -= OnSwap;
        if (race != null) race.RaceEnded -= ShowResults;
    }

    void BuildTrack()
    {
        track = new GameObject("Tutorial Circuit").AddComponent<LoopTrackGenerator>();
        track.enabled = false;
        track.layout = LoopTrackGenerator.Layout.Tutorial;
        track.startIndex = 0;
        track.roadWidth = 14f;
        track.curbWidth = 0f;
        track.wallGap = 0f;
        track.groundToFit = ground;
        track.Generate();

        float distance = 0f;
        for (int i = 0; i < track.centerline.Count; i++)
        {
            metres.Add(distance);
            distance += Vector3.Distance(track.GetPoint(i), track.GetPoint(i + 1));
        }
        metres.Add(distance);

        AddGate("Finish Line", 0f, false);
        AddGate("Lap Checkpoint", distance * .67f, true);
    }

    void AddGate(string label, float distance, bool checkpoint)
    {
        var gate = new GameObject(label);
        gate.transform.SetPositionAndRotation(Point(distance, 0f, 1.5f), Heading(distance));
        var trigger = gate.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(track.roadWidth + 4f, 3f, 1.5f);
        gate.AddComponent<FinishLine>().isCheckpoint = checkpoint;
    }

    int Segment(float distance)
    {
        int index = metres.BinarySearch(distance);
        return Mathf.Clamp(index >= 0 ? index : ~index - 1, 0, metres.Count - 2);
    }

    Quaternion Heading(float distance) => Quaternion.LookRotation(track.GetForward(Segment(distance)));

    Vector3 Point(float distance, float lane, float height)
    {
        int index = Segment(distance);
        float t = Mathf.InverseLerp(metres[index], metres[index + 1], distance);
        Vector3 center = Vector3.Lerp(track.GetPoint(index), track.GetPoint(index + 1), t);
        Vector3 side = Vector3.Cross(track.GetForward(index), Vector3.up);
        return center + side * lane + Vector3.up * height;
    }

    float Progress(Vector3 position)
    {
        float closest = float.PositiveInfinity;
        float progress = 0f;
        for (int i = 0; i < metres.Count - 1; i++)
        {
            Vector3 from = track.GetPoint(i);
            Vector3 segment = track.GetPoint(i + 1) - from;
            float t = Mathf.Clamp01(Vector3.Dot(position - from, segment) / segment.sqrMagnitude);
            float error = (position - (from + segment * t)).sqrMagnitude;
            if (error >= closest) continue;
            closest = error;
            progress = Mathf.Lerp(metres[i], metres[i + 1], t);
        }
        return progress;
    }

    void PlaceCar(CarDriver car, float distance, float lane)
    {
        Vector3 position = Point(distance, lane, .5f);
        Quaternion rotation = Heading(distance);
        Rigidbody body = car.GetComponent<Rigidbody>();
        body.position = position;
        body.rotation = rotation;
        car.transform.SetPositionAndRotation(position, rotation);
        car.SetRespawnPoint(position, rotation);
    }

    CarDriver CreateRival(float distance, float lane, FuelType color)
    {
        CarDriver car = Instantiate(redCar, Point(distance, lane, .5f), Heading(distance));
        car.name = $"{SuperFartRecipe.ColorNameOf(color)} Rival";
        PlaceCar(car, distance, lane);
        return car;
    }

    AICarWaypointDriver AddRivalDriver(CarDriver car, float lane, FuelType color)
    {
        var ai = car.gameObject.AddComponent<AICarWaypointDriver>();
        ai.track = track;
        ai.laneOffset = lane;
        ai.speedKph = 51f;
        ai.huntCubes = false;
        ai.paintWithFuelColour = false;
        ai.useShortcuts = false;
        ai.catchUp = false;
        ai.equalizeLaneLength = false;
        car.GetComponent<AIFuelTank>().ownFuel = color;
        return ai;
    }

    void AddPracticeRows()
    {
        float finish = metres[metres.Count - 1];
        var rowGroup = new GameObject("Practice Pickup Rows");
        rowGroup.SetActive(false);
        var spawner = rowGroup.AddComponent<FuelPickupSpawner>();
        spawner.track = track;
        spawner.inventory = inventory;
        spawner.abTestLaneSpacings = System.Array.Empty<float>();
        spawner.rowWaypoints = new[]
        {
            Segment(RowsStart + 26f), Segment((RowsStart + finish) * .5f), Segment(finish - 64f)
        };
        rowGroup.SetActive(true);
    }

    FuelPickup AddBlock(float distance, float lane, FuelType color, float width)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = inventory.NameOf(color) + " Block";
        block.transform.SetParent(transform);
        block.transform.position = Point(distance, lane, .9f);
        const float pickupSize = 1.2f;
        block.transform.localScale = Vector3.one * pickupSize;
        block.GetComponent<Renderer>().sharedMaterial = inventory.MaterialOf(color);
        var trigger = block.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(width, 1.95f, width) / pickupSize;
        var pickup = block.AddComponent<FuelPickup>();
        pickup.type = color;
        pickup.inventory = inventory;
        pickup.respawnSeconds = 0f;
        return pickup;
    }

    void OnSwap(FuelType previous, FuelType next)
    {
        if (rowsStarted) SetPracticeRespawn();
        else
        {
            BeginChargeSection();
            swapCueUntil = Time.time + 1.8f;
        }
    }

    void BeginChargeSection()
    {
        if (chargeSection) return;
        chargeSection = true;
        foreach (FuelPickup block in swapBlocks) block.HideForTutorial();
        CarDriver player = inventory.CurrentCar;
        float lane = PlayerLane();
        chargeBlocks.Add(AddBlock(FirstMatchDistance, lane, inventory.carFuel, 4f));
        chargeBlocks.Add(AddBlock(SecondMatchDistance, lane, inventory.carFuel, 4f));
        chargeBlocks.Add(AddBlock(ThirdMatchDistance, lane, inventory.carFuel, 4f));
        int otherIndex = player == redCar ? 1 : 0;
        shieldBlock = AddBlock(ShieldTestDistance, lane, race.ColorForRacerIndex(otherIndex), 4f);
        shieldBlock.RequiresShield = true;
        shieldBlock.HideForTutorial();
        player.SetRespawnPoint(Point(ChargeStart, lane, .5f), Heading(ChargeStart));
    }

    float PlayerLane()
    {
        int index = System.Array.IndexOf(inventory.cars, inventory.CurrentCar);
        return drivers[index].laneOffset;
    }

    void SetPracticeRespawn()
    {
        float distance = RowsStart - 8f;
        inventory.CurrentCar.SetRespawnPoint(Point(distance, PlayerLane(), .5f), Heading(distance));
    }

    void OnPickup(FuelPickup pickup, bool collected)
    {
        if (!collected || rowsStarted) return;
        if (pickup == activeGuideBlock) SetGuideBlock(null);
        if (pickup == shieldBlock)
        {
            shieldCueUntil = Time.time + 2.5f;
        }
        else if (chargeBlocks.Contains(pickup))
        {
            if (inventory.IsShielded)
                shieldBlock.RestoreForTutorial();
            else if (inventory.Charges == 1)
                fuelHUD.SetChargeBarVisible(true);
        }
    }

    void OnPlayerReset()
    {
        overturnedFor = 0;
        if (rowsStarted) return;
        SetGuideBlock(null);
        if (!chargeSection)
        {
            foreach (FuelPickup block in swapBlocks) block.RestoreForTutorial();
            return;
        }

        CarDriver player = inventory.CurrentCar;
        player.GetComponent<FartBoost>().ClearBoost();
        inventory.ResetTutorialCharges(player);
        foreach (FuelPickup block in chargeBlocks) block.RestoreForTutorial();
        shieldBlock.HideForTutorial();
        fuelHUD.SetChargeBarVisible(false);
        swapCueUntil = shieldCueUntil = 0f;
    }

    void Update()
    {
        if (inventory == null || race.IsFinished) return;
        if (!ready && keyboardControls != null)
        {
            bool gamepad = Gamepad.current != null;
            keyboardControls.gameObject.SetActive(!gamepad);
            gamepadControls.gameObject.SetActive(gamepad);
        }
        if (!race.IsRacing || inventory.CurrentCar == null) return;

        CarDriver player = inventory.CurrentCar;
        bool overturned = Vector3.Dot(player.transform.up, Vector3.up) < .2f && player.SpeedKmh < 8f;
        overturnedFor = overturned ? overturnedFor + Time.deltaTime : 0f;
        if (player.transform.position.y < -4f || overturnedFor >= 1f)
            player.RequestReset();

        float progress = Progress(player.transform.position);
        if (!chargeSection && progress >= ChargeStart && progress < ChargeStart + 35f)
            BeginChargeSection();

        if (!rowsStarted && progress >= RowsStart && progress < RowsStart + 35f)
        {
            rowsStarted = true;
            fuelHUD.SetChargeBarVisible(true);
            fuelHUD.ShowPickupMessages = true;
            SetPracticeRespawn();
            foreach (AICarWaypointDriver driver in drivers) driver.huntCubes = true;
        }

        for (int i = 0; i < drivers.Length; i++)
        {
            CarDriver rival = inventory.cars[i];
            if (rival == player || rival.IsStopped) continue;
            float gap = Progress(rival.transform.position) - progress;
            drivers[i].speedKph = Mathf.Lerp(57f, 24f, Mathf.InverseLerp(10f, 37f, gap));
        }

        if (shieldBlock != null && shieldBlock.IsAvailable && !inventory.IsShielded)
            shieldBlock.HideForTutorial();

        if (Time.time < shieldCueUntil)
            SetCue("Swap blocked! You stay in your car");
        else if (!chargeSection && progress >= SwapCueDistance && progress < 182f)
            SetCue("Grab the rival’s block to swap", NextGuideBlock(swapBlocks, progress));
        else if (Time.time < swapCueUntil)
            SetCue($"Swapped! You’re in the {inventory.NameOf(inventory.carFuel).ToLowerInvariant()} car");
        else if (!rowsStarted && shieldBlock != null && shieldBlock.IsAvailable)
            SetCue("Shield on! Try a different color", GuideBlockAhead(shieldBlock, progress) ? shieldBlock : null);
        else if (!rowsStarted && chargeSection && progress >= ChargeStart && progress < ShieldTestDistance)
            SetCue(inventory.Charges switch
            {
                1 => "Turbo! Match twice more to fill the bar",
                2 => "One more match activates your shield",
                _ => "Match your car’s color for turbo"
            }, NextGuideBlock(chargeBlocks, progress));
        else
            SetCue("");
    }

    FuelPickup NextGuideBlock(List<FuelPickup> blocks, float progress)
    {
        foreach (FuelPickup block in blocks)
            if (GuideBlockAhead(block, progress)) return block;
        return null;
    }

    bool GuideBlockAhead(FuelPickup block, float progress)
        => block.IsAvailable && Progress(block.transform.position) >= progress;

    void SetGuideBlock(FuelPickup block)
    {
        if (activeGuideBlock == block) return;
        if (activeGuideBlock != null) activeGuideBlock.SetGuideBlink(false);
        activeGuideBlock = block;
        if (activeGuideBlock != null) activeGuideBlock.SetGuideBlink(true);
    }

    void SetCue(string text, FuelPickup guideBlock = null)
    {
        SetGuideBlock(guideBlock);
        if (cue.text != text) cue.text = text;
        cue.transform.parent.gameObject.SetActive(text.Length > 0);
    }

    void BuildUI()
    {
        var cuePlate = Panel("Race Cue", new Vector2(.5f, 1f), new Vector2(0, -190), new Vector2(760, 68));
        cue = Label("Cue", cuePlate, Vector2.zero, cuePlate.sizeDelta, 34);
        SetCue("");

        if (ready) return;

        var intro = Panel("Starting Grid", new Vector2(.5f, .5f), Vector2.zero, new Vector2(920, 344), opacity: .88f);
        Label("Get Ready...", intro, new Vector2(0, 116), new Vector2(880, 64), 56);
        keyboardControls = ControlGroup("Keyboard Controls", intro, new Vector2(0, 23));
        var wasd = ControlGroup("WASD", keyboardControls, new Vector2(-155, 0));
        AddKeycap(wasd, "W", new Vector2(0, 34), 44);
        AddKeycap(wasd, "A", new Vector2(-52, -15), 44);
        AddKeycap(wasd, "S", new Vector2(0, -15), 44);
        AddKeycap(wasd, "D", new Vector2(52, -15), 44);
        Label("Accelerate", wasd, new Vector2(101, 34), new Vector2(144, 32), 25);
        Label("Steer left", wasd, new Vector2(-152, -15), new Vector2(144, 32), 25);
        Label("Steer right", wasd, new Vector2(152, -15), new Vector2(144, 32), 25);
        Label("Reverse", wasd, new Vector2(0, -54), new Vector2(144, 32), 25);
        ControlHint(keyboardControls, "Brake", 250, ("Space", 96)).anchoredPosition += Vector2.up * 26f;
        ControlHint(keyboardControls, "Reset", 250, ("R", 44)).anchoredPosition += Vector2.down * 26f;
        gamepadControls = ControlGroup("Gamepad Controls", intro, Vector2.zero);
        ControlHint(gamepadControls, "Steer", -285, ("Left Stick", 146));
        ControlHint(gamepadControls, "Accelerate", 0, ("RT", 52));
        ControlHint(gamepadControls, "Reverse", 285, ("LT", 52));
        ControlHint(gamepadControls, "Brake", -145, ("A", 44)).anchoredPosition += Vector2.down * 60f;
        ControlHint(gamepadControls, "Reset", 145, ("Start", 92)).anchoredPosition += Vector2.down * 60f;
        gamepadControls.anchoredPosition = new Vector2(0, 45);
        keyboardControls.gameObject.SetActive(Gamepad.current == null);
        gamepadControls.gameObject.SetActive(Gamepad.current != null);

        Button raceButton = Instantiate(race.restartButton, intro, false);
        raceButton.name = "Race Button";
        ((RectTransform)raceButton.transform).anchoredPosition = new Vector2(0, -108);
        raceButton.GetComponentInChildren<TMP_Text>(true).text = "Race";
        raceButton.navigation = new Navigation { mode = Navigation.Mode.None };
        raceButton.onClick.AddListener(() =>
        {
            ready = true;
            intro.gameObject.SetActive(false);
        });
        raceButton.gameObject.SetActive(true);
        Focus(raceButton);
    }

    static RectTransform ControlGroup(string label, Transform parent, Vector2 position)
        => Rect(label, parent, new Vector2(.5f, .5f), position, Vector2.zero);

    RectTransform ControlHint(Transform parent, string action, float x, params (string label, float width)[] keys)
    {
        var hint = ControlGroup(action, parent, new Vector2(x, 0));
        const float gap = 8f;
        const float captionWidth = 144f;
        float width = captionWidth;
        foreach (var key in keys) width += key.width + gap;
        float left = -width * .5f;
        foreach (var key in keys)
        {
            AddKeycap(hint, key.label, new Vector2(left + key.width * .5f, 0), key.width);
            left += key.width + gap;
        }
        var caption = Label(action, hint, new Vector2(left + captionWidth * .5f, 0), new Vector2(captionWidth, 40), 25);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        return hint;
    }

    void AddKeycap(Transform parent, string key, Vector2 position, float width)
    {
        var cap = Rect(key + " Keycap", parent, new Vector2(.5f, .5f), position, new Vector2(width, 40));
        var face = cap.gameObject.AddComponent<Image>();
        face.color = new Color(.92f, .94f, .98f);
        face.raycastTarget = false;
        var edge = cap.gameObject.AddComponent<Shadow>();
        edge.effectColor = new Color(.28f, .36f, .48f);
        edge.effectDistance = new Vector2(0, -4);
        Label(key, cap, Vector2.zero, cap.sizeDelta, 24).color = new Color(.10f, .15f, .23f);
    }

    void ShowResults(bool won, string message)
    {
        SetCue("");
        string finishTime = RaceManager.FormatRaceTime(Mathf.FloorToInt(race.ElapsedTime));
        race.resultText.text = $"{message}\n<size=65%>{finishTime}</size>";
        race.resultText.gameObject.SetActive(true);
    }

    RectTransform Panel(string label, Vector2 anchor, Vector2 position, Vector2 size, float opacity = .65f)
    {
        RectTransform rect = Rect(label, race.resultText.transform.parent, anchor, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, opacity);
        image.raycastTarget = false;
        return rect;
    }

    static RectTransform Rect(string label, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    TMP_Text Label(string label, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(label, parent, new Vector2(.5f, .5f), position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = race.resultText.font;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
        return text;
    }

    static void Focus(Button button)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
