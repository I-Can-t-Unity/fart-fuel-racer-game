using UnityEngine;

/// <summary>
/// Marker: the track generator must NOT delete this object (or its children)
/// when it clears and rebuilds the track. See SETUP.md, "Generator patch".
/// Safest of all: keep shortcut objects OUTSIDE the generated track's parent.
/// </summary>
[DisallowMultipleComponent]
public class PreserveOnRegenerate : MonoBehaviour
{
    /// <summary>Use this in the generator's cleanup loop.</summary>
    public static bool ShouldKeep(Transform t)
    {
        return t != null && t.GetComponentInChildren<PreserveOnRegenerate>(true) != null;
    }
}
