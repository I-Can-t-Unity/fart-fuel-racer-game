using System;
using System.Collections.Generic;
using UnityEngine;

// The activation zone at the entrance of the Super Fart jump shortcut. A car that
// drives in holding Super Fart spends it here and gets launched; a car without it
// spends nothing and gets nothing, so it has to take the main route. The player's
// bag and the AI's tank follow the same rule.
// The gate only spends the mix; the launch itself listens to Activated. It only ever
// acts on the car that drove in, so one racer using the jump never opens it for the other.
// Put it on an object with a box collider covering the shortcut lane; the box is made a trigger.
[RequireComponent(typeof(BoxCollider))]
public class SuperFartJumpGate : MonoBehaviour
{
    [Tooltip("The mix a car must hold to use this shortcut.")]
    public FuelMix requiredMix = FuelMix.SuperFart;
    [Tooltip("After a car is let through or turned away, it is ignored for this long, in seconds. A car has several colliders and each one enters the zone, so without this one pass would count several times.")]
    public float retriggerSeconds = 2f;

    // A car spent its mix at this gate and should be launched now.
    public static event Action<SuperFartJumpGate, Rigidbody> Activated;
    // A car drove in without the mix; nothing was spent and nothing happens (for HUD feedback).
    public static event Action<SuperFartJumpGate, Rigidbody> Denied;

    readonly Dictionary<Rigidbody, float> ignoreUntil = new Dictionary<Rigidbody, float>();

    void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return;
        // Every collider of a car (body, wheels) shares the car's rigidbody, which is
        // also where its bag or tank sits.
        Rigidbody car = other.attachedRigidbody;
        if (car == null)
            return;
        if (ignoreUntil.TryGetValue(car, out float until) && Time.time < until)
            return;

        FuelInventory player = car.GetComponent<FuelInventory>();
        AIFuelTank rival = player == null ? car.GetComponent<AIFuelTank>() : null;
        if (player == null && rival == null)
            return;
        ignoreUntil[car] = Time.time + retriggerSeconds;

        // Checking and spending is one step, so pressing Q in the same frame can't use the mix twice.
        bool spent = player != null ? player.TryConsumeMix(requiredMix) : rival.TryConsumeMix(requiredMix);
        if (spent)
        {
            if (player != null)
                player.Announce("SUPER FART JUMP!");
            Activated?.Invoke(this, car);
        }
        else
        {
            if (player != null)
                player.Announce($"JUMP NEEDS {player.NameOf(requiredMix)}");
            Denied?.Invoke(this, car);
        }
    }
}
