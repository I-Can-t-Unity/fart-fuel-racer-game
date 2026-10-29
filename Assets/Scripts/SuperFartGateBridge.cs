using UnityEngine;

/// <summary>
/// Connects the team's SuperFartJumpGate (which spends the mix) to the shortcut
/// system (assisted launch, fail zone, race progress). Sits on the same object
/// as the SuperFartJumpGate at the start of the ramp. Created by BrokenBridgeBuilder.
/// </summary>
[RequireComponent(typeof(SuperFartJumpGate))]
public class SuperFartGateBridge : MonoBehaviour
{
    public ShortcutBranch branch;
    SuperFartJumpGate gate;

    void Awake() { gate = GetComponent<SuperFartJumpGate>(); }

    void OnEnable()
    {
        SuperFartJumpGate.Activated += OnActivated;
        SuperFartJumpGate.Denied += OnDenied;
    }

    void OnDisable()
    {
        SuperFartJumpGate.Activated -= OnActivated;
        SuperFartJumpGate.Denied -= OnDenied;
    }

    // Paid: the launch pad on the lip will give this car the calculated jump.
    void OnActivated(SuperFartJumpGate source, Rigidbody car)
    {
        if (source != gate || car == null || branch == null) return;
        ShortcutTracker.Enter(car, branch, true);
    }

    // Not paid: still on the shortcut (so the fail zone catches it), but no assisted jump.
    void OnDenied(SuperFartJumpGate source, Rigidbody car)
    {
        if (source != gate || car == null || branch == null) return;
        if (!ShortcutTracker.IsOnBranch(car)) ShortcutTracker.Enter(car, branch, false);
    }
}
