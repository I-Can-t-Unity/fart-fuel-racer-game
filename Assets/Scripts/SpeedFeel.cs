using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes speed FEEL fast. Put it on the Main Camera and tick the effects you want.
/// It always follows the car you're driving (works after a SWITCHEROO too).
///
///   1 FOV stretch        camera widens as you speed up
///   2 Boost pull-back    camera drops back a little during a boost/turbo
///   3 Top-speed shake    gentle wobble near top speed
///   4 Speed lines        your Particle System streaks, more of them the faster you go
///   6 Turbo flash        fades in your "BoostVolume" (colour fringing etc.) during a boost
///   8 Wheel dust         your dust Particle System follows the car, more dust at speed
///   9 Speedo colour      speed number turns your car's colour and pulses at top speed
/// </summary>
[DefaultExecutionOrder(1000)]   // runs after the follow camera has moved
public class SpeedFeel : MonoBehaviour
{
    [Header("General")]
    [Tooltip("Your cars' top speed. Effects are strongest here.")]
    public float topSpeedKmh = 120f;
    [Tooltip("Speed gain (km/h per second) that counts as a boost/turbo kicking in.")]
    public float boostDetectKmhPerSec = 45f;

    [Header("1 · FOV stretch")]
    public bool fovStretch = true;
    [Tooltip("How much wider the view gets at top speed (degrees).")]
    public float extraFov = 15f;

    [Header("2 · Boost pull-back")]
    public bool boostPullBack = true;
    public float pullBackMetres = 1.5f;

    [Header("3 · Top-speed shake")]
    public bool topSpeedShake = true;
    [Tooltip("Shake starts at this share of top speed (0.85 = 85%).")]
    [Range(0f, 1f)] public float shakeFrom = .85f;
    public float shakeAmount = .06f;

    [Header("4 · Speed lines")]
    public bool speedLines = true;
    [Tooltip("The SpeedLines Particle System (child of this camera).")]
    public ParticleSystem speedLinesParticles;
    [Tooltip("Lines start appearing at this share of top speed.")]
    [Range(0f, 1f)] public float linesFrom = .45f;
    public float maxLinesPerSecond = 80f;

    [Header("6 · Turbo flash")]
    public bool turboFlash = true;
    [Tooltip("A Global Volume (weight 0) with Chromatic Aberration etc. It is faded in during boosts.")]
    public Volume boostVolume;

    [Header("8 · Wheel dust")]
    public bool wheelDust = true;
    [Tooltip("The WheelDust Particle System (anywhere in the scene, Simulation Space = World).")]
    public ParticleSystem dustParticles;
    public float maxDustPerSecond = 40f;

    [Header("9 · Speedo colour")]
    public bool speedoColour = true;
    [Tooltip("The speed NUMBER text on your speed gauge.")]
    public TMP_Text speedNumber;

    Camera cam;
    float baseFov, shownFov;
    Vector3 lastOffset, pull;
    float lastKmh, boostLeft, flash;
    Vector3 speedoScale = Vector3.one;
    FuelInventory inventory;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogWarning("SpeedFeel needs a Camera on the same GameObject; disabling it to avoid moving scenery.", this);
            enabled = false;
            return;
        }
        if (cam != null) baseFov = shownFov = cam.fieldOfView;
        if (speedNumber != null) speedoScale = speedNumber.rectTransform.localScale;
        if (boostVolume != null) boostVolume.weight = 0f;
        inventory = FindFirstObjectByType<FuelInventory>();
    }

    // Take back last frame's nudge before the follow camera moves, so nudges never build up.
    void Update()
    {
        transform.position -= lastOffset;
        lastOffset = Vector3.zero;
    }

    void LateUpdate()
    {
        var race = RaceManager.Instance;
        Transform car = race != null ? race.CurrentPlayerCar : null;
        Rigidbody rb = car != null ? car.GetComponent<Rigidbody>() : null;
        float kmh = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
        float t = Mathf.Clamp01(kmh / Mathf.Max(1f, topSpeedKmh));   // 0 = stopped, 1 = top speed
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Boost = a sudden speed gain.
        float gain = (kmh - lastKmh) / dt;
        lastKmh = kmh;
        if (gain > boostDetectKmhPerSec) boostLeft = .6f;
        boostLeft -= dt;
        bool boosting = boostLeft > 0f;

        // 1 · FOV
        if (cam != null)
        {
            float target = baseFov + (fovStretch ? extraFov * t * t : 0f) + (boosting && fovStretch ? 4f : 0f);
            shownFov = Mathf.Lerp(shownFov, target, 1f - Mathf.Exp(-4f * dt));
            cam.fieldOfView = shownFov;
        }

        Vector3 offset = Vector3.zero;

        // 2 · Pull-back
        Vector3 pullTarget = boostPullBack && boosting ? -transform.forward * pullBackMetres : Vector3.zero;
        pull = Vector3.Lerp(pull, pullTarget, 1f - Mathf.Exp(-5f * dt));
        offset += pull;

        // 3 · Shake
        if (topSpeedShake && t > shakeFrom)
        {
            float k = (t - shakeFrom) / Mathf.Max(.01f, 1f - shakeFrom) * shakeAmount;
            float s = Time.time * 25f;
            offset += transform.right * ((Mathf.PerlinNoise(s, 0f) - .5f) * 2f * k)
                    + transform.up * ((Mathf.PerlinNoise(0f, s) - .5f) * 2f * k);
        }

        transform.position += offset;
        lastOffset = offset;

        // 4 · Speed lines
        if (speedLinesParticles != null)
        {
            var em = speedLinesParticles.emission;
            float k = speedLines ? Mathf.Clamp01((t - linesFrom) / Mathf.Max(.01f, 1f - linesFrom)) : 0f;
            if (boosting && speedLines) k = Mathf.Max(k, .8f);
            em.rateOverTime = k * maxLinesPerSecond;
        }

        // 6 · Turbo flash
        if (boostVolume != null)
        {
            flash = Mathf.Lerp(flash, turboFlash && boosting ? 1f : 0f, 1f - Mathf.Exp(-8f * dt));
            boostVolume.weight = flash;
        }

        // 8 · Dust follows the car you're driving
        if (dustParticles != null)
        {
            var em = dustParticles.emission;
            if (wheelDust && car != null)
            {
                dustParticles.transform.position = car.position - car.forward * 2f + Vector3.up * .3f;
                em.rateOverTime = Mathf.Clamp01((t - .2f) / .8f) * maxDustPerSecond;
            }
            else em.rateOverTime = 0f;
        }

        // 9 · Speedo colour
        if (speedNumber != null)
        {
            Color carColour = inventory != null ? inventory.ColorOf(inventory.carFuel) : Color.white;
            carColour = Color.Lerp(carColour, Color.white, .25f);   // a bit lighter so dark blue reads
            speedNumber.color = speedoColour ? Color.Lerp(Color.white, carColour, Mathf.Clamp01((t - .3f) / .5f)) : Color.white;
            float pulse = speedoColour && t > .9f ? 1f + .08f * Mathf.Sin(Time.time * 14f) : 1f;
            speedNumber.rectTransform.localScale = speedoScale * pulse;
        }
    }

    void OnDisable()
    {
        transform.position -= lastOffset;
        lastOffset = Vector3.zero;
        if (cam != null) cam.fieldOfView = baseFov;
        if (boostVolume != null) boostVolume.weight = 0f;
    }
}
