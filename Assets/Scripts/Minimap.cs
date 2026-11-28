using UnityEngine;
using UnityEngine.UI;

// Bottom-right minimap: the track outline traced from the centre line, plus a dot per car
// in its colour. The car you're driving gets a white border. Built at runtime from plain
// UI Images (no art assets). North-up: world +Z points up the map.
public class Minimap : MonoBehaviour
{
    public float size = 280f;
    public Vector2 bottomRightOffset = new Vector2(-40f, 40f);
    public float lineWidth = 7f;
    public float dotSize = 20f;
    public Color trackColor = new Color(1f, 1f, 1f, .8f);

    RaceManager race;
    LoopTrackGenerator track;
    RectTransform map, playerRing;
    RectTransform[] dots;
    Vector3 center;
    float scale;

    void Update()
    {
        if (map == null) TryBuild();
        else MoveDots();
    }

    void TryBuild()
    {
        race = RaceManager.Instance;
        if (track == null) track = FindFirstObjectByType<LoopTrackGenerator>();
        if (race == null || !race.RacersInitialized || track == null || track.centerline.Count < 2) return;

        // Fit the track's bounding box inside the panel, keeping its proportions.
        Vector3 lo = World(0), hi = lo;
        for (int i = 1; i < track.centerline.Count; i++) { lo = Vector3.Min(lo, World(i)); hi = Vector3.Max(hi, World(i)); }
        center = (lo + hi) * .5f;
        scale = (size - 40f) / Mathf.Max(hi.x - lo.x, hi.z - lo.z, 1f);

        map = (RectTransform)new GameObject("Minimap", typeof(RectTransform)).transform;
        map.SetParent(transform, false);
        map.anchorMin = map.anchorMax = map.pivot = new Vector2(1f, 0f);
        map.anchoredPosition = bottomRightOffset;
        map.sizeDelta = new Vector2(size, size);
        Paint(map.gameObject, new Color(0f, 0f, 0f, .55f));

        // One thin bar per stretch of centre line; ~150 bars is plenty to read the shape.
        int count = track.centerline.Count, step = Mathf.Max(1, count / 150);
        for (int i = 0; i < count; i += step)
        {
            Vector2 a = ToMap(World(i)), b = ToMap(World(i + step));
            RectTransform bar = Square("Track", (a + b) * .5f, lineWidth, trackColor);
            bar.sizeDelta = new Vector2(Vector2.Distance(a, b) + lineWidth, lineWidth);
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        playerRing = Square("Player Ring", Vector2.zero, dotSize + 8f, Color.white);
        dots = new RectTransform[race.Racers.Count];
        for (int i = 0; i < dots.Length; i++)
            dots[i] = Square("Car " + i, Vector2.zero, dotSize, FuelPalette.ColorOf(race.ColorForRacerIndex(i)));
        MoveDots();
    }

    void MoveDots()
    {
        Transform current = race.CurrentPlayerCar;
        CarDriver player = current != null ? current.GetComponentInParent<CarDriver>() : null;
        playerRing.gameObject.SetActive(false);
        for (int i = 0; i < dots.Length; i++)
        {
            CarDriver car = race.Racers[i];
            if (car == null) { dots[i].gameObject.SetActive(false); continue; }
            dots[i].anchoredPosition = ToMap(car.transform.position);
            if (car != player) continue;
            playerRing.anchoredPosition = dots[i].anchoredPosition;
            playerRing.gameObject.SetActive(true);
            dots[i].SetAsLastSibling(); // the player's dot draws on top of rivals
        }
    }

    // The centre line is stored in the track's local space; cars move in world space.
    Vector3 World(int index) => track.transform.TransformPoint(track.GetPoint(index));

    Vector2 ToMap(Vector3 world) => new Vector2(world.x - center.x, world.z - center.z) * scale;

    RectTransform Square(string name, Vector2 position, float side, Color color)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(map, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(side, side);
        Paint(rect.gameObject, color);
        return rect;
    }

    static void Paint(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
