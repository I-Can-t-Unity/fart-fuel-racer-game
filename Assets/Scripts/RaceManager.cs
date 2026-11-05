using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }

    [Header("Color switching")]
    public bool colorSwitchingMode = true;
    [Tooltip("Physical cars in color order: Brown, Green, Purple. Assign all three.")]
    public CarDriver[] racers = new CarDriver[0];
    [Tooltip("Optional obsolete fuel panel containers to hide, without hiding the speed HUD.")]
    public GameObject[] obsoleteFuelPanels = new GameObject[0];
    public Transform CurrentPlayerCar => car;
    public IReadOnlyList<CarDriver> Racers => racers;
    public float ElapsedTime { get; private set; }
    public event System.Action<Transform, Transform> ControlledCarChanged;
    readonly HashSet<CarDriver> eligibleCars = new HashSet<CarDriver>();
    readonly Dictionary<CarDriver, System.Action> resetHandlers = new Dictionary<CarDriver, System.Action>();
    bool initialized;

    // Kept for existing scene references and teammate integrations.
    public Transform car;
    public Rigidbody carBody;
    // Optional AI car; the player loses if it finishes first.
    public Transform opponent;
    public TMP_Text timerText, speedText, fuelText, resultText;
    public Button restartButton;
    public Button mainMenuButton;

    // On loop tracks the car starts on the finish line, so a lap only
    // counts after it has driven through a checkpoint.
    public bool requireCheckpoint;

    public float raceDuration = 30f;
    public float fuel = 100f;
    // Old fuel/speed system:
    // public float fuelDrainPerSecond = 1.2f;
    public float fuelDrainPerSecond = 0f;
    public float fuelDrainPerSpeed = .05f;
    [Tooltip("Extra fuel used per second at full throttle, so easing off saves fuel.")]
    public float fuelDrainPerThrottle = 2.4f;
    [Tooltip("With an empty tank the engine dies and the car coasts; the race is lost once it has been stopped this long.")]
    public float stallSeconds = 2f;

    float timeLeft, stalledFor;
    bool finished;
    // Lets the AI's own fuel tank stop draining once the race is over, like the player's.
    public bool IsFinished => finished;
    public bool IsCountingDown { get; private set; } = true;
    public bool IsRacing => !IsCountingDown && !finished;
    // Whether the player won, and the result shown on screen (for analytics).
    public event System.Action<bool, string> RaceEnded;
    bool checkpointReached, opponentCheckpointReached;
    Vector3 previousPosition;
    CarDriver driver;

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        timeLeft = raceDuration;
        // Let scene/grid and AI Start methods finish before assigning control/paint.
        foreach (CarDriver vehicle in FindObjectsByType<CarDriver>(FindObjectsSortMode.None))
            vehicle.SetCountdownHold(true);
        yield return null;
        InitializeRacers();
        if (colorSwitchingMode)
            HideLegacyHUD();

        if (!colorSwitchingMode && car != null)
        {
            previousPosition = car.position;

            // Resetting puts the car back on the start/finish line, so the
            // lap has to be driven again instead of counting as a finish.
            driver = car.GetComponent<CarDriver>();
            if (driver != null)
                driver.Respawned += () => checkpointReached = false;
        }

        if (resultText != null)
            resultText.gameObject.SetActive(false);

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartRace);
            restartButton.gameObject.SetActive(false);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            mainMenuButton.gameObject.SetActive(false);
        }

        StartCoroutine(StartCountdown());
    }

    IEnumerator StartCountdown()
    {
        CarDriver[] vehicles = FindObjectsByType<CarDriver>(FindObjectsSortMode.None);
        foreach (CarDriver vehicle in vehicles)
            vehicle.SetCountdownHold(true);

        TMP_Text countdownText = null;
        if (resultText != null)
        {
            countdownText = Instantiate(resultText, resultText.transform.parent);
            countdownText.name = "CountdownText";
            countdownText.fontSize = 96;
            countdownText.raycastTarget = false;
            countdownText.transform.SetAsLastSibling();
            countdownText.gameObject.SetActive(true);
        }

        for (int count = 3; count > 0; count--)
        {
            if (countdownText != null)
                countdownText.text = count.ToString();
            yield return new WaitForSeconds(1f);
        }

        IsCountingDown = false;
        foreach (CarDriver vehicle in vehicles)
        {
            if (vehicle != null)
                vehicle.SetCountdownHold(false);
        }

        if (countdownText != null)
            countdownText.text = "GO!";
        yield return new WaitForSeconds(1f);
        if (countdownText != null)
            Destroy(countdownText.gameObject);
    }

    void Update()
    {
        if (car == null) return;

        float speedKph;

        if (carBody != null)
            speedKph = carBody.linearVelocity.magnitude * 3.6f;
        else
            speedKph = Vector3.Distance(car.position, previousPosition)
                / Mathf.Max(Time.deltaTime, .001f) * 3.6f;

        previousPosition = car.position;

        if (IsRacing && colorSwitchingMode)
            ElapsedTime += Time.deltaTime;

        if (IsRacing && !colorSwitchingMode)
        {
            timeLeft = Mathf.Max(0, timeLeft - Time.deltaTime);
            // Old fuel/speed system: drain from time and speed only.
            // fuel = Mathf.Max(0, fuel -
            //     (fuelDrainPerSecond + speedKph * fuelDrainPerSpeed)
            //     * Time.deltaTime);
            fuel = Mathf.Max(0, fuel -
                (fuelDrainPerSecond + speedKph * fuelDrainPerSpeed
                 + (driver != null ? Mathf.Abs(driver.Throttle) * fuelDrainPerThrottle : 0))
                * Time.deltaTime);

            // An empty tank kills the engine but the car keeps coasting; the race
            // is only lost if it rolls to a stop before picking up more fuel.
            // Old fuel/speed system:
            // if (driver != null)
            //     driver.EngineRunning = fuel > 0;
            if (driver != null)
            {
                driver.EngineRunning = fuel > 0;
                driver.FuelFraction = fuel / 100f;
            }
            stalledFor = fuel <= 0 && speedKph < 2f ? stalledFor + Time.deltaTime : 0;

            if (timeLeft <= 0)
                EndRace(false, "TIME UP!");
            else if (stalledFor >= stallSeconds)
                EndRace(false, "OUT OF FUEL!");
        }

        int totalSeconds = colorSwitchingMode ? Mathf.FloorToInt(ElapsedTime) : Mathf.CeilToInt(timeLeft);

        if (timerText != null)
            timerText.text =
                $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";

        if (speedText != null)
            speedText.text =
                $"SPEED\n<size=160%><b>{speedKph:0}</b></size>\nkm/h";

        if (fuelText != null)
            fuelText.text =
                $"FUEL\n<size=160%><b>{fuel:0}%</b></size>";
    }

    public void AddFuel(float amount)
    {
        if (colorSwitchingMode) return;
        if (IsRacing)
            fuel = Mathf.Min(100f, fuel + amount);
    }

    // Empties the tank and returns how much fuel was in it.
    public float DumpFuel()
    {
        if (colorSwitchingMode || !IsRacing) return 0;
        float dumped = fuel;
        fuel = 0;
        return dumped;
    }

    public void ReachCheckpoint(bool byOpponent)
    {
        if (colorSwitchingMode)
        {
            ReachCheckpoint(byOpponent ? opponent : car);
            return;
        }
        if (!IsRacing) return;
        if (byOpponent) opponentCheckpointReached = true;
        else checkpointReached = true;
    }

    public void CrossFinish(bool byOpponent)
    {
        if (colorSwitchingMode)
        {
            CrossFinish(byOpponent ? opponent : car);
            return;
        }
        if (!IsRacing) return;
        bool reached = byOpponent ? opponentCheckpointReached : checkpointReached;
        if (requireCheckpoint && !reached) return;

        if (!finished && timeLeft > 0)
        {
            if (byOpponent) EndRace(false, "AI WINS!");
            else EndRace(true, "FINISH! YOU WIN!");
        }
    }

    void InitializeRacers()
    {
        if (!colorSwitchingMode) return;
        var roster = new List<CarDriver>();
        foreach (CarDriver candidate in racers)
            if (candidate != null && !roster.Contains(candidate)) roster.Add(candidate);
        // Compatibility with the old two-car scene; the final grid must be explicit.
        if (roster.Count == 0)
        {
            if (car != null && car.GetComponentInParent<CarDriver>() != null)
                roster.Add(car.GetComponentInParent<CarDriver>());
            if (opponent != null && opponent.GetComponentInParent<CarDriver>() != null
                && !roster.Contains(opponent.GetComponentInParent<CarDriver>()))
                roster.Add(opponent.GetComponentInParent<CarDriver>());
        }
        racers = roster.ToArray();
        if (racers.Length != 3)
            Debug.LogWarning("Color race requires exactly three cars. Assign RaceManager > Racers in Brown/Green/Purple order. Current count: " + racers.Length, this);
        fuel = 100f;
        fuelDrainPerSecond = fuelDrainPerSpeed = fuelDrainPerThrottle = 0f;
        for (int i = 0; i < racers.Length; i++)
        {
            CarDriver physicalCar = racers[i];
            physicalCar.SetCountdownHold(true);
            physicalCar.EngineRunning = true;
            physicalCar.FuelFraction = 1f;
            System.Action onReset = () => eligibleCars.Remove(physicalCar);
            resetHandlers.Add(physicalCar, onReset);
            physicalCar.Respawned += onReset;
            var appearance = physicalCar.GetComponent<ApplySelectedCar>();
            if (appearance != null) appearance.ApplyColor(i % 3);
        }
        initialized = true;
        int selection = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCarColor", PlayerPrefs.GetInt("SelectedCarFuel", 0)), 0, 2);
        CarDriver startingCar = racers.Length > selection ? racers[selection] : racers.Length > 0 ? racers[0] : null;
        if (startingCar != null) InitializeStartingControl(startingCar);
    }

    // Only initial ownership is established here. Jason owns live AI handoffs.
    void InitializeStartingControl(CarDriver selected)
    {
        foreach (CarDriver physicalCar in racers)
        {
            AICarWaypointDriver ai = physicalCar.GetComponent<AICarWaypointDriver>();
            if (ai != null) ai.enabled = physicalCar != selected;
            else if (physicalCar != selected)
                Debug.LogWarning("Missing AI driver on " + physicalCar.name + "; teammate grid/handoff setup is required.", physicalCar);
            physicalCar.ExternalInput = physicalCar != selected;
        }
        SetControlledCar(selected.transform);
    }

    // Call AFTER a successful player/AI handoff. Does not teleport, respawn, alter
    // velocity, reset checkpoints, or toggle AI. False means the reference was rejected.
    public bool SetControlledCar(Transform nextCar)
    {
        if (!initialized || finished || nextCar == null) return false;
        CarDriver next = nextCar.GetComponentInParent<CarDriver>();
        if (next == null || System.Array.IndexOf(racers, next) < 0) return false;
        Transform previous = car;
        car = next.transform;
        carBody = next.GetComponent<Rigidbody>();
        driver = next;
        previousPosition = car.position;
        foreach (CarFollowCamera camera in FindObjectsByType<CarFollowCamera>(FindObjectsSortMode.None))
            camera.SetTarget(car);
        foreach (FollowCamera camera in FindObjectsByType<FollowCamera>(FindObjectsSortMode.None))
            camera.target = car;
        ControlledCarChanged?.Invoke(previous, car);
        return true;
    }

    public CarDriver ResolveRacer(Transform candidate)
    {
        if (candidate == null) return null;
        CarDriver physicalCar = candidate.GetComponentInParent<CarDriver>();
        return physicalCar != null && System.Array.IndexOf(racers, physicalCar) >= 0 ? physicalCar : null;
    }

    public bool HasCheckpoint(Transform physicalCar)
    {
        CarDriver racer = ResolveRacer(physicalCar);
        return racer != null && eligibleCars.Contains(racer);
    }

    public void ReachCheckpoint(Transform physicalCar)
    {
        CarDriver racer = ResolveRacer(physicalCar);
        if (IsRacing && racer != null) eligibleCars.Add(racer);
    }

    public void CrossFinish(Transform physicalCar)
    {
        CarDriver racer = ResolveRacer(physicalCar);
        if (!IsRacing || racer == null || (requireCheckpoint && !eligibleCars.Contains(racer))) return;
        bool won = racer.transform == car;
        EndRace(won, won ? "FINISH! YOU WIN!" : "AI WINS!");
    }

    void HideLegacyHUD()
    {
        foreach (GameObject oldPanel in obsoleteFuelPanels)
            if (oldPanel != null) oldPanel.SetActive(false);
        if (fuelText != null) fuelText.gameObject.SetActive(false);
        foreach (FuelInventoryUI bag in FindObjectsByType<FuelInventoryUI>(FindObjectsSortMode.None))
        {
            bag.enabled = false;
            foreach (string childName in new[] { "FuelBag", "Message" })
            {
                Transform oldPanel = bag.transform.Find(childName);
                if (oldPanel != null) oldPanel.gameObject.SetActive(false);
            }
        }
        foreach (RivalMixHUD rivalHUD in FindObjectsByType<RivalMixHUD>(FindObjectsSortMode.None))
        {
            rivalHUD.enabled = false;
            if (rivalHUD.mixText != null) rivalHUD.mixText.gameObject.SetActive(false);
        }
        foreach (GaugeAnimator gauge in FindObjectsByType<GaugeAnimator>(FindObjectsSortMode.None))
            if (gauge.fuelRing != null) gauge.fuelRing.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        foreach (var entry in resetHandlers)
            if (entry.Key != null) entry.Key.Respawned -= entry.Value;
        if (Instance == this) Instance = null;
    }

    void EndRace(bool won, string message)
    {
        if (finished)
            return;
        finished = true;

        foreach (CarDriver vehicle in FindObjectsByType<CarDriver>(FindObjectsSortMode.None))
            vehicle.StopDriving();

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = message;
        }

        if (restartButton != null)
            restartButton.gameObject.SetActive(true);

        if (mainMenuButton != null)
            mainMenuButton.gameObject.SetActive(true);

        Debug.Log(message);
        RaceEnded?.Invoke(won, message);
    }

    public void RestartRace()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenuCanvas");
    }
}
