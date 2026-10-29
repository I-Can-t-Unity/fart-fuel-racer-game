using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Shared types for the shortcut system.
// If your team already has a fuel-mix enum, delete this one and either name
// yours FuelMixType or find/replace FuelMixType in these scripts.
// ─────────────────────────────────────────────────────────────────────────────
public enum FuelMixType { None, SuperFart, Hybrid, Premium }

/// <summary>Implemented by the fuel/inventory system (player AND AI cars).</summary>
public interface IFuelMixHolder
{
    FuelMixType HeldMix { get; }
    /// <summary>Remove the held mix if it matches. Return true if it was spent.</summary>
    bool TryConsumeMix(FuelMixType mix);
}

/// <summary>Optional: implemented by the off-track recovery system so shortcut
/// failures use the same respawn logic (fade, invulnerability, etc.).</summary>
public interface ICarRespawnHandler
{
    void RespawnAt(Vector3 position, Quaternion rotation);
}

/// <summary>Works on Unity 2021/2022 (velocity) and Unity 6 (linearVelocity).</summary>
public static class RbCompat
{
    public static Vector3 GetVelocity(Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    public static void SetVelocity(Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    public static void SetAngularVelocity(Rigidbody rb, Vector3 w)
    {
        rb.angularVelocity = w;
    }
}

/// <summary>
/// Global record of which car is currently driving on which shortcut, and
/// whether it paid the fuel mix (and so gets the assisted launch).
/// Waypoint tracking / off-track recovery / wrong-way checks should query this.
/// </summary>
public static class ShortcutTracker
{
    static readonly Dictionary<Rigidbody, ShortcutBranch> onBranch = new Dictionary<Rigidbody, ShortcutBranch>();
    static readonly HashSet<Rigidbody> boosted = new HashSet<Rigidbody>();

    // Clears statics when "Enter Play Mode Options" skips domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        onBranch.Clear();
        boosted.Clear();
        Failed = null;
        Completed = null;
    }

    public static void Enter(Rigidbody rb, ShortcutBranch branch, bool paidMix)
    {
        if (rb == null || branch == null) return;
        onBranch[rb] = branch;
        if (paidMix) boosted.Add(rb); else boosted.Remove(rb);
    }

    public static void Exit(Rigidbody rb)
    {
        if (rb == null) return;
        onBranch.Remove(rb);
        boosted.Remove(rb);
    }

    public static bool IsOnBranch(Rigidbody rb) => rb != null && onBranch.ContainsKey(rb);

    public static bool TryGetBranch(Rigidbody rb, out ShortcutBranch branch)
    {
        branch = null;
        return rb != null && onBranch.TryGetValue(rb, out branch);
    }

    /// <summary>Raised when a car falls off / misses the jump and is respawned.</summary>
    public static event System.Action<Rigidbody, ShortcutBranch> Failed;
    /// <summary>Raised when a car passes the branch's exit gate.</summary>
    public static event System.Action<Rigidbody, ShortcutBranch> Completed;

    public static void RaiseFailed(Rigidbody rb, ShortcutBranch b) { Exit(rb); Failed?.Invoke(rb, b); }
    public static void RaiseCompleted(Rigidbody rb, ShortcutBranch b) { Exit(rb); Completed?.Invoke(rb, b); }

    public static bool HasBoost(Rigidbody rb) => rb != null && boosted.Contains(rb);

    public static void ConsumeBoost(Rigidbody rb)
    {
        if (rb != null) boosted.Remove(rb);
    }

    /// <summary>Finds a component/interface anywhere on the car that owns this rigidbody.</summary>
    public static T FindOnCar<T>(Rigidbody rb) where T : class
    {
        if (rb == null) return null;
        T found = rb.GetComponentInChildren<T>();
        if (IsMissing(found)) found = rb.GetComponentInParent<T>();
        return IsMissing(found) ? null : found;
    }

    static bool IsMissing<T>(T t) where T : class
    {
        if (t is Object uo) return uo == null; // handles Unity "fake null"
        return t == null;
    }
}
