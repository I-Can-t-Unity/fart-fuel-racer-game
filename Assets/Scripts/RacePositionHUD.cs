using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The player's race position against the AI rival (1ST or 2ND) and the gap between
// them, just under the timer. Both cars are measured in metres driven along the centre
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
    [Tooltip("How far one car has to pull ahead, in metres, before the positions swap, so they don't flicker while the cars are side by side.")]
    public float swapMargin = 1f;

    // Where one car is on the lap.
    class Racer
    {
        public Rigidbody body;
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

    readonly Racer player = new Racer(), rival = new Racer();
    // Centre-line points of each shortcut's split (x) and merge (y).
    readonly Dictionary<ShortcutBranch, Vector2Int> spans = new Dictionary<ShortcutBranch, Vector2Int>();
    // Metres from centre-line point 0 to each point.
    float[] along;
    float lapLength;
    bool ahead = true;
    TMP_Text positionText, gapText;

    IEnumerator Start()
    {
        RaceManager race = RaceManager.Instance;
        if (track == null)
            track = FindFirstObjectByType<LoopTrackGenerator>();
        if (race != null)
        {
            player.body = race.carBody != null ? race.carBody : race.car != null ? race.car.GetComponentInParent<Rigidbody>() : null;
            rival.body = race.opponent != null ? race.opponent.GetComponentInParent<Rigidbody>() : null;
        }
        if (track == null || player.body == null || rival.body == null)
        {
            enabled = false;
            yield break;
        }

        BuildPanel();

        player.driver = player.body.GetComponent<CarDriver>();
        rival.driver = rival.body.GetComponent<CarDriver>();
        if (player.driver != null)
            player.driver.Respawned += PlayerRespawned;
        if (rival.driver != null)
            rival.driver.Respawned += RivalRespawned;
        ShortcutTracker.Failed += ShortcutFailed;

        // The track fills in its centre line in its own Start.
        while (track.centerline.Count == 0)
            yield return null;
        MeasureTrack();
    }

    void OnDestroy()
    {
        if (player.driver != null)
            player.driver.Respawned -= PlayerRespawned;
        if (rival.driver != null)
            rival.driver.Respawned -= RivalRespawned;
        ShortcutTracker.Failed -= ShortcutFailed;
    }

    // R puts the car back on the start line, and the lap has to be driven again.
    void PlayerRespawned() => Restart(player);
    void RivalRespawned() => Restart(rival);

    void Restart(Racer racer)
    {
        racer.point = -1;
        racer.ignoreShortcut = ShortcutTracker.IsOnBranch(racer.body);
    }

    // The fail zone has put the car back on the main road before the split.
    void ShortcutFailed(Rigidbody body, ShortcutBranch branch)
    {
        if (body == player.body) player.lost = true;
        else if (body == rival.body) rival.lost = true;
    }

    void Update()
    {
        if (along == null)
            return;
        Follow(player);
        Follow(rival);

        float gap = player.metres - rival.metres;
        if (gap > swapMargin) ahead = true;
        else if (gap < -swapMargin) ahead = false;

        positionText.text = (ahead ? "1ST" : "2ND") + "<size=50%> / 2</size>";
        positionText.color = ahead ? aheadColor : behindColor;
        gapText.text = $"{(ahead ? "AHEAD" : "BEHIND")} {Mathf.Abs(gap):0} m";
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
