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

    public const string BuildId = "alpha-analytics-v2";
    public const string LocalFileName = "fuel_pickup_events.csv";

    static readonly string[] Columns =
    {
        "session_id", "timestamp_utc", "platform", "event", "race_time_s", "lane_spacing", "row",
        "fuel_type", "color", "lane", "suitable", "collected_in_row", "cubes_available", "bag_full",
        "race_result", "won", "event_id", "schema_version", "build_id", "scene", "car_fuel", "mix", "fuel_amount",
    };

    FuelInventory inventory;
    FuelPickupSpawner spawner;
    RaceManager race;
    string sessionId;
    bool raceOver;
    readonly Dictionary<int, int> availableOnApproach = new Dictionary<int, int>();
    readonly Dictionary<int, bool> fullOnApproach = new Dictionary<int, bool>();
    readonly Dictionary<int, FuelType> firstFuel = new Dictionary<int, FuelType>();
    readonly HashSet<int> rowsPassed = new HashSet<int>();
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
        sessionId = "r-" + Guid.NewGuid().ToString("N");
    }

    void Start()
    {
        spawner = FindFirstObjectByType<FuelPickupSpawner>();
        race = RaceManager.Instance;
        FuelPickup.PlayerTouched += OnPickupTouched;
        FuelRowGate.PlayerPassed += OnRowPassed;
        FuelRowGate.PlayerApproached += OnRowApproached;
        inventory.MixCreated += OnMixCreated;
        inventory.MixUsed += OnMixUsed;
        inventory.FuelDumped += OnFuelDumped;
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
        FuelRowGate.PlayerApproached -= OnRowApproached;
        inventory.MixCreated -= OnMixCreated;
        inventory.MixUsed -= OnMixUsed;
        inventory.FuelDumped -= OnFuelDumped;
        if (race != null)
            race.RaceEnded -= OnRaceEnded;
    }

    void OnPickupTouched(FuelPickup pickup, bool collected)
    {
        if (raceOver || (race != null && !race.IsRacing))
            return;
        if (collected && rowsCollected.Add(pickup.row))
            firstFuel[pickup.row] = pickup.type;
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
        if (raceOver || !rowsPassed.Add(gate.row))
            return;
        bool collected = rowsCollected.Remove(gate.row);
        Record("row_passed", new Dictionary<string, string>
        {
            ["row"] = gate.row.ToString(CultureInfo.InvariantCulture),
            ["collected_in_row"] = collected ? "1" : "0",
            ["fuel_type"] = collected ? firstFuel[gate.row].ToString() : "",
            ["color"] = collected ? ColorName(firstFuel[gate.row]) : "",
            // Count the cube the player just took as available when they arrived.
            ["cubes_available"] = Math.Max(collected ? 1 : 0, availableOnApproach.TryGetValue(gate.row, out int available) ? available : gate.AvailableCount + (collected ? 1 : 0)).ToString(CultureInfo.InvariantCulture),
            // A full bag means no pickup was possible, so the row isn't a real opportunity.
            ["bag_full"] = !collected && (fullOnApproach.TryGetValue(gate.row, out bool full) ? full : inventory.IsFull) ? "1" : "0",
        });
    }

    void OnRowApproached(FuelRowGate gate)
    {
        if (raceOver || availableOnApproach.ContainsKey(gate.row)) return;
        availableOnApproach[gate.row] = gate.AvailableCount;
        fullOnApproach[gate.row] = inventory.IsFull;
    }

    void OnMixCreated(FuelMix mix) => RecordMechanic("mix_created", mix.ToString(), "");
    void OnMixUsed(FuelMix mix) => RecordMechanic("mix_used", mix.ToString(), "");
    void OnFuelDumped(float amount) => RecordMechanic("fuel_dumped", "", amount.ToString("0.##", CultureInfo.InvariantCulture));
    void RecordMechanic(string name, string mix, string amount)
    {
        if (!raceOver) Record(name, new Dictionary<string, string> { ["mix"] = mix, ["fuel_amount"] = amount });
    }

    public void EndSession() => OnRaceEnded(false, "ABANDONED");

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

    // Keep recorded color names aligned with the fuel palette players see.
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
        fields["event_id"] = "e-" + Guid.NewGuid().ToString("N");
        fields["schema_version"] = "2";
        fields["build_id"] = BuildId;
        fields["scene"] = gameObject.scene.name;
        fields["car_fuel"] = inventory.carFuel.ToString();
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
        else
        {
            // Expand the previous 16-column editor CSV without losing old sessions.
            string header;
            using (var reader = new StreamReader(path)) header = reader.ReadLine() ?? "";
            string[] previousColumns = header.Split(',');
            if (previousColumns.Length < Columns.Length)
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0) lines = new string[1];
                lines[0] = string.Join(",", Columns);
                string padding = new string(',', Columns.Length - previousColumns.Length);
                for (int i = 1; i < lines.Length; i++) lines[i] += padding;
                File.WriteAllLines(path, lines);
            }
        }
        var values = new string[Columns.Length];
        for (int i = 0; i < Columns.Length; i++)
            values[i] = fields.TryGetValue(Columns[i], out string value) ? value : "";
        File.AppendAllText(path, string.Join(",", values) + "\n");
    }
}
