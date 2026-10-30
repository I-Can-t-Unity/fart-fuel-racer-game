using UnityEngine;

/// <summary>
/// Shows this UI panel only while the player is driving TOWARD the shortcut and
/// close to it, then hides it again once they pass. Put it on JumpInstructionsPanel.
///
/// It fades a CanvasGroup instead of switching the object on/off, so it never
/// fights other scripts (e.g. the HUD reset on "Race Again").
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ShowNearShortcut : MonoBehaviour
{
    [Tooltip("Drag PlayerCar here (not the AI).")]
    public Transform player;
    [Tooltip("Drag Shortcut_BrokenBridge → Anchor_Entry here.")]
    public Transform shortcutEntry;

    [Tooltip("Start showing when the player is this close (metres).")]
    public float showDistance = 80f;
    [Tooltip("How much the car must be facing the shortcut (0 = sideways, 1 = straight at it).")]
    [Range(-1f, 1f)] public float facingThreshold = 0.3f;
    public float fadeSpeed = 4f;

    CanvasGroup group;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    void Update()
    {
        bool show = false;
        if (player && shortcutEntry)
        {
            Vector3 toEntry = shortcutEntry.position - player.position;
            toEntry.y = 0f;
            float dist = toEntry.magnitude;
            Vector3 fwd = player.forward; fwd.y = 0f;
            bool facing = dist > 0.1f && Vector3.Dot(fwd.normalized, toEntry / dist) > facingThreshold;
            show = dist < showDistance && facing;   // once you pass the split, "facing" goes false → hides
        }
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * Time.deltaTime);
    }
}
