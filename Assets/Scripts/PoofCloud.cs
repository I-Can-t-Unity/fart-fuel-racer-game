using UnityEngine;

/// <summary>A quick coloured "poof" of puffs that grow and fade. Spawned on a swap or a shield smash.</summary>
public class PoofCloud : MonoBehaviour
{
    public static void Spawn(Vector3 at, Color color, int puffs = 12, float size = 2.2f)
    {
        var go = new GameObject("Poof");
        go.transform.position = at;
        var p = go.AddComponent<PoofCloud>();
        p.Build(color, puffs, size);
    }

    Transform[] parts;
    Vector3[] dirs;
    Material mat;
    Color baseColor;
    float t;
    const float Life = 0.9f;

    void Build(Color color, int puffs, float size)
    {
        baseColor = new Color(color.r, color.g, color.b, 0.7f);
        mat = Switcheroo.Transparent(baseColor);
        parts = new Transform[puffs];
        dirs = new Vector3[puffs];
        for (int i = 0; i < puffs; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(transform, false);
            s.GetComponent<Renderer>().sharedMaterial = mat;
            s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            parts[i] = s.transform;
            dirs[i] = (Random.insideUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(1.5f, 3.5f);
            s.transform.localScale = Vector3.one * size * Random.Range(0.6f, 1.1f);
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = t / Life;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].localPosition = dirs[i] * Mathf.Sqrt(k) * 1.6f;
            parts[i].localScale *= 1f + Time.deltaTime * 1.2f;
        }
        mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - k));
        if (t >= Life) { Destroy(mat); Destroy(gameObject); }
    }
}
