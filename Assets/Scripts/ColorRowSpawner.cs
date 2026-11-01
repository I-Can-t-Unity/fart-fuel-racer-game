using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Places rows of red/blue blocks across the track. Every row has BOTH colours,
/// so whichever car you're in, there's always a lane you can safely take and a
/// lane that swaps you. Put on an empty "BlockRows" object (not inside LoopTrack),
/// set Track Object = LoopTrack, then ⋮ → "Build Block Rows".
/// </summary>
public class ColorRowSpawner : MonoBehaviour
{
    public LoopTrackGenerator track;

    [Tooltip("Centre-line points where rows go. Defaults are set for Bridge Circuit: none on the " +
             "final straight, none in the first stretch after the start, none on the shortcut lanes.")]
    public int[] rowIndices = { 28, 42, 70, 95, 130, 155, 190, 225, 258 };
    [Range(2, 6)] public int lanes = 4;
    public float blockSize = 2.2f;
    public float respawnSeconds = 6f;
    [Tooltip("Change to get different colour patterns.")]
    public int seed = 7;

    const string RootName = "_Rows";

    // Each row mixes both colours; R = 0, B = 1.
    static readonly int[][] patterns4 = {
        new[] {0,1,0,1}, new[] {1,0,1,0}, new[] {0,0,1,1}, new[] {1,1,0,0}, new[] {0,1,1,0}, new[] {1,0,0,1}
    };

    [ContextMenu("Build Block Rows")]
    public void Build()
    {
        if (!track) track = FindFirstObjectByType<LoopTrackGenerator>();
        if (!track || track.centerline.Count < 10)
        {
            Debug.LogError("Set Track = LoopTrack and click Regenerate Track on it first.", this);
            return;
        }
        if (!GetComponent<PreserveOnRegenerate>()) gameObject.AddComponent<PreserveOnRegenerate>();

        var old = transform.Find(RootName);
        if (old) DestroyImmediate(old.gameObject);
        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var rng = new System.Random(seed);
        var c = track.centerline; int n = c.Count;
        float spacing = track.roadWidth / lanes;
        int count = 0;

        foreach (int raw in rowIndices)
        {
            int i = ((raw % n) + n) % n;
            Vector3 fwd = c[(i + 1) % n] - c[(i - 1 + n) % n]; fwd.y = 0f; fwd.Normalize();
            Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);
            int[] pattern = Pattern(rng);

            var row = new GameObject($"Row_{i}").transform;
            row.SetParent(root, false);
            row.SetPositionAndRotation(c[i], Quaternion.LookRotation(fwd, Vector3.up));

            for (int l = 0; l < lanes; l++)
            {
                float x = -track.roadWidth * 0.5f + spacing * (l + 0.5f);
                var go = new GameObject($"Block_{l}");
                go.transform.SetParent(row, false);
                go.transform.SetPositionAndRotation(c[i] + side * x + Vector3.up * (blockSize * 0.5f + 0.35f),
                                                    Quaternion.LookRotation(fwd, Vector3.up));
                go.AddComponent<BoxCollider>();
                var b = go.AddComponent<ColorBlock>();
                b.color = pattern[l] == 0 ? CarColor.Red : CarColor.Blue;
                b.size = blockSize;
                b.respawnSeconds = respawnSeconds;
                b.BuildVisual();
                count++;
            }
        }
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log($"Switcheroo: built {rowIndices.Length} rows ({count} blocks).", this);
    }

    int[] Pattern(System.Random rng)
    {
        if (lanes == 4) return patterns4[rng.Next(patterns4.Length)];
        // Other lane counts: random, but always both colours present.
        var p = new int[lanes];
        do { for (int l = 0; l < lanes; l++) p[l] = rng.Next(2); }
        while (System.Array.IndexOf(p, 0) < 0 || System.Array.IndexOf(p, 1) < 0);
        return p;
    }
}
