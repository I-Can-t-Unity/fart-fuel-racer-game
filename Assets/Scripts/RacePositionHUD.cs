using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Physical-car race progress, displayed for the currently controlled car.
// Cars are measured in metres driven along the centre
// line since the start line. On a shortcut a car is placed between the split and the
// merge by how far along the shortcut it is, so the jump shows up as ground gained
// instead of the car standing still. Built at runtime under this canvas, like FuelInventoryUI.
public class RacePositionHUD : MonoBehaviour
{
    public LoopTrackGenerator track;
    [Tooltip("Offset of the top of the panel from the top-right corner of the screen. The default puts it just under the timer.")]
    public Vector2 topRightOffset = new Vector2(-170, -130);
    public float positionFontSize = 72f;
    public float gapFontSize = 30f;
    public Color aheadColor = new Color(.35f, 1f, .45f);
    public Color behindColor = new Color(1f, .4f, .3f);
    [Tooltip("Legacy setting retained for scene compatibility; standings use physical-car progress.")]
    public float swapMargin = 1f;

    // Where one car is on the lap.
    class Racer
    {
        public Rigidbody body;
        public System.Action onRespawn;
        public CarDriver driver;
        // Nearest centre-line point, or -1 until the car has been placed on the lap.
        public int point = -1;
        // Metres from the start line to that point and to the car, counting whole laps.
        public float pointMetres, metres;
        // Moved somewhere else at once (a missed jump), so it is looked for around the whole lap.
        public bool lost;
        // Reset to the start line while still on a shortcut, which leaves the shortcut
        // system thinking it is there, so the shortcut is ignored until that clears.
        public bool ignoreShortcut;
    }

    readonly List<Racer> racers = new List<Racer>();
    RaceManager race;
    // Centre-line points of each shortcut's split (x) and merge (y).
    readonly Dictionary<ShortcutBranch, Vector2Int> spans = new Dictionary<ShortcutBranch, Vector2Int>();
    // Metres from centre-line point 0 to each point.
    float[] along;
    float lapLength;
    TMP_Text positionText, gapText;

    public static RacePositionHUD Instance { get; private set; }

    void Awake() => Instance = this;

    // Lets the AI see how far each car has got (same numbers as the panel).
    public bool TryGetMetres(Rigidbody body, out float metres)
    {
        metres = 0f;
        if (along == null || body == null) return false;
        foreach (Racer racer in racers)
            if (racer.body == body && racer.point >= 0) { metres = racer.metres; return true; }
        return false;
    }

    public Rigidbody PlayerBody => race != null ? race.carBody : null;

    IEnumerator Start()
    {
        while (RaceManager.Instance == null) yield return null;
        race = RaceManager.Instance;
        while (race.IsCountingDown) yield return null;
        if (track == null) track = FindFirstObjectByType<LoopTrackGenerator>();
        if (track == null) { enabled = false; yield break; }
        while (track.centerline.Count < 2) yield return null;
        if (race.colorSwitchingMode)
        {
            foreach (CarDriver physicalCar in race.Racers) AddRacer(physicalCar);
        }
        else
        {
            AddRacer(race.car != null ? race.car.GetComponentInParent<CarDriver>() : null);
            AddRacer(race.opponent != null ? race.opponent.GetComponentInParent<CarDriver>() : null);
        }
        if (racers.Count == 0) { enabled = false; yield break; }
        BuildPanel();
        MeasureTrack();
        ShortcutTracker.Failed += ShortcutFailed;
    }

    void AddRacer(CarDriver physicalCar)
    {
        if (physicalCar == null) return;
        Rigidbody body = physicalCar.GetComponent<Rigidbody>();
        if (body == null || racers.Exists(item => item.body == body)) return;
        var racer = new Racer { body = body, driver = physicalCar };
        racer.onRespawn = () => Restart(racer);
        physicalCar.Respawned += racer.onRespawn;
        racers.Add(racer);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (Racer racer in racers)
            if (racer.driver != null) racer.driver.Respawned -= racer.onRespawn;
        ShortcutTracker.Failed -= ShortcutFailed;
    }

    void Restart(Racer racer)
    {
        racer.point = -1;
        racer.pointMetres = racer.metres = 0;
        racer.ignoreShortcut = ShortcutTracker.IsOnBranch(racer.body);
    }

    void ShortcutFailed(Rigidbody body, ShortcutBranch branch)
    {
        foreach (Racer racer in racers)
            if (racer.body == body) racer.lost = true;
    }

    void Update()
    {
        if (along == null || race == null) return;
        Racer controlled = null;
        foreach (Racer racer in racers)
        {
            if (racer.body == null) continue;
            Follow(racer);
            if (racer.body == race.carBody) controlled = racer;
        }
        if (controlled == null) return;
        int rank = 1;
        float leaderMetres = controlled.metres;
        float closestBehind = float.NegativeInfinity;
        foreach (Racer racer in racers)
        {
            if (racer == controlled || racer.body == null) continue;
            // Stable tie order follows roster order and cannot assign two leaders.
            if (racer.metres > controlled.metres + .01f ||
                (Mathf.Abs(racer.metres - controlled.metres) <= .01f && racers.IndexOf(racer) < racers.IndexOf(controlled))) rank++;
            leaderMetres = Mathf.Max(leaderMetres, racer.metres);
            closestBehind = Mathf.Max(closestBehind, racer.metres);
        }
        positionText.text = rank + "<size=50%> / " + racers.Count + "</size>";
        positionText.color = rank == 1 ? aheadColor : behindColor;
        gapText.text = rank == 1
            ? (float.IsNegativeInfinity(closestBehind) ? "" : $"AHEAD {Mathf.Max(0, controlled.metres - closestBehind):0} m")
            : $"TO LEADER {Mathf.Max(0, leaderMetres - controlled.metres):0} m";
    }

    void Follow(Racer racer)
    {
        int count = along.Length;
        Vector3 position = racer.body.position;

        if (racer.point < 0)
        {
            racer.point = Nearest(position, 0, count, out _);
            racer.pointMetres = Signed(along[racer.point] - along[Wrap(track.startIndex)]);
            racer.lost = false;
        }

        bool onShortcut = ShortcutTracker.TryGetBranch(racer.body, out ShortcutBranch branch);
        if (!onShortcut)
            racer.ignoreShortcut = false;
        if (onShortcut && !racer.ignoreShortcut && branch.waypoints.Count >= 2)
        {
            float fraction = branch.GetBranchFraction(position);
            // Past the end of the shortcut it is back on the main road, even if it missed the exit gate.
            if (fraction < .999f)
            {
                Vector2Int span = SpanOf(branch);
                MoveTo(racer, Wrap(span.x + Mathf.RoundToInt(Wrap(span.y - span.x) * fraction)));
                float splitMetres = racer.pointMetres + Signed(along[span.x] - along[racer.point]);
                racer.metres = splitMetres + Mathf.Repeat(along[span.y] - along[span.x], lapLength) * fraction;
                return;
            }
        }

        // Looks only near the last point, so a part of the lap that passes close by can't
        // make the car jump there (the same idea as AICarWaypointDriver.UpdateWaypoint).
        int nearest = Nearest(position, racer.point - 8, 40, out float distance);
        if (racer.lost || distance > track.roadWidth)
        {
            nearest = Nearest(position, 0, count, out distance);
            // In the air or nowhere near the road: keep the last place until it lands.
            if (distance > track.roadWidth)
                return;
            racer.lost = false;
        }
        MoveTo(racer, nearest);

        // How far past (or short of) its nearest point the car is, so close cars still compare.
        float offset = Vector3.Dot(position - track.GetPoint(nearest), track.GetForward(nearest));
        float before = Mathf.Repeat(along[nearest] - along[Wrap(nearest - 1)], lapLength);
        float after = Mathf.Repeat(along[Wrap(nearest + 1)] - along[nearest], lapLength);
        racer.metres = racer.pointMetres + Mathf.Clamp(offset, -before, after);
    }

    void MoveTo(Racer racer, int point)
    {
        racer.pointMetres += Signed(along[point] - along[racer.point]);
        racer.point = point;
    }

    void MeasureTrack()
    {
        int count = track.centerline.Count;
        along = new float[count];
        for (int i = 1; i < count; i++)
            along[i] = along[i - 1] + Vector3.Distance(track.centerline[i - 1], track.centerline[i]);
        lapLength = along[count - 1] + Vector3.Distance(track.centerline[count - 1], track.centerline[0]);
    }

    // The same split and merge AIShortcutRouter uses: the bridge builder's entry and exit
    // anchors, else the shortcut's first and last waypoints.
    Vector2Int SpanOf(ShortcutBranch branch)
    {
        if (spans.TryGetValue(branch, out Vector2Int span))
            return span;
        BrokenBridgeBuilder builder = branch.GetComponent<BrokenBridgeBuilder>();
        Vector3 entry = builder != null && builder.entryAnchor != null ? builder.entryAnchor.position : branch.waypoints[0].point.position;
        Vector3 exit = builder != null && builder.exitAnchor != null ? builder.exitAnchor.position : branch.waypoints[branch.waypoints.Count - 1].point.position;
        span = new Vector2Int(Nearest(entry, 0, along.Length, out _), Nearest(exit, 0, along.Length, out _));
        spans[branch] = span;
        return span;
    }

    int Nearest(Vector3 position, int from, int span, out float distance)
    {
        float closest = float.MaxValue;
        int closestIndex = 0;
        for (int i = 0; i < span; i++)
        {
            int index = Wrap(from + i);
            Vector3 difference = track.centerline[index] - position;
            difference.y = 0f;
            if (difference.sqrMagnitude < closest)
            {
                closest = difference.sqrMagnitude;
                closestIndex = index;
            }
        }
        distance = Mathf.Sqrt(closest);
        return closestIndex;
    }

    int Wrap(int index) => (index % along.Length + along.Length) % along.Length;

    // A distance along the lap taken the short way round, so going backwards is negative.
    float Signed(float metres) => Mathf.Repeat(metres + lapLength * .5f, lapLength) - lapLength * .5f;

    void BuildPanel()
    {
        RectTransform panel = new GameObject("RacePosition", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(1, 1);
        panel.pivot = new Vector2(.5f, 1);
        panel.anchoredPosition = topRightOffset;
        panel.sizeDelta = new Vector2(300, positionFontSize * 1.2f + gapFontSize * 1.4f);

        positionText = NewText("Position", panel, 0, positionFontSize);
        gapText = NewText("Gap", panel, -positionFontSize * 1.2f, gapFontSize);
    }

    static TMP_Text NewText(string name, RectTransform panel, float y, float fontSize)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(panel.sizeDelta.x, fontSize * 1.2f);

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.outlineWidth = .2f;
        text.outlineColor = Color.black;
        text.raycastTarget = false;
        return text;
    }
}
