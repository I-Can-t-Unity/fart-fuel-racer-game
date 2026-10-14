using UnityEngine;

public class FinishLine : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var manager=RaceManager.Instance;
        if(manager!=null && manager.car!=null &&
           (other.transform==manager.car || other.transform.IsChildOf(manager.car)))
            manager.CrossFinish();
    }
}
