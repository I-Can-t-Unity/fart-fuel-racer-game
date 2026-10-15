using UnityEngine;

public class FinishLine : MonoBehaviour
{
    // Checkpoints mark progress around a loop instead of ending the race.
    public bool isCheckpoint;

    void OnTriggerEnter(Collider other)
    {
        var manager=RaceManager.Instance;
        if(manager==null) return;

        bool player=IsPartOf(other.transform, manager.car);
        bool opponent=IsPartOf(other.transform, manager.opponent);
        if(!player && !opponent) return;

        if(isCheckpoint) manager.ReachCheckpoint(opponent);
        else manager.CrossFinish(opponent);
    }

    static bool IsPartOf(Transform t, Transform car)
    {
        return car!=null && (t==car || t.IsChildOf(car));
    }
}
