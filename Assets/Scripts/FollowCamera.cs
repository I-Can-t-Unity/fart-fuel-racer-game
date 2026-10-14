using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0,6,-11);
    public float smoothSpeed=6f;
    void LateUpdate()
    {
        if(target==null) return;
        var desired=target.TransformPoint(offset);
        transform.position=Vector3.Lerp(transform.position,desired,Time.deltaTime*smoothSpeed);
        transform.LookAt(target.position+Vector3.up*1.5f);
    }
}
