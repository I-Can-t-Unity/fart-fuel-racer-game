using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Isolated physics practice. No race timer, fuel drain, AI or analytics.
public class SuperFartJumpTestArea : MonoBehaviour
{
    public FuelInventory inventory;
    public SuperFartJumpGate gate;
    public Transform restartPoint;
    public float landingStart = 14f;
    public float landingEnd = 65f;
    public float fallHeight = -5f;

    public static bool StartWithMix = true;
    public int Activations { get; private set; }
    public int Denials { get; private set; }
    public bool Landed { get; private set; }
    public bool Recovered { get; private set; }

    Rigidbody car;
    string status = "Approach the pink gate. Save your mix for the jump.";
    float launchedAt;

    void OnEnable()
    {
        SuperFartJumpGate.Activated += OnActivated;
        SuperFartJumpGate.Denied += OnDenied;
    }

    void OnDisable()
    {
        SuperFartJumpGate.Activated -= OnActivated;
        SuperFartJumpGate.Denied -= OnDenied;
    }

    void Start()
    {
        car = inventory.GetComponent<Rigidbody>();
        if (StartWithMix)
        {
            // Supply the recipe without the pickup boosts moving a parked test car.
            CarDriver driver = inventory.GetComponent<CarDriver>();
            driver.SetCountdownHold(true);
            FuelType wrong = inventory.SuperFartFuel;
            inventory.TryCollect(wrong);
            inventory.TryCollect(wrong);
            driver.SetCountdownHold(false);
            status = "Super Fart supplied for this trial. Drive into the pink gate.";
        }
        else
            status = "Empty-bag trial: the gate should reject you without launching.";
    }

    void OnActivated(SuperFartJumpGate source, Rigidbody enteringCar)
    {
        if (source != gate || enteringCar != car) return;
        Activations++;
        launchedAt = Time.time;
        status = "LAUNCHED - mix spent. Aim for the green landing platform.";
    }

    void OnDenied(SuperFartJumpGate source, Rigidbody enteringCar)
    {
        if (source != gate || enteringCar != car) return;
        Denials++;
        status = "DENIED - no Super Fart. This test lane has no main-route bypass.";
    }

    void Update()
    {
        Keyboard keys = Keyboard.current;
        if (keys != null)
        {
            if (keys.f1Key.wasPressedThisFrame) Restart(true);
            else if (keys.f2Key.wasPressedThisFrame) Restart(false);
        }
        if (car == null) return;

        if (!Landed && !Recovered && Activations > 0 && Time.time > launchedAt + .5f
            && car.position.z > landingStart + 2f && car.position.z < landingEnd)
        {
            int grounded = 0;
            foreach (WheelCollider wheel in car.GetComponentsInChildren<WheelCollider>())
                if (wheel.isGrounded) grounded++;
            if (grounded >= 3)
            {
                Landed = true;
                status = "LANDED - brake before the platform ends. F1 starts another trial.";
            }
        }

        if (car.position.y < fallHeight || car.position.z > landingEnd + 12f)
        {
            car.position = restartPoint.position;
            car.rotation = restartPoint.rotation;
            car.linearVelocity = Vector3.zero;
            car.angularVelocity = Vector3.zero;
            Recovered = true;
            status = "RECOVERED at approach. Mix is not restored. F1/F2 starts a fresh trial.";
        }
    }

    void Restart(bool withMix)
    {
        StartWithMix = withMix;
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(620, Screen.width - 32), 170), GUI.skin.box);
        GUILayout.Label("SUPER FART JUMP TEST - 14 m gap | no race timer or fuel drain");
        GUILayout.Label("WASD / arrows: drive | Space: brake | Q: spend mix before gate");
        GUILayout.Label("R: return to start (keeps bag) | F1: new trial with mix | F2: empty trial");
        GUILayout.Label(status);
        if (inventory != null)
            GUILayout.Label($"Held: {inventory.HeldMix} | Speed: {(car != null ? car.linearVelocity.magnitude * 3.6f : 0):0} km/h");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Restart with Super Fart")) Restart(true);
        if (GUILayout.Button("Restart empty")) Restart(false);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
