using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Turns the OLD fuel game off in this scene only, without editing any of the team's
/// fuel scripts (so LoopRaceScene keeps working exactly as before).
///
///  • no fuel drain, tank pinned full, engine always running
///  • player's FuelInventory and the AI's AIFuelTank switched off (no Q mixes, no F dumps)
///  • fuel cans / fuel row gates removed from the track
///  • fuel gauge, fuel bag, rival-mix panel, jump-instructions panel and timer hidden
///  • race time limit removed (if RaceManager has one)
///
/// Put it on the SwapManager object (or any object) in SwitcherooScene.
/// </summary>
[DefaultExecutionOrder(-500)]   // runs before the fuel scripts copy RaceManager's numbers
public class SwitcherooMode : MonoBehaviour
{
    public bool disableFuelSystem = true;
    public bool removeTimeLimit = true;

    [Tooltip("HUD objects to hide. Leave empty to hide these by name: " +
             "FuelGaugeBackground, FuelRing, FuelText, TimerText, FuelBag, RivalMixPanel, JumpInstructionsPanel.")]
    public GameObject[] hideObjects;

    static readonly string[] defaultHide = {
        "FuelGaugeBackground", "FuelRing", "FuelText", "TimerText", "FuelBag", "RivalMixPanel", "JumpInstructionsPanel"
    };
    static readonly string[] timeLimitNames = {
        "raceduration", "timelimit", "raceseconds", "racelength", "timelimitseconds",
        "racetimelimit", "racedurationseconds", "matchtime", "countdowntime", "racelimit", "maxracetime"
    };

    readonly List<GameObject> hidden = new List<GameObject>();
    readonly List<CarDriver> drivers = new List<CarDriver>();

    void Awake()
    {
        var race = FindFirstObjectByType<RaceManager>();
        if (race != null)
        {
            if (disableFuelSystem)
            {
                SetFloat(race, "fuel", 100f);
                SetFloat(race, "fuelDrainPerSecond", 0f);
                SetFloat(race, "fuelDrainPerSpeed", 0f);
                SetFloat(race, "fuelDrainPerThrottle", 0f);
            }
            if (removeTimeLimit) RemoveTimeLimit(race);
        }
    }

    IEnumerator Start()
    {
        // Wait a frame: AICarWaypointDriver adds the AI's fuel tank in its own Start.
        yield return null;

        if (disableFuelSystem)
        {
            foreach (var t in FindObjectsByType<AIFuelTank>(FindObjectsSortMode.None))
            {
                t.fuel = 100f;
                t.drainPerSecond = t.drainPerSpeed = t.drainPerThrottle = 0f;
                t.enabled = false;
            }
            foreach (var inv in FindObjectsByType<FuelInventory>(FindObjectsSortMode.None))
            {
                SetFloat(inv, "fuel", 100f);
                inv.enabled = false;
            }
            foreach (var p in FindObjectsByType<FuelPickup>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
            foreach (var g in FindObjectsByType<FuelRowGate>(FindObjectsSortMode.None)) g.gameObject.SetActive(false);
            var cans = GameObject.Find("FuelPickups");
            if (cans) cans.SetActive(false);
            drivers.AddRange(FindObjectsByType<CarDriver>(FindObjectsSortMode.None));
        }

        if (hideObjects != null && hideObjects.Length > 0) hidden.AddRange(hideObjects);
        else
            foreach (var name in defaultHide)
            {
                var go = FindIncludingInactive(name);
                if (go) hidden.Add(go);
            }
        Debug.Log($"[Switcheroo] Fuel system off: {disableFuelSystem}. Hiding {hidden.Count} HUD object(s).", this);
    }

    void LateUpdate()
    {
        // Keep everything off even if another script turns it back on.
        foreach (var go in hidden) if (go && go.activeSelf) go.SetActive(false);
        foreach (var d in drivers)
        {
            if (!d) continue;
            d.EngineRunning = true;
            d.FuelFraction = 1f;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    void RemoveTimeLimit(RaceManager race)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var f in race.GetType().GetFields(F))
        {
            string n = f.Name.ToLowerInvariant().Trim('_');
            if (System.Array.IndexOf(timeLimitNames, n) < 0) continue;
            if (f.FieldType == typeof(float)) { f.SetValue(race, 99999f); Debug.Log($"[Switcheroo] Time limit removed: RaceManager.{f.Name}", this); }
            else if (f.FieldType == typeof(int)) { f.SetValue(race, 99999); Debug.Log($"[Switcheroo] Time limit removed: RaceManager.{f.Name}", this); }
        }
    }

    static void SetFloat(object target, string name, float value)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var f = target.GetType().GetField(name, F);
        if (f != null && f.FieldType == typeof(float)) { f.SetValue(target, value); return; }
        var p = target.GetType().GetProperty(name, F);
        if (p != null && p.CanWrite && p.PropertyType == typeof(float)) p.SetValue(target, value);
    }

    static GameObject FindIncludingInactive(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }
}
