using UnityEngine;

public class FinishLine : MonoBehaviour
{
    public bool isCheckpoint;

    void OnTriggerEnter(Collider other)
    {
        RaceManager manager = RaceManager.Instance;
        if (manager == null) return;
        if (manager.colorSwitchingMode)
        {
            CarDriver racer = manager.ResolveRacer(other.transform);
            if (racer == null) return;
            if (isCheckpoint) manager.ReachCheckpoint(racer.transform);
            else manager.CrossFinish(racer.transform);
            return;
        }
        bool player = IsPartOf(other.transform, manager.car);
        bool opponent = IsPartOf(other.transform, manager.opponent);
        if (!player && !opponent) return;
        if (isCheckpoint) manager.ReachCheckpoint(opponent);
        else manager.CrossFinish(opponent);
    }

    static bool IsPartOf(Transform candidate, Transform car)
        => car != null && (candidate == car || candidate.IsChildOf(car));
}
