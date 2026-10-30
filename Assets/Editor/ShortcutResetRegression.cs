using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in a disposable project copy with -batchmode -nographics
// -executeMethod ShortcutResetRegression.RunBatch. Does not save scenes/assets.
public static class ShortcutResetRegression
{
    static readonly StringBuilder Report = new StringBuilder();
    static bool passed;

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run in a disposable batch-mode project copy.");
        Report.Clear();
        passed = true;
        SuperFartGateBridge bridge = null;
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LoopRaceScene.unity");
            var bag = UnityEngine.Object.FindFirstObjectByType<FuelInventory>();
            var driver = bag.GetComponent<CarDriver>();
            var body = bag.GetComponent<Rigidbody>();
            var collider = bag.GetComponent<BoxCollider>();
            var rival = UnityEngine.Object.FindFirstObjectByType<AICarWaypointDriver>().GetComponent<Rigidbody>();
            bridge = UnityEngine.Object.FindFirstObjectByType<SuperFartGateBridge>();
            var gate = bridge.GetComponent<SuperFartJumpGate>();
            var pad = UnityEngine.Object.FindFirstObjectByType<ShortcutLaunchPad>();
            Invoke(driver, "Awake");
            Invoke(bag.GetComponent<FartBoost>(), "Awake");
            Invoke(bag, "Awake");
            Invoke(gate, "Awake");
            Invoke(bridge, "Awake");
            Invoke(bridge, "OnEnable");
            gate.retriggerSeconds = 0;

            foreach (FuelType own in Enum.GetValues(typeof(FuelType)))
            {
                bag.carFuel = own;
                CollectSuperFart(bag);
                Invoke(gate, "OnTriggerEnter", collider);
                Check(bag.HeldMix == FuelMix.None && ShortcutTracker.HasBoost(body), own + ": gate spends mix and grants launch");
                ShortcutTracker.Enter(rival, bridge.branch, true);

                bool cleanAtRespawn = false;
                Action onRespawn = () => cleanAtRespawn = !ShortcutTracker.IsOnBranch(body) && !ShortcutTracker.HasBoost(body);
                driver.Respawned += onRespawn;
                typeof(CarDriver).GetField("resetRequested", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(driver, true);
                Invoke(driver, "FixedUpdate");
                driver.Respawned -= onRespawn;
                Check(cleanAtRespawn, own + ": R clears branch and launch before notifying listeners");
                Check(ShortcutTracker.HasBoost(rival), own + ": resetting player preserves rival permission");

                Invoke(gate, "OnTriggerEnter", collider);
                Check(ShortcutTracker.IsOnBranch(body) && !ShortcutTracker.HasBoost(body), own + ": empty bag is denied after reset");
                Check(!Launch(pad, body, collider), own + ": denied car receives no assisted launch");

                // A denial must also revoke stale payment from any earlier entry.
                ShortcutTracker.Enter(body, bridge.branch, true);
                Invoke(gate, "OnTriggerEnter", collider);
                Check(!ShortcutTracker.HasBoost(body), own + ": denial revokes stale permission");

                CollectSuperFart(bag);
                Invoke(gate, "OnTriggerEnter", collider);
                Check(Launch(pad, body, collider) && !ShortcutTracker.HasBoost(body), own + ": fresh mix enables exactly one launch");
                Check(!Launch(pad, body, collider), own + ": spent launch cannot be reused");
                ShortcutTracker.Exit(body);
                ShortcutTracker.Exit(rival);
            }
            Invoke(bridge, "OnDisable");
            bridge = null;

            EditorSceneManager.OpenScene("Assets/Scenes/MainMenuCanvas.unity");
            var rules = GameObject.Find("InstructionsFuelRules");
            if (rules == null)
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (text.name == "InstructionsFuelRules") { rules = text.gameObject; break; }
            var instructions = rules.GetComponent<TMP_Text>();
            Check(instructions.text.Contains("2 PURPLE (Ethanol)") && instructions.text.Contains("2 BROWN (Gasoline)")
                && instructions.text.Contains("2 GREEN (Diesel)") && instructions.text.Contains("3 sec choke"), "Menu describes all three recipes and other wrong-fuel pairs");
            instructions.ForceMeshUpdate(true);
            Check(instructions.preferredHeight <= instructions.rectTransform.rect.height, "Updated instructions fit the text panel");
        }
        catch (Exception error) { passed = false; Report.AppendLine(error.ToString()); }
        finally
        {
            if (bridge != null) Invoke(bridge, "OnDisable");
            File.WriteAllText("Library/shortcut-reset-regression.txt", Report.ToString());
            Debug.Log(Report.ToString());
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    static void CollectSuperFart(FuelInventory bag)
    {
        Check(bag.TryCollect(bag.SuperFartFuel) && bag.TryCollect(bag.SuperFartFuel)
            && bag.HeldMix == FuelMix.SuperFart, bag.carFuel + ": craft fresh Super Fart");
    }

    static bool Launch(ShortcutLaunchPad pad, Rigidbody body, Collider collider)
    {
        ((Dictionary<Rigidbody, float>)typeof(ShortcutLaunchPad).GetField("cooldown", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pad)).Clear();
        body.position = pad.transform.position;
        body.linearVelocity = Vector3.ProjectOnPlane(pad.transform.forward, Vector3.up).normalized * 12;
        bool assisted = false;
        Action<Rigidbody, bool> onLaunch = (car, paid) => assisted = car == body && paid;
        pad.Launched += onLaunch;
        Invoke(pad, "OnTriggerEnter", collider);
        pad.Launched -= onLaunch;
        return assisted;
    }

    static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    static void Check(bool condition, string message)
    {
        passed &= condition;
        Report.AppendLine((condition ? "PASS " : "FAIL ") + message);
    }
}
