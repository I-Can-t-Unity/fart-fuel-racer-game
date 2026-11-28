using UnityEngine;
using UnityEngine.Rendering;

// Fuel side effects on the car. A fart pushes it forward past its normal top
// speed. Turbo uses a pulsing exhaust light.
// Runs after CarDriver so it can override the wheel torques CarDriver sets.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class FartBoost : MonoBehaviour
{
    public float acceleration = 12f;
    public float maxBoostKph = 100f;
    [Tooltip("Longest a boost can be held back in bends; the held time is used after the bend.")]
    public float maxHeldSeconds = 4f;

    [Header("Boost exhaust")]
    [SerializeField] Vector3 boostExhaustOffset = new Vector3(0f, .86f, -1.6f);

    Rigidbody body;
    CarDriver driver;
    WheelCollider[] wheels;
    float fartUntil, boostStarted;
    float heldSeconds;
    Material exhaustMaterial;
    Light exhaustLight;
    LineRenderer exhaustBeam;

    public bool IsFarting => Time.time < fartUntil;
    public bool IsBoosting => isActiveAndEnabled && IsFarting
        && (driver == null || driver.CanDrive);

    internal void ClearBoost()
    {
        fartUntil = 0f;
        heldSeconds = 0f;
        if (exhaustLight != null)
            exhaustLight.gameObject.SetActive(false);
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        driver = GetComponent<CarDriver>();
        wheels = GetComponentsInChildren<WheelCollider>();
        if (driver != null)
            driver.Respawned += ClearBoost;
    }

    // Farts stack.
    public void Fart(float seconds)
    {
        if (driver != null && !driver.CanDrive)
            return;
        if (!IsFarting)
        {
            boostStarted = Time.time;
            heldSeconds = 0f;
        }
        fartUntil = Mathf.Max(fartUntil, Time.time) + seconds;
    }


    void FixedUpdate()
    {
        if (driver != null && !driver.CanDrive)
            return;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 velocity = body.linearVelocity;

        if (IsFarting)
        {
            // Without throttle, CarDriver's idle brake and PhysX's low-speed tyre grip
            // hold a parked car in place and swallow the push. Release the wheels and
            // signal a tiny drive torque, unless the player is braking on purpose.
            if (driver == null || !driver.IsBraking)
            {
                foreach (WheelCollider wheel in wheels)
                {
                    wheel.brakeTorque = 0;
                    if (wheel.motorTorque == 0)
                        wheel.motorTorque = 1;
                }
            }
            // Don't push past the corner's safe speed. Pause the timer while held back
            // so the rest of the boost fires on the corner exit.
            float boostLimit = maxBoostKph / 3.6f;
            // Read the bend ahead whether or not the player is steering yet, so the boost
            // backs off before the turn-in instead of pushing the car wide.
            float cornerLimit = driver != null ? driver.BoostCornerSafeSpeed() : float.PositiveInfinity;
            float speed = Vector3.Dot(velocity, forward);
            // Boost speed carried into a bend: take back only the extra the boost added
            // (never below the car's normal top speed), at the corner-entry braking rate.
            if (driver != null && driver.GroundedWheelCount >= 2)
            {
                float floor = Mathf.Max(cornerLimit, driver.TopSpeedMs);
                float excess = speed - floor;
                if (excess > 0f)
                    body.AddForce(-forward * Mathf.Min(driver.CornerEntryDecel * Mathf.Clamp01(excess / 1.5f),
                        excess / Time.fixedDeltaTime), ForceMode.Acceleration);
            }
            float headroom = Mathf.Min(boostLimit, cornerLimit) - speed;
            if (headroom > 0f && (driver == null || !driver.IsBraking))
            {
                // Ease out over the final 2 m/s instead of switching full thrust
                // on and off at the limit. Never add more than this step's headroom.
                float thrust = Mathf.Min(acceleration * Mathf.Clamp01(headroom / 2f),
                    headroom / Time.fixedDeltaTime);
                body.AddForce(forward * thrust, ForceMode.Acceleration);
            }
            if (cornerLimit < boostLimit && speed >= cornerLimit - .1f && heldSeconds < maxHeldSeconds)
            {
                fartUntil += Time.fixedDeltaTime;
                heldSeconds += Time.fixedDeltaTime;
            }
        }
    }

    void Update()
    {
        if (IsBoosting)
            UpdateExhaust();
        else if (exhaustLight != null && exhaustLight.gameObject.activeSelf)
            exhaustLight.gameObject.SetActive(false);
    }

    void UpdateExhaust()
    {
        if (exhaustLight == null)
        {
            var exhaust = new GameObject("Boost Exhaust Light");
            exhaust.transform.SetParent(transform, false);
            exhaust.transform.localPosition = boostExhaustOffset;
            exhaust.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -.09f, -1f));

            exhaustLight = exhaust.AddComponent<Light>();
            exhaustLight.type = LightType.Spot;
            exhaustLight.color = new Color(.65f, .14f, 1f);
            exhaustLight.range = 7f;
            exhaustLight.spotAngle = 36f;
            exhaustLight.innerSpotAngle = 17f;
            exhaustLight.shadows = LightShadows.None;

            exhaustMaterial = new Material(Resources.Load<Shader>("BoostExhaust"));
            exhaustBeam = exhaust.AddComponent<LineRenderer>();
            exhaustBeam.sharedMaterial = exhaustMaterial;
            exhaustBeam.shadowCastingMode = ShadowCastingMode.Off;
            exhaustBeam.textureMode = LineTextureMode.Stretch;
            exhaustBeam.useWorldSpace = false;
            exhaustBeam.positionCount = 2;
            exhaustBeam.SetPosition(0, Vector3.zero);
            exhaustBeam.startWidth = .30f;
            exhaustBeam.endWidth = 3.1f;
        }

        // The central outlet stays bright as the purple cone flashes behind it.
        float wave = .5f + .5f * Mathf.Cos((Time.time - boostStarted) * 2.5f * 2f * Mathf.PI);
        float flash = wave * wave * wave;
        exhaustLight.intensity = Mathf.Lerp(.8f, 4f, flash);
        exhaustMaterial.SetFloat("_Pulse", flash);
        exhaustBeam.SetPosition(1, Vector3.forward * Mathf.Lerp(3.8f, 5.8f, flash));
        exhaustLight.gameObject.SetActive(true);
    }

    void OnDisable() => ClearBoost();

    void OnDestroy()
    {
        if (driver != null)
            driver.Respawned -= ClearBoost;
        if (exhaustLight != null)
            Destroy(exhaustLight.gameObject);
        Destroy(exhaustMaterial);
    }
}
