using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }

    public Transform car;
    public Rigidbody carBody;
    public TMP_Text timerText, speedText, fuelText, resultText;
    public Button restartButton;

    public float raceDuration = 30f;
    public float fuel = 100f;
    public float fuelDrainPerSecond = 1.2f;
    public float fuelDrainPerSpeed = .025f;

    float timeLeft;
    bool finished;
    Vector3 previousPosition;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        timeLeft = raceDuration;

        if (car != null)
            previousPosition = car.position;

        if (resultText != null)
            resultText.gameObject.SetActive(false);

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartRace);
            restartButton.gameObject.SetActive(false);
        }
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

        if (!finished)
        {
            timeLeft = Mathf.Max(0, timeLeft - Time.deltaTime);
            fuel = Mathf.Max(0, fuel -
                (fuelDrainPerSecond + speedKph * fuelDrainPerSpeed)
                * Time.deltaTime);

            if (timeLeft <= 0)
                EndRace(false, "TIME UP!");
            else if (fuel <= 0)
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

    public void CrossFinish()
    {
        if (!finished && timeLeft > 0)
            EndRace(true, "FINISH! YOU WIN!");
    }

    void EndRace(bool won, string message)
    {
        finished = true;

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = message;
        }

        if (restartButton != null)
            restartButton.gameObject.SetActive(true);

        Debug.Log(message);
    }

    public void RestartRace()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}