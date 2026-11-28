using UnityEngine;

public class CarFollowCamera : MonoBehaviour
{
    private const float SwapBlockShakeDuration = .36f;
    private const float SwapBlockShakeDistance = .28f;

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0, 3.5f, -7f);
    [SerializeField, Min(0)] private float followSpeed = 6f;

    [Header("Impact shake")]
    [Tooltip("Camera shake (m) for the hardest hit. Small taps shake much less.")]
    [SerializeField, Min(0)] private float impactShakeDistance = .22f;
    [SerializeField, Min(.05f)] private float impactShakeSeconds = .3f;

    [Header("Turbo blur")]
    [SerializeField, Range(0f, 1f)] private float turboBlurIntensity = .85f;
    [SerializeField, Min(.01f)] private float turboBlurFadeSeconds = .2f;

    private Camera viewCamera;
    private FartBoost targetBoost;
    public float TurboBlurAmount { get; private set; }
    private float boostFovOffset;
    private float swapBlockShakeEnd;
    private Vector3 swapBlockShakeOffset;
    private CarDriver targetDriver;
    private float impactShakeStart = float.NegativeInfinity;
    private float impactShakeStrength;
    private Vector3 impactShakeDirection;
    private Vector3 impactShakeOffset;

    public void ShakeForBlockedSwap()
    {
        if (isActiveAndEnabled)
            swapBlockShakeEnd = Time.time + SwapBlockShakeDuration;
    }

    public void SetTarget(Transform car)
    {
        target = car;
        targetBoost = car != null ? car.GetComponent<FartBoost>() : null;
        // Only the car being followed shakes the camera.
        if (targetDriver != null) targetDriver.Impacted -= OnTargetImpact;
        targetDriver = car != null ? car.GetComponent<CarDriver>() : null;
        if (targetDriver != null) targetDriver.Impacted += OnTargetImpact;
    }

    private void OnTargetImpact(float strength, Vector3 pushDirection)
    {
        // A new hit only replaces the shake if it's stronger than what's left of the last one.
        float remaining = Mathf.Clamp01(1f - (Time.time - impactShakeStart) / impactShakeSeconds) * impactShakeStrength;
        if (strength < remaining)
            return;
        impactShakeStart = Time.time;
        impactShakeStrength = strength;
        impactShakeDirection = pushDirection;
    }

    private void Start()
    {
        viewCamera = GetComponent<Camera>();
        SetTarget(target);
        if (target != null)
            Follow(1);
    }

    private void LateUpdate()
    {
        // Remove the previous shake before smoothing so it can't affect the follow position.
        transform.position -= swapBlockShakeOffset + impactShakeOffset;
        swapBlockShakeOffset = Vector3.zero;
        impactShakeOffset = Vector3.zero;
        if (target != null)
        {
            Follow(1 - Mathf.Exp(-followSpeed * Time.deltaTime));
            float impactAge = (Time.time - impactShakeStart) / impactShakeSeconds;
            if (impactAge < 1f)
            {
                // Jolt in the push direction, then a quick damped wobble that dies away.
                float fade = (1f - impactAge) * (1f - impactAge);
                float swing = Mathf.Sin(impactAge * 3f * Mathf.PI);
                float side = Vector3.Dot(impactShakeDirection, transform.right);
                Vector3 shove = transform.right * side + transform.up * 0.35f;
                impactShakeOffset = shove * (swing * fade * impactShakeStrength * impactShakeDistance);
                transform.position += impactShakeOffset;
            }
            if (Time.time < swapBlockShakeEnd)
            {
                float remaining = (swapBlockShakeEnd - Time.time) / SwapBlockShakeDuration;
                float swing = Mathf.Sin((1f - remaining) * 6f * Mathf.PI);
                swapBlockShakeOffset = transform.right * (swing * remaining * SwapBlockShakeDistance);
                transform.position += swapBlockShakeOffset;
            }
        }
        if (viewCamera == null || viewCamera.orthographic)
            return;

        bool boosting = targetBoost != null && targetBoost.IsBoosting;
        float nextOffset = Mathf.MoveTowards(boostFovOffset, boosting ? 3f : 0f, 15f * Time.deltaTime);
        if (nextOffset != boostFovOffset)
        {
            viewCamera.fieldOfView += nextOffset - boostFovOffset;
            boostFovOffset = nextOffset;
        }

        TurboBlurAmount = Mathf.MoveTowards(TurboBlurAmount,
            boosting ? turboBlurIntensity : 0f,
            Time.deltaTime / Mathf.Max(.01f, turboBlurFadeSeconds));
    }

    private void OnDisable()
    {
        transform.position -= swapBlockShakeOffset + impactShakeOffset;
        swapBlockShakeOffset = Vector3.zero;
        impactShakeOffset = Vector3.zero;
        impactShakeStart = float.NegativeInfinity;
        swapBlockShakeEnd = 0f;
        if (viewCamera != null && boostFovOffset != 0f)
            viewCamera.fieldOfView -= boostFovOffset;
        boostFovOffset = 0f;
        TurboBlurAmount = 0f;
    }

    private void OnDestroy()
    {
        if (targetDriver != null) targetDriver.Impacted -= OnTargetImpact;
    }

    private void Follow(float blend)
    {
        // Follow yaw without rolling the camera when the car tips.
        Quaternion heading = Quaternion.Euler(0, target.eulerAngles.y, 0);
        Vector3 desiredPosition = target.position + heading * offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, blend);
        Vector3 lookAt = target.position + Vector3.up * 0.8f;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(lookAt - transform.position), blend);
    }
}
