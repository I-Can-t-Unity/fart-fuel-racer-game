using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// Collects data for the "Fuel Pickup Collection Rate" metric: which fuel colours the
// player picks up, and how many rows of cubes they drive past with room in the bag.
//
//   Collection rate = rows where a cube was picked up / rows passed with room in the bag
//
// Events go to a Google Apps Script web app (see Analytics/README.md), so plays of
// the WebGL build on GitHub Pages are recorded too. The dashboard in docs/analytics
// reads them back. With no endpoint set, events go to a local CSV instead
// (<project>/Analytics/fuel_pickup_events.csv in the editor).
[RequireComponent(typeof(FuelInventory))]
public class FuelPickupAnalytics : MonoBehaviour
{
    [Tooltip("Web app URL of the Google Apps Script that stores events (see Analytics/README.md). Leave empty to write a local CSV instead.")]
    public string endpointUrl = "";

    public const string LocalFileName = "fuel_pickup_events.csv";

    static readonly string[] Columns =
    {
        "session_id", "timestamp_utc", "platform", "event", "race_time_s", "lane_spacing", "row",
        "fuel_type", "color", "lane", "suitable", "collected_in_row", "cubes_available", "bag_full",
        "race_result", "won",
    };

    FuelInventory inventory;
    FuelPickupSpawner spawner;
    RaceManager race;
    string sessionId;
    bool raceOver;
    readonly HashSet<int> rowsCollected = new HashSet<int>();

    public static string LocalFolder =>
#if UNITY_EDITOR
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Analytics");
#else
        Application.persistentDataPath;
#endif

    void Awake()
    {
        inventory = GetComponent<FuelInventory>();
        // The "r-" prefix keeps spreadsheets from reading the id as a date or number.
        sessionId = "r-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + UnityEngine.Random.Range(1000, 10000);
    }

    void Start()
    {
        spawner = FindFirstObjectByType<FuelPickupSpawner>();
        race = RaceManager.Instance;
        FuelPickup.PlayerTouched += OnPickupTouched;
        FuelRowGate.PlayerPassed += OnRowPassed;
        if (race != null)
            race.RaceEnded += OnRaceEnded;
        Record("race_start", new Dictionary<string, string>());
    }

    void OnDestroy()
    {
        // Leaving play mode in the middle of a race still closes the session.
        if (!raceOver)
            OnRaceEnded(false, "ABANDONED");
        FuelPickup.PlayerTouched -= OnPickupTouched;
        FuelRowGate.PlayerPassed -= OnRowPassed;
        if (race != null)
            race.RaceEnded -= OnRaceEnded;
    }

    void OnPickupTouched(FuelPickup pickup, bool collected)
    {
        if (raceOver)
            return;
        if (collected)
            rowsCollected.Add(pickup.row);
        Record(collected ? "pickup_collected" : "pickup_blocked", new Dictionary<string, string>
        {
            ["row"] = pickup.row.ToString(CultureInfo.InvariantCulture),
            ["lane"] = pickup.lane.ToString(CultureInfo.InvariantCulture),
            ["fuel_type"] = pickup.type.ToString(),
            ["color"] = ColorName(pickup.type),
            ["suitable"] = inventory.IsSuitable(pickup.type) ? "1" : "0",
        });
    }

    void OnRowPassed(FuelRowGate gate)
    {
        if (raceOver)
            return;
        bool collected = rowsCollected.Remove(gate.row);
        Record("row_passed", new Dictionary<string, string>
        {
            ["row"] = gate.row.ToString(CultureInfo.InvariantCulture),
            ["collected_in_row"] = collected ? "1" : "0",
            // Count the cube the player just took as available when they arrived.
            ["cubes_available"] = (gate.AvailableCount + (collected ? 1 : 0)).ToString(CultureInfo.InvariantCulture),
            // A full bag means no pickup was possible, so the row isn't a real opportunity.
            ["bag_full"] = !collected && inventory.IsFull ? "1" : "0",
        });
    }

    void OnRaceEnded(bool won, string message)
    {
        if (raceOver)
            return;
        raceOver = true;
        Record("race_end", new Dictionary<string, string>
        {
            ["race_result"] = Regex.Replace(message.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_'),
            ["won"] = won ? "1" : "0",
        });
    }

    // The colour players see: the car's own fuel is brown, Diesel green and Ethanol purple.
    static string ColorName(FuelType type)
    {
        switch (type)
        {
            case FuelType.Gasoline: return "brown";
            case FuelType.Diesel: return "green";
            case FuelType.Ethanol: return "purple";
            default: return type.ToString().ToLowerInvariant();
        }
    }

    void Record(string eventName, Dictionary<string, string> fields)
    {
        fields["session_id"] = sessionId;
        fields["timestamp_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        fields["platform"] = Application.platform.ToString();
        fields["event"] = eventName;
        fields["race_time_s"] = Time.timeSinceLevelLoad.ToString("0.00", CultureInfo.InvariantCulture);
        float spacing = spawner != null ? spawner.LaneSpacingUsed : 0;
        fields["lane_spacing"] = spacing.ToString("0.##", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(endpointUrl))
            AnalyticsUploader.Send(endpointUrl.Trim(), ToJson(fields));
        else if (Application.platform != RuntimePlatform.WebGLPlayer)
            AppendLocal(fields);
    }

    static string ToJson(Dictionary<string, string> fields)
    {
        var json = new StringBuilder("{");
        foreach (string column in Columns)
        {
            if (json.Length > 1)
                json.Append(',');
            fields.TryGetValue(column, out string value);
            json.Append('"').Append(column).Append("\":\"")
                .Append((value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }
        return json.Append('}').ToString();
    }

    static void AppendLocal(Dictionary<string, string> fields)
    {
        Directory.CreateDirectory(LocalFolder);
        string path = Path.Combine(LocalFolder, LocalFileName);
        if (!File.Exists(path))
            File.WriteAllText(path, string.Join(",", Columns) + "\n");
        var values = new string[Columns.Length];
        for (int i = 0; i < Columns.Length; i++)
            values[i] = fields.TryGetValue(Columns[i], out string value) ? value : "";
        File.AppendAllText(path, string.Join(",", values) + "\n");
    }
}
