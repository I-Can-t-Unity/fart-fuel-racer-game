using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turns a HUD arrow so it shows which way the track goes, compared with where you're looking.
///   Arrow up       = you're on track
///   Arrow sideways = a bend is coming that way
///   Arrow down     = you're going the wrong way (it also turns pink)
/// Put it on the arrow Image inside your dial. It follows whichever car you're driving.
/// </summary>
public class TrackDirectionArrow : MonoBehaviour
{
    [Tooltip("Your LoopTrack (found automatically if left empty).")]
    public LoopTrackGenerator track;
    [Tooltip("How far ahead along the track the arrow looks, in metres. More = it warns about bends earlier.")]
    public float lookAhead = 25f;
    [Tooltip("How quickly the arrow turns (higher = snappier).")]
    public float turnSpeed = 8f;

    [Header("Colours")]
    public Color normalColour = Color.white;
    public Color wrongWayColour = new Color(1f, .24f, .67f);
    [Tooltip("Turn pink when the track is more than this many degrees away from where you're facing.")]
    public float wrongWayAngle = 110f;

    RectTransform rect;
    Graphic graphic;
    int lastIndex = -1;
    float shownAngle;

    void Start()
    {
        rect = GetComponent<RectTransform>();
        graphic = GetComponent<Graphic>();
        if (track == null) track = FindFirstObjectByType<LoopTrackGenerator>();
    }

    void Update()
    {
        var race = RaceManager.Instance;
        if (race == null || race.CurrentPlayerCar == null || track == null
            || track.centerline == null || track.centerline.Count < 3) return;

        Transform car = race.CurrentPlayerCar;
        var pts = track.centerline;
        int n = pts.Count;
        Vector3 local = track.transform.InverseTransformPoint(car.position);

        // Nearest point of the track (searched near the last one, full search the first time).
        int best = 0; float bestD = float.MaxValue;
        int from = lastIndex < 0 ? 0 : lastIndex - 40, to = lastIndex < 0 ? n : lastIndex + 40;
        for (int k = from; k < to; k++)
        {
            int i = ((k % n) + n) % n;
            float d = (pts[i] - local).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        lastIndex = best;

        // Walk forward along the track by lookAhead metres: that's where you should be heading.
        int j = best; float walked = 0f;
        while (walked < lookAhead)
        {
            int nj = (j + 1) % n;
            walked += Vector3.Distance(pts[j], pts[nj]);
            j = nj;
            if (j == best) break;
        }
        Vector3 target = track.transform.TransformPoint(pts[j]) - car.position;
        target.y = 0f;

        // Compare with where you're looking (the camera), so "up" on the dial = straight ahead on screen.
        Transform view = Camera.main != null ? Camera.main.transform : car;
        Vector3 facing = view.forward; facing.y = 0f;
        if (target.sqrMagnitude < .01f || facing.sqrMagnitude < .01f) return;

        float angle = Vector3.SignedAngle(facing, target, Vector3.up);   // + = track goes right
        shownAngle = Mathf.LerpAngle(shownAngle, angle, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        rect.localRotation = Quaternion.Euler(0f, 0f, -shownAngle);

        if (graphic != null)
            graphic.color = Mathf.Abs(angle) > wrongWayAngle ? wrongWayColour : normalColour;
    }
}
