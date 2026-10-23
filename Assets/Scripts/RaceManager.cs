using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }

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

    void Start()
    {
        timeLeft = raceDuration;

        if (car != null)
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

        if (IsRacing)
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

        int totalSeconds = Mathf.CeilToInt(timeLeft);

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
        if (IsRacing)
            fuel = Mathf.Min(100f, fuel + amount);
    }

    // Empties the tank and returns how much fuel was in it.
    public float DumpFuel()
    {
        if (!IsRacing) return 0;
        float dumped = fuel;
        fuel = 0;
        return dumped;
    }

    public void ReachCheckpoint(bool byOpponent)
    {
        if (!IsRacing) return;
        if (byOpponent) opponentCheckpointReached = true;
        else checkpointReached = true;
    }

    public void CrossFinish(bool byOpponent)
    {
        if (!IsRacing) return;
        bool reached = byOpponent ? opponentCheckpointReached : checkpointReached;
        if (requireCheckpoint && !reached) return;

        if (!finished && timeLeft > 0)
        {
            if (byOpponent) EndRace(false, "AI WINS!");
            else EndRace(true, "FINISH! YOU WIN!");
        }
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
        if (car != null) car.GetComponent<FuelPickupAnalytics>()?.EndSession();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMainMenu()
    {
        if (car != null) car.GetComponent<FuelPickupAnalytics>()?.EndSession();
        SceneManager.LoadScene("MainMenuCanvas");
    }
}
