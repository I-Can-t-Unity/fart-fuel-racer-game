#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// TEMPORARY, editor only. Puts a see-through pink SuperFartJumpGate across the loop
// track so the gate can be tried before the real shortcut exists, and logs every car
// it lets through or turns away. It is added to any scene with a LoopTrackGenerator
// when that scene loads, so the race scene itself is not changed, and it never goes
// into a build. Delete this file once the real gate sits at the shortcut entrance.
public class SuperFartJumpTestGate : MonoBehaviour
{
    // Centerline point (0-179) the gate sits on. The first two fuel rows are at 40
    // and 85, so a car can pick up the same wrong fuel twice before reaching it.
    const int Waypoint = 100;
    const float Height = 3f, Depth = 3f;

    LoopTrackGenerator track;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= AddToScene;
        SceneManager.sceneLoaded += AddToScene;
    }

    static void AddToScene(Scene scene, LoadSceneMode mode)
    {
        LoopTrackGenerator track = FindFirstObjectByType<LoopTrackGenerator>();
        if (track != null)
            new GameObject("SuperFartJumpTestGate").AddComponent<SuperFartJumpTestGate>().track = track;
    }

    void OnEnable()
    {
        SuperFartJumpGate.Activated += LogActivated;
        SuperFartJumpGate.Denied += LogDenied;
    }

    void OnDisable()
    {
        SuperFartJumpGate.Activated -= LogActivated;
        SuperFartJumpGate.Denied -= LogDenied;
    }

    IEnumerator Start()
    {
        // The track fills in its centerline in its own Start.
        while (track.centerline.Count == 0)
            yield return null;

        // As wide as the fuel row gates: the road, curbs and the gap to the walls.
        Vector3 forward = track.GetForward(Waypoint);
        float width = track.roadWidth + 2 * (track.curbWidth + track.wallGap);
        GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gate.name = "SuperFartJumpGate (test)";
        gate.transform.SetParent(transform, false);
        gate.transform.SetPositionAndRotation(track.GetPoint(Waypoint) + Vector3.up * Height * .5f, Quaternion.LookRotation(forward));
        gate.transform.localScale = new Vector3(width, Height, Depth);
        Renderer gateRenderer = gate.GetComponent<Renderer>();
        gateRenderer.sharedMaterial = MakeSeeThrough(new Color(1f, .35f, .7f, .35f));
        gateRenderer.shadowCastingMode = ShadowCastingMode.Off;
        // The cube's own box collider becomes the gate's trigger.
        gate.AddComponent<SuperFartJumpGate>();
    }

    static void LogActivated(SuperFartJumpGate gate, Rigidbody car) =>
        Debug.Log($"[Super Fart jump test] {car.name} spent Super Fart and should launch ({Time.time:0.00}s)");

    static void LogDenied(SuperFartJumpGate gate, Rigidbody car) =>
        Debug.Log($"[Super Fart jump test] {car.name} has no Super Fart, nothing spent ({Time.time:0.00}s)");

    // URP Unlit switched to its transparent mode, and visible from inside so the
    // camera still sees it while driving through.
    static Material MakeSeeThrough(Color color)
    {
        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
        material.SetFloat("_Surface", 1);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }
}
#endif
