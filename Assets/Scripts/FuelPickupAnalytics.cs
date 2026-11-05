using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

[RequireComponent(typeof(FuelInventory))]
public class FuelPickupAnalytics : MonoBehaviour
{
    [Tooltip("Web app URL of the Google Apps Script that stores events (see Analytics/README.md). Leave empty to write a local CSV instead.")]
    public string endpointUrl = "";

    public const string LocalFileName = "switcheroo_events.csv";

    static readonly string[] Columns =
    {
        "session_id", "timestamp_utc", "platform", "event", "race_time_s", "row", "lane", "block_color", "own_color", "result", "from_color", "to_color", "hit_in_row", "race_result", "won", "final_color", "switches",
    };

    FuelInventory inventory;
    FuelPickupSpawner spawner;
    RaceManager race;
    string sessionId;
    bool raceOver;
    readonly HashSet<int> rowsCollected = new HashSet<int>();
    int switches;

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
        inventory.Switched += OnSwitched;
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
        inventory.Switched -= OnSwitched;
        if (race != null)
            race.RaceEnded -= OnRaceEnded;
    }

    void OnPickupTouched(FuelPickup pickup, bool collected)
    {
        if (raceOver)
            return;
        if (collected)
            rowsCollected.Add(pickup.row);
        Record("block_hit", new Dictionary<string, string>
        {
            ["row"] = pickup.row.ToString(CultureInfo.InvariantCulture),
            ["lane"] = pickup.lane.ToString(CultureInfo.InvariantCulture),
            ["block_color"] = ColorName(pickup.type),
            ["own_color"] = ColorName(inventory.carFuel),
            ["result"] = inventory.LastResult
        });
    }

    void OnRowPassed(FuelRowGate gate)
    {
        if (raceOver)
            return;
        bool hit = rowsCollected.Remove(gate.row);
        Record("row_passed", new Dictionary<string, string>
        {
            ["row"] = gate.row.ToString(CultureInfo.InvariantCulture),
            ["hit_in_row"] = hit ? "1" : "0",
        });
    }

    void OnSwitched(FuelType from, FuelType to)
    {
        if(raceOver)
        {
            return;
        }
        switches += 1;
        Record("switch", new Dictionary<string, string>
        {
            ["from_color"] = ColorName(from),
            ["to_color"] = ColorName(to),
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
            ["final_color"] = ColorName(inventory.carFuel),
            ["switches"] = switches.ToString(CultureInfo.InvariantCulture),
        });
    }

    // The color players see: the car's own fuel is red, Diesel blue and Ethanol yellow.
    static string ColorName(FuelType type)
    {
        switch (type)
        {
            case FuelType.Gasoline: return "red";
            case FuelType.Diesel: return "blue";
            case FuelType.Ethanol: return "yellow";
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
