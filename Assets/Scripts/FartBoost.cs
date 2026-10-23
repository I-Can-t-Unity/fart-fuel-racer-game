using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Fuel side effects on the car. A fart pushes it forward past its normal top
// speed; a choke drags it down to a crawl. Both leave a trail of puffs.
// Runs after CarDriver so it can override the wheel torques CarDriver sets.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class FartBoost : MonoBehaviour
{
    public float acceleration = 12f;
    public float maxBoostKph = 100f;
    public float chokeMaxKph = 25f;

    [Header("Puffs")]
    public Vector3 exhaustOffset = new Vector3(0, .5f, -2.4f);
    public float puffLifetime = .8f;

    struct Puff
    {
        public Transform transform;
        public float born, size;
    }

    Rigidbody body;
    CarDriver driver;
    WheelCollider[] wheels;
    float fartUntil, chokeUntil, nextPuff;
    Material fartMaterial, chokeMaterial;
    readonly List<Puff> puffs = new List<Puff>();

    public bool IsFarting => Time.time < fartUntil;
    public bool IsChoking => Time.time < chokeUntil;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        driver = GetComponent<CarDriver>();
        wheels = GetComponentsInChildren<WheelCollider>();
        fartMaterial = MakeMaterial(new Color(.55f, .7f, .2f));
        chokeMaterial = MakeMaterial(new Color(.15f, .15f, .15f));
    }

    // Farts stack, and a fart clears a choked engine (and the other way round).
    public void Fart(float seconds)
    {
        if (driver != null && driver.IsStopped)
            return;
        fartUntil = Mathf.Max(fartUntil, Time.time) + seconds;
        chokeUntil = 0;
    }

    public void Choke(float seconds)
    {
        if (driver != null && driver.IsStopped)
            return;
        chokeUntil = Mathf.Max(chokeUntil, Time.time) + seconds;
        fartUntil = 0;
    }

    void FixedUpdate()
    {
        if (driver != null && driver.IsStopped)
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
            if (Vector3.Dot(velocity, forward) < maxBoostKph / 3.6f)
                body.AddForce(forward * acceleration, ForceMode.Acceleration);
        }
        else if (IsChoking && velocity.magnitude > chokeMaxKph / 3.6f)
            body.AddForce(-velocity.normalized * acceleration, ForceMode.Acceleration);
    }

    void Update()
    {
        if ((driver == null || !driver.IsStopped) && (IsFarting || IsChoking) && Time.time >= nextPuff)
        {
            nextPuff = Time.time + (IsFarting ? .04f : .12f);
            SpawnPuff(IsFarting ? fartMaterial : chokeMaterial, IsFarting ? 1.4f : .8f);
        }

        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            Puff puff = puffs[i];
            float t = (Time.time - puff.born) / puffLifetime;
            if (t >= 1)
            {
                Destroy(puff.transform.gameObject);
                puffs.RemoveAt(i);
                continue;
            }
            // Swell quickly, then shrink away while drifting up.
            puff.transform.localScale = Vector3.one * puff.size * Mathf.Sin(Mathf.PI * Mathf.Sqrt(t));
            puff.transform.position += Vector3.up * .8f * Time.deltaTime;
        }
    }

    void SpawnPuff(Material material, float size)
    {
        GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        puff.name = "FartPuff";
        Collider puffCollider = puff.GetComponent<Collider>();
        puffCollider.enabled = false;
        Destroy(puffCollider);

        Renderer puffRenderer = puff.GetComponent<Renderer>();
        puffRenderer.sharedMaterial = material;
        puffRenderer.shadowCastingMode = ShadowCastingMode.Off;

        puff.transform.position = transform.TransformPoint(exhaustOffset) + Random.insideUnitSphere * .25f;
        puff.transform.localScale = Vector3.zero;
        puffs.Add(new Puff { transform = puff.transform, born = Time.time, size = size * Random.Range(.8f, 1.2f) });
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { color = color };
    }
}
