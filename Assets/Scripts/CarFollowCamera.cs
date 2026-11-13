using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CarFollowCamera : MonoBehaviour
{
    private const float SwapBlockShakeDuration = .36f;
    private const float SwapBlockShakeDistance = .28f;

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0, 3.5f, -7f);
    [SerializeField, Min(0)] private float followSpeed = 6f;

    private Camera viewCamera;
    private FartBoost targetBoost;
    private Volume boostVolume;
    private VolumeProfile boostProfile;
    private float boostFovOffset;
    private float swapBlockShakeEnd;
    private Vector3 swapBlockShakeOffset;

    public void ShakeForBlockedSwap()
    {
        if (isActiveAndEnabled)
            swapBlockShakeEnd = Time.time + SwapBlockShakeDuration;
    }

    public void SetTarget(Transform car)
    {
        target = car;
        targetBoost = car != null ? car.GetComponent<FartBoost>() : null;
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
        transform.position -= swapBlockShakeOffset;
        swapBlockShakeOffset = Vector3.zero;
        if (target != null)
        {
            Follow(1 - Mathf.Exp(-followSpeed * Time.deltaTime));
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

        if (boosting && boostVolume == null && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            CreateBoostVolume();
        if (boostVolume != null)
            boostVolume.weight = boostFovOffset / 3f;
    }

    private void CreateBoostVolume()
    {
        // Use per-object motion so the followed car stays sharper than the track.
        UniversalAdditionalCameraData cameraData = viewCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        var volumeObject = new GameObject("Boost Motion Blur");
        volumeObject.transform.SetParent(transform, false);
        int layer = 0;
        int mask = cameraData.volumeLayerMask.value;
        while (layer < 31 && mask != 0 && (mask & (1 << layer)) == 0)
            layer++;
        volumeObject.layer = layer;
        if (mask == 0)
            cameraData.volumeLayerMask = 1 << layer;

        boostProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        MotionBlur blur = boostProfile.Add<MotionBlur>();
        blur.mode.Override(MotionBlurMode.CameraAndObjects);
        blur.quality.Override(MotionBlurQuality.Medium);
        blur.intensity.Override(.85f);
        blur.clamp.Override(.085f);
        boostVolume = volumeObject.AddComponent<Volume>();
        boostVolume.isGlobal = true;
        boostVolume.priority = 100f;
        boostVolume.sharedProfile = boostProfile;
        boostVolume.weight = 0f;
    }

    private void OnDisable()
    {
        transform.position -= swapBlockShakeOffset;
        swapBlockShakeOffset = Vector3.zero;
        swapBlockShakeEnd = 0f;
        if (viewCamera != null && boostFovOffset != 0f)
            viewCamera.fieldOfView -= boostFovOffset;
        boostFovOffset = 0f;
        if (boostVolume != null)
            boostVolume.weight = 0f;
    }

    private void OnDestroy()
    {
        if (boostVolume != null)
            Destroy(boostVolume.gameObject);
        if (boostProfile != null)
        {
            foreach (VolumeComponent component in boostProfile.components)
                Destroy(component);
            Destroy(boostProfile);
        }
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
