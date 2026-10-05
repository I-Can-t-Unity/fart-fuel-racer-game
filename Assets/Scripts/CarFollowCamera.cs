using UnityEngine;

public class CarFollowCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0, 3.5f, -7f);
    [SerializeField, Min(0)] private float followSpeed = 6f;

    public void SetTarget(Transform car) => target = car;

    private void Start()
    {
        if (target != null)
            Follow(1);
    }

    private void LateUpdate()
    {
        if (target != null)
            Follow(1 - Mathf.Exp(-followSpeed * Time.deltaTime));
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
