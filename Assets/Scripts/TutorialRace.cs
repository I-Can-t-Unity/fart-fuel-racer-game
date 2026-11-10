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
    const float SwapStart = 100f;
    const float ChargeStart = 200f;
    const float ShieldTestDistance = 326f;
    const float RowsStart = 375f;
    static readonly Color PanelColor = new Color(0f, 0f, 0f, .65f);

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
    FuelPickup shieldBlock;
    TMP_Text controls, cue;
    bool ready, chargeSection, rowsStarted;
    float overturnedFor, turboCueUntil, shieldCueUntil;

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
        inventory.pickupFartSeconds = 2f;
        inventory.TurboKeyEnabled = false;
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

    float Progress(CarDriver car)
    {
        Vector3 position = car.transform.position;
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
        else BeginChargeSection();
    }

    void BeginChargeSection()
    {
        if (chargeSection) return;
        chargeSection = true;
        foreach (FuelPickup block in swapBlocks) block.HideForTutorial();
        CarDriver player = inventory.CurrentCar;
        float lane = PlayerLane();
        for (int i = 0; i < 3; i++)
            chargeBlocks.Add(AddBlock(232f + i * 27f, lane, inventory.carFuel, 4f));
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
        if (pickup == shieldBlock)
        {
            shieldCueUntil = Time.time + 2.5f;
        }
        else if (chargeBlocks.Contains(pickup))
        {
            if (inventory.IsShielded)
                shieldBlock.RestoreForTutorial();
            else if (inventory.Charges == 1)
            {
                fuelHUD.SetChargeBarVisible(true);
                turboCueUntil = Time.time + 1.4f;
            }
        }
    }

    void OnPlayerReset()
    {
        overturnedFor = 0;
        if (rowsStarted) return;
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
        turboCueUntil = shieldCueUntil = 0f;
    }

    void Update()
    {
        if (inventory == null || race.IsFinished) return;
        if (!ready && controls != null)
        {
            controls.text = Gamepad.current != null
                ? "Left stick  Steer     RT  Accelerate     A  Brake"
                : "A / D  Steer     W  Accelerate     Space  Brake";
        }
        if (!race.IsRacing || inventory.CurrentCar == null) return;

        CarDriver player = inventory.CurrentCar;
        bool overturned = Vector3.Dot(player.transform.up, Vector3.up) < .2f && player.SpeedKmh < 8f;
        overturnedFor = overturned ? overturnedFor + Time.deltaTime : 0f;
        if (player.transform.position.y < -4f || overturnedFor >= 1f)
            player.RequestReset();

        float progress = Progress(player);
        if (!chargeSection && progress >= ChargeStart && progress < ChargeStart + 35f)
            BeginChargeSection();

        if (!rowsStarted && progress >= RowsStart && progress < RowsStart + 35f)
        {
            rowsStarted = true;
            fuelHUD.SetChargeBarVisible(true);
            SetPracticeRespawn();
            foreach (AICarWaypointDriver driver in drivers) driver.huntCubes = true;
        }

        for (int i = 0; i < drivers.Length; i++)
        {
            CarDriver rival = inventory.cars[i];
            if (rival == player || rival.IsStopped) continue;
            float gap = Progress(rival) - progress;
            drivers[i].speedKph = Mathf.Lerp(57f, 24f, Mathf.InverseLerp(10f, 37f, gap));
        }

        if (shieldBlock != null && shieldBlock.IsAvailable && !inventory.IsShielded)
            shieldBlock.HideForTutorial();

        if (Time.time < shieldCueUntil)
            SetCue("Swap blocked! You stay in your car");
        else if (!chargeSection && progress < 70f)
            SetCue(Gamepad.current != null ? "Steer with the left stick" : "Use A / D to steer");
        else if (!chargeSection && progress >= SwapStart && progress < 182f)
            SetCue("Grab the rival’s block to swap");
        else if (Time.time < turboCueUntil)
            SetCue("Turbo! That block also filled one slot below");
        else if (shieldBlock != null && shieldBlock.IsAvailable)
            SetCue($"Shield on! Grab the {inventory.NameOf(shieldBlock.type).ToLowerInvariant()} block to test it");
        else if (!rowsStarted && chargeSection && progress >= ChargeStart && progress < ShieldTestDistance)
            SetCue(inventory.Charges > 0
                ? "Fill the bar with matching blocks to get a shield"
                : "Grab a block matching your car for turbo");
        else
            SetCue("");
    }

    void SetCue(string text)
    {
        cue.text = text;
        cue.transform.parent.gameObject.SetActive(text.Length > 0);
    }

    void BuildUI()
    {
        var cuePlate = Panel("Race Cue", new Vector2(.5f, 1f), new Vector2(0, -190), new Vector2(760, 68));
        cue = Label("Cue", cuePlate, Vector2.zero, cuePlate.sizeDelta, 34);
        SetCue("");

        if (ready) return;

        var intro = Panel("Starting Grid", new Vector2(.5f, .5f), Vector2.zero, new Vector2(920, 300));
        Label("Title", intro, new Vector2(0, 92), new Vector2(880, 64), 56).text = "TUTORIAL RACE";
        controls = Label("Controls", intro, new Vector2(0, 16), new Vector2(880, 54), 34);

        Button raceButton = Instantiate(race.restartButton, intro, false);
        raceButton.name = "Race Button";
        ((RectTransform)raceButton.transform).anchoredPosition = new Vector2(0, -82);
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

    void ShowResults(bool won, string message)
    {
        SetCue("");
        string finishTime = RaceManager.FormatRaceTime(Mathf.FloorToInt(race.ElapsedTime));
        race.resultText.text = $"{message}\n<size=65%>{finishTime}</size>";
        race.resultText.gameObject.SetActive(true);
    }

    RectTransform Panel(string label, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(label, race.resultText.transform.parent, anchor, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = PanelColor;
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
        return text;
    }

    static void Focus(Button button)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
