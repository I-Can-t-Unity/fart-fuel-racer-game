using UnityEngine;
using UnityEngine.UI;

// Draws an orthographic cube in the canvas, with a fixed button hit area.
public sealed class VehicleColorBlock : MaskableGraphic
{
    static readonly Quaternion ViewTilt = Quaternion.Euler(-25f, 0f, 0f);
    static readonly Vector3 LightDirection = new Vector3(-.45f, .75f, -.5f).normalized;
    static readonly Vector3[] Corners =
    {
        new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f),
        new Vector3(1f, 1f, -1f), new Vector3(-1f, 1f, -1f),
        new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f),
        new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f)
    };

    static readonly (Vector3 normal, int a, int b, int c, int d)[] Faces =
    {
        (Vector3.back, 0, 3, 2, 1),
        (Vector3.right, 1, 2, 6, 5),
        (Vector3.forward, 5, 6, 7, 4),
        (Vector3.left, 4, 7, 3, 0),
        (Vector3.up, 3, 7, 6, 2)
    };

    [SerializeField] FuelType fuel;
    float angle = 24f;

    public float Angle
    {
        get => angle;
        set
        {
            if (Mathf.Approximately(angle, value))
                return;

            angle = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Min(rect.width, rect.height) * .29f;
        Quaternion rotation = ViewTilt * Quaternion.Euler(0f, angle, 0f);
        Color baseColor = Color.Lerp(FuelPalette.ColorOf(fuel), Color.white, .26f) * color;

        foreach (var face in Faces)
        {
            Vector3 normal = rotation * face.normal;
            if (normal.z >= 0f)
                continue;

            float light = .62f + .42f * Mathf.Max(0f, Vector3.Dot(normal, LightDirection));
            Color32 tint = new Color(baseColor.r * light, baseColor.g * light, baseColor.b * light, baseColor.a);
            int start = mesh.currentVertCount;
            AddCorner(face.a);
            AddCorner(face.b);
            AddCorner(face.c);
            AddCorner(face.d);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);

            void AddCorner(int index)
            {
                Vector2 point = rect.center + (Vector2)(rotation * Corners[index]) * scale;
                mesh.AddVert(point, tint, Vector2.zero);
            }
        }
    }
}
