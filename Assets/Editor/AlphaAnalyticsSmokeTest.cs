using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the authored Alpha scene with local CSV output, never the live endpoint.
[InitializeOnLoad]
public static class AlphaAnalyticsSmokeTest
{
    const string Running = "AlphaAnalyticsSmokeTest.Running";
    const string PreviousScene = "AlphaAnalyticsSmokeTest.PreviousScene";
    public const string ReportPath = "/tmp/alpha-analytics-smoke-test.txt";
    static AlphaAnalyticsSmokeTest() { EditorApplication.playModeStateChanged += StateChanged; }

    [MenuItem("Tools/Analytics/Test Alpha Tracking")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before testing.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save scene changes before running the analytics test.");
        SessionState.SetString(PreviousScene, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        EditorSceneManager.OpenScene("Assets/Scenes/AlphaRaceScene.unity");
        var analytics = UnityEngine.Object.FindFirstObjectByType<FuelPickupAnalytics>();
        Require(analytics != null, "Alpha player has analytics");
        analytics.endpointUrl = "";
        SessionState.SetBool(Running, true);
        EditorApplication.isPlaying = true;
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Running, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.update += Tick;
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Running, false);
            string previous = SessionState.GetString(PreviousScene, "");
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        }
    }

    static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || RaceManager.Instance == null || !RaceManager.Instance.IsRacing) return;
        EditorApplication.update -= Tick;
        try
        {
            var analytics = UnityEngine.Object.FindFirstObjectByType<FuelPickupAnalytics>();
            var inventory = analytics.GetComponent<FuelInventory>();
            var spawner = UnityEngine.Object.FindFirstObjectByType<FuelPickupSpawner>();
            Require(spawner.authoredRows.Length == 2, "Two authored rows configured");
            Require(spawner.LaneSpacingUsed == 3.5f || spawner.LaneSpacingUsed == 4.5f, "A/B spacing selected");
            var gates = UnityEngine.Object.FindObjectsByType<FuelRowGate>(FindObjectsSortMode.None);
            Require(gates.Length == 4, "Approach and exit gates generated for both rows");
            foreach (var pickup in spawner.authoredRows.SelectMany(r => r.pickups))
                for (Transform node = pickup.transform; node != null; node = node.parent) node.gameObject.SetActive(true);
            var collider = inventory.GetComponent<Collider>();
            var body = inventory.GetComponent<Rigidbody>();
            body.linearVelocity = inventory.transform.forward * 10;
            FuelRowGate Gate(int row, bool approach) => gates.Single(g => g.row == row && g.capturesApproach == approach);
            void Cross(FuelRowGate gate) => Call(gate, "OnTriggerEnter", collider);
            string path = Path.Combine(FuelPickupAnalytics.LocalFolder, FuelPickupAnalytics.LocalFileName);
            string[] before = File.ReadAllLines(path);
            string session = before.Last().Split(',')[0];
            Cross(Gate(0, true));
            // Two real pickup callbacks fill the bag and automatically create a mix.
            Call(spawner.authoredRows[0].pickups[0], "OnTriggerEnter", collider);
            Call(spawner.authoredRows[0].pickups[1], "OnTriggerEnter", collider);
            Require(inventory.IsFull, "Two pickups create a held mix");
            Cross(Gate(0, false));
            Cross(Gate(0, false)); // Additional car collider / repeat must not count twice.
            Cross(Gate(1, true));
            Call(spawner.authoredRows[1].pickups[0], "OnTriggerEnter", collider);
            Call(inventory, "UseMix");
            Require(!inventory.IsFull, "Using mix frees the bag");
            Cross(Gate(1, false)); // Must retain the full-bag snapshot from approach.
            Call(inventory, "DumpFuel");
            analytics.EndSession(); analytics.EndSession();
            string[] lines = File.ReadAllLines(path).Skip(1).Where(l => l.StartsWith(session + ",")).ToArray();
            string[] header = File.ReadAllLines(path)[0].Split(',');
            string Field(string line, string key) => line.Split(',')[Array.IndexOf(header, key)];
            int Count(string name) => lines.Count(l => Field(l, "event") == name);
            Require(Count("race_start") == 1 && Count("race_end") == 1, "Exactly one start/end");
            Require(Count("pickup_collected") == 2 && Count("pickup_blocked") == 1, "Pickup and blocked counts");
            Require(Count("mix_created") == 1 && Count("mix_used") == 1 && Count("fuel_dumped") == 1, "Revised mechanic events");
            var rows = lines.Where(l => Field(l, "event") == "row_passed").ToArray();
            Require(rows.Length == 2, "Row callback deduplication");
            Require(Field(rows[0], "bag_full") == "0" && Field(rows[0], "collected_in_row") == "1", "Pickup filling bag is eligible");
            Require(Field(rows[1], "bag_full") == "1" && Field(rows[1], "collected_in_row") == "0", "Full approach stays excluded after mix use");
            Require(lines.All(l => Field(l, "build_id") == FuelPickupAnalytics.BuildId && Field(l, "scene") == "AlphaRaceScene"), "Build and scene attribution");
            Require(lines.Select(l => Field(l, "event_id")).Distinct().Count() == lines.Length, "Unique event IDs");
            File.WriteAllText(ReportPath, "PASS: Alpha scene wiring, A/B placement, pickup callbacks, row deduplication, bag snapshots, revised mechanics, session closure, attribution, event IDs.\n");
            Debug.Log(File.ReadAllText(ReportPath));
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "FAIL: " + error + "\n");
            Debug.LogException(error);
        }
        finally { EditorApplication.isPlaying = false; }
    }
}
