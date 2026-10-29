using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class LoopTrackGenerator : MonoBehaviour
{
    [Header("Track shape")]
    public float scale = 1f;
    public float roadWidth = 12f;
    [Range(4, 20)] public int samplesPerSegment = 12;
    [Tooltip("Unmarked shoulder width retained for wall spacing and AI driving limits.")]
    public float curbWidth = 1.3f;
    public float lineWidth = 0.20f;
    public float surfaceY = 0.12f;
    [Tooltip("Centre-line point where the checkered start/finish line is drawn. 2 = bottom straight (both layouts).")]
    public int startIndex = 2;
    [Tooltip("Race the track the other way round (anticlockwise). Everything that reads the centre line follows automatically.")]
    public bool reverseDirection = false;

    [Header("Walls")]
    [Tooltip("Low walls along both edges keep cars on the track. Set to 0 to remove them.")]
    public float wallHeight = 1f;
    public float wallThickness = 0.6f;
    [Tooltip("Gap between the shoulder and the wall. The AI cuts corners up to about 0.5 m past the shoulder, so keep this at least 0.6 m.")]
    public float wallGap = 1f;

    [Header("Materials (optional)")]
    public Material roadMaterial;
    public Material whiteMaterial;
    public Material wallMaterial;

    [Header("Generated points - read only")]
    public List<Vector3> centerline = new List<Vector3>();

    private readonly List<int> barrierColliderIds = new List<int>();

    public enum Layout { Classic, BridgeCircuit }

    [Header("Layout")]
    [Tooltip("Classic = the original short loop. BridgeCircuit = 1.4 km circuit with a straight broken-bridge shortcut.")]
    public Layout layout = Layout.BridgeCircuit;
    [Tooltip("Optional: the grass/ground object. It is moved and scaled to cover the whole track.")]
    public Transform groundToFit;

    // Original short loop (~0.6 km).
    static readonly Vector2[] classicAnchors = {
        new Vector2(-48,-35), new Vector2(-74,-24), new Vector2(-80, 2),
        new Vector2(-65, 25), new Vector2(-28, 22), new Vector2(-5, 31),
        new Vector2(6, 53), new Vector2(25, 65), new Vector2(58, 62),
        new Vector2(77, 43), new Vector2(76, 14), new Vector2(55, 4),
        new Vector2(23, -2), new Vector2(16, -27), new Vector2(0, -39)
    };

    // Bridge Circuit (~1.39 km, ~80-90 s a lap). Long start straight, fast right sweeper,
    // the ARCH detour (right-left-right) that the broken-bridge shortcut cuts straight across,
    // top-right corner, tight top hairpin loop, S-bends through the middle, big left sweeper.
    // Checked: no corner tighter than 13.5 m radius, separate parts of the track always
    // >= 60 m apart (centre to centre), so walls never touch.
    static readonly Vector2[] bridgeAnchors = {
        new Vector2(-110,-110), new Vector2(0,-118), new Vector2(90,-110), new Vector2(140,-85),
        new Vector2(160,-40), new Vector2(160,-5),
        new Vector2(200,20), new Vector2(225,55), new Vector2(200,90), new Vector2(160,115),
        new Vector2(158,150), new Vector2(130,185), new Vector2(80,192), new Vector2(35,180),
        new Vector2(20,150), new Vector2(40,125), new Vector2(85,115), new Vector2(105,85),
        new Vector2(90,50), new Vector2(40,30), new Vector2(-10,50), new Vector2(-40,90),
        new Vector2(-90,105), new Vector2(-140,85), new Vector2(-165,40), new Vector2(-165,-30),
        new Vector2(-150,-85)
    };

    Vector2[] anchors => layout == Layout.Classic ? classicAnchors : bridgeAnchors;

    void Start() { Generate(); }

    [ContextMenu("Regenerate Track")]
    public void Generate()
    {
        ClearGenerated();
        centerline.Clear();
        for (int i = 0; i < anchors.Length; i++)
        {
            Vector2 p0 = anchors[(i - 1 + anchors.Length) % anchors.Length];
            Vector2 p1 = anchors[i];
            Vector2 p2 = anchors[(i + 1) % anchors.Length];
            Vector2 p3 = anchors[(i + 2) % anchors.Length];
            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;
                Vector2 p = 0.5f * ((2f * p1) + (-p0 + p2) * t +
                    (2f*p0 - 5f*p1 + 4f*p2 - p3) * t*t +
                    (-p0 + 3f*p1 - 3f*p2 + p3) * t*t*t);
                centerline.Add(new Vector3(p.x * scale, surfaceY, p.y * scale));
            }
        }

        if (reverseDirection) centerline.Reverse();
        FitGround();

        int n = centerline.Count;
        Vector3[] left = new Vector3[n], right = new Vector3[n];
        Vector3[] sides = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tangent = (centerline[(i+1)%n] - centerline[(i-1+n)%n]).normalized;
            Vector3 side = new Vector3(-tangent.z, 0, tangent.x);
            sides[i] = side;
            left[i] = centerline[i] - side * roadWidth * .5f;
            right[i] = centerline[i] + side * roadWidth * .5f;
        }

        Mesh roadMesh = MakeStrip(left, right, 0f);
        GetComponent<MeshFilter>().sharedMesh = roadMesh;
        GetComponent<MeshCollider>().sharedMesh = null;
        GetComponent<MeshCollider>().sharedMesh = roadMesh;
        GetComponent<MeshRenderer>().sharedMaterial = Mat(roadMaterial, new Color(.29f,.30f,.32f));

        // Walls just outside the unmarked shoulders. They are frictionless so a car that hits
        // one slides along it instead of being grabbed and spun round.
        if (wallHeight > 0)
        {
            Material wall = Mat(wallMaterial, new Color(.85f, .85f, .88f));
            PhysicsMaterial slide = new PhysicsMaterial("TrackWall")
            {
                dynamicFriction = 0,
                staticFriction = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0
            };
            float offset = roadWidth * .5f + curbWidth + wallGap + wallThickness * .5f;
            for (int i = 0; i < n; i++)
            {
                int next = (i+1)%n;
                Vector3 aL = centerline[i] - sides[i] * offset, bL = centerline[next] - sides[next] * offset;
                Vector3 aR = centerline[i] + sides[i] * offset, bR = centerline[next] + sides[next] * offset;
                // Shortcut entrances: leave out any wall piece covered by a WallOpening box.
                if (!WallOpening.IsInsideAny((aL + bL) * 0.5f)) CreateWall("Wall_L_"+i, aL, bL, wall, slide);
                if (!WallOpening.IsInsideAny((aR + bR) * 0.5f)) CreateWall("Wall_R_"+i, aR, bR, wall, slide);
            }
        }

        // Dashed white center line.
        Material white = Mat(whiteMaterial, Color.white);
        float distance = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i+1)%n;
            if (Mathf.FloorToInt(distance / 2.8f) % 2 == 0)
            {
                Vector3 tangent = (centerline[next]-centerline[i]).normalized;
                Vector3 side = new Vector3(-tangent.z,0,tangent.x)*lineWidth*.5f;
                CreateStrip("Dash_"+i,
                    new[]{centerline[i]-side,centerline[next]-side},
                    new[]{centerline[i]+side,centerline[next]+side}, .025f, white);
            }
            distance += Vector3.Distance(centerline[i], centerline[next]);
        }

        // Start line and suggested car starting positions.
        // Vector3 forward = (centerline[3]-centerline[1]).normalized;
        // Vector3 across = new Vector3(-forward.z,0,forward.x);
        // Vector3 start = centerline[2];
        // for (int i=0; i<8; i++)
        // {
        //     float a = -roadWidth*.5f + i*roadWidth/8f;
        //     float b = a + roadWidth/8f;
        //     Vector3 offset = forward*.85f;
        //     CreateStrip("Finish_"+i,
        //         new[]{start+across*a-offset,start+across*a+offset},
        //         new[]{start+across*b-offset,start+across*b+offset}, .035f,
        //         i%2==0 ? white : Mat(null, Color.black));
        // }

        // Black-and-white checkered start/finish line.
        Vector3 forward = (GetPoint(startIndex + 1) - GetPoint(startIndex - 1)).normalized;
        Vector3 across = new Vector3(-forward.z, 0, forward.x);
        Vector3 start = GetPoint(startIndex);

        int columns = 10;
        int rows = 4;
        float finishLength = 3f;

        Material black = Mat(null, Color.black);
        Material whiteFinish = Mat(whiteMaterial, Color.white);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                float a = -roadWidth * 0.5f + col * roadWidth / columns;
                float b = -roadWidth * 0.5f + (col + 1) * roadWidth / columns;

                float z0 = -finishLength * 0.5f + row * finishLength / rows;
                float z1 = -finishLength * 0.5f + (row + 1) * finishLength / rows;

                Vector3 p0 = start + across * a + forward * z0;
                Vector3 p1 = start + across * a + forward * z1;
                Vector3 p2 = start + across * b + forward * z0;
                Vector3 p3 = start + across * b + forward * z1;

                CreateStrip(
                    "Finish_" + row + "_" + col,
                    new[] { p0, p1 },
                    new[] { p2, p3 },
                    0.04f,
                    (row + col) % 2 == 0 ? whiteFinish : black
                );
            }
        }

        Debug.Log("Track generated. Start/finish at " + GetPoint(startIndex) + ", cars face toward " + GetPoint(startIndex + 1));
    }

    void FitGround()
    {
        if (!groundToFit || centerline.Count == 0) return;
        Vector3 min = centerline[0], max = centerline[0];
        foreach (var p in centerline) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        float margin = roadWidth * 0.5f + curbWidth + wallGap + wallThickness + 25f;
        float width = (max.x - min.x) + margin * 2f, depth = (max.z - min.z) + margin * 2f;

        // Work out how big the ground mesh is at scale 1 (Plane = 10 x 10, Quad/Cube = 1 x 1).
        Vector3 unit = Vector3.one;
        var mf = groundToFit.GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh) unit = mf.sharedMesh.bounds.size;
        float ux = Mathf.Max(0.001f, unit.x);
        bool quadLike = unit.z < 0.001f;              // a Quad lies in X/Y and is rotated flat
        float uz = Mathf.Max(0.001f, quadLike ? unit.y : unit.z);

        Vector3 pos = groundToFit.position;
        groundToFit.position = new Vector3((min.x + max.x) * 0.5f, pos.y, (min.z + max.z) * 0.5f);
        Vector3 sc = groundToFit.localScale;
        groundToFit.localScale = quadLike ? new Vector3(width / ux, depth / uz, sc.z)
                                          : new Vector3(width / ux, sc.y, depth / uz);
    }

    public Vector3 GetPoint(int index) => centerline[(index % centerline.Count + centerline.Count) % centerline.Count];
    public Vector3 GetForward(int index) => (GetPoint(index+1)-GetPoint(index)).normalized;

    // Mesh MakeStrip(Vector3[] a, Vector3[] b, float height)
    // {
    //     int count = a.Length;
    //     bool closed = count > 2;
    //     int segments = closed ? count : count-1;
    //     Vector3[] verts = new Vector3[count*2];
    //     Vector2[] uv = new Vector2[count*2];
    //     int[] tris = new int[segments*12]; // double sided for reliable visibility/collision
    //     for(int i=0;i<count;i++)
    //     {
    //         verts[i*2] = a[i]+Vector3.up*height;
    //         verts[i*2+1] = b[i]+Vector3.up*height;
    //         uv[i*2] = new Vector2(0,i); uv[i*2+1] = new Vector2(1,i);
    //     }
    //     for(int i=0;i<segments;i++)
    //     {
    //         int x=i*2, y=((i+1)%count)*2, k=i*12;
    //         tris[k]=x;tris[k+1]=y;tris[k+2]=x+1;
    //         tris[k+3]=x+1;tris[k+4]=y;tris[k+5]=y+1;
    //         tris[k+6]=x+1;tris[k+7]=y;tris[k+8]=x;
    //         tris[k+9]=y+1;tris[k+10]=y;tris[k+11]=x+1;
    //     }
    //     Mesh mesh = new Mesh();
    //     mesh.name="Track Strip";
    //     mesh.vertices=verts; mesh.uv=uv; mesh.triangles=tris;
    //     mesh.RecalculateNormals(); mesh.RecalculateBounds();
    //     return mesh;
    // }


    Mesh MakeStrip(Vector3[] a, Vector3[] b, float height)
    {
        int count = a.Length;
        bool closed = count > 2;
        int segments = closed ? count : count - 1;

        Vector3[] verts = new Vector3[count * 2];
        Vector2[] uv = new Vector2[count * 2];
        int[] tris = new int[segments * 6];

        for (int i = 0; i < count; i++)
        {
            verts[i * 2] = a[i] + Vector3.up * height;
            verts[i * 2 + 1] = b[i] + Vector3.up * height;

            uv[i * 2] = new Vector2(0, i);
            uv[i * 2 + 1] = new Vector2(1, i);
        }

        for (int i = 0; i < segments; i++)
        {
            int x = i * 2;
            int y = ((i + 1) % count) * 2;
            int k = i * 6;

            // Single-sided triangles facing upward.
            tris[k] = x;
            tris[k + 1] = x + 1;
            tris[k + 2] = y;

            tris[k + 3] = x + 1;
            tris[k + 4] = y + 1;
            tris[k + 5] = y;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Track Strip";
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = tris;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }


    void CreateStrip(string name, Vector3[] a, Vector3[] b, float height, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform,false);
        go.AddComponent<MeshFilter>().sharedMesh = MakeStrip(a,b,height);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    void CreateWall(string name, Vector3 a, Vector3 b, Material material, PhysicsMaterial physics)
    {
        // The road sits above the grass, so sink the wall a little to avoid a gap underneath.
        const float sink = 0.3f;
        Vector3 along = b - a;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        int barrierLayer = LayerMask.NameToLayer("TrackBarrier");
        if (barrierLayer >= 0)
            wall.layer = barrierLayer;
        wall.transform.SetParent(transform, false);
        wall.transform.SetPositionAndRotation(
            (a + b) * .5f + Vector3.up * (wallHeight - sink) * .5f,
            Quaternion.LookRotation(along));
        // Pieces overlap by one wall thickness so the outside of a bend has no gaps.
        wall.transform.localScale = new Vector3(wallThickness, wallHeight + sink, along.magnitude + wallThickness);
        wall.GetComponent<Renderer>().sharedMaterial = material;
        Collider shape = wall.GetComponent<Collider>();
        shape.sharedMaterial = physics;
        int colliderId = shape.GetInstanceID();
        barrierColliderIds.Add(colliderId);
        VehicleBarrierContacts.RegisterBarrier(colliderId);
    }

    Material Mat(Material existing, Color color)
    {
        if(existing!=null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if(shader==null) shader = Shader.Find("Standard");
        Material m = new Material(shader); m.color=color; return m;
    }

    void OnDestroy() => UnregisterBarriers();

    void UnregisterBarriers()
    {
        foreach (int id in barrierColliderIds)
            VehicleBarrierContacts.RemoveBarrier(id);
        barrierColliderIds.Clear();
    }

    void ClearGenerated()
    {
        UnregisterBarriers();
        for(int i=transform.childCount-1;i>=0;i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if(Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }
}
