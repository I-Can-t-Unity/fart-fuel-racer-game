using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class LoopTrackGenerator : MonoBehaviour
{
    [Header("Track shape")]
    public float scale = 1f;
    public float roadWidth = 12f;
    [Range(4, 20)] public int samplesPerSegment = 12;
    public float curbWidth = 1.3f;
    public float curbHeight = 0.10f;
    public float stripeLength = 2.2f;
    public float lineWidth = 0.20f;
    public float surfaceY = 0.12f;

    [Header("Materials (optional)")]
    public Material roadMaterial;
    public Material whiteMaterial;
    public Material redMaterial;

    [Header("Generated points - read only")]
    public List<Vector3> centerline = new List<Vector3>();

    // Control points arranged to make a closed, multi-bend circuit.
    readonly Vector2[] anchors = {
        new Vector2(-48,-35), new Vector2(-74,-24), new Vector2(-80, 2),
        new Vector2(-65, 25), new Vector2(-28, 22), new Vector2(-5, 31),
        new Vector2(6, 53), new Vector2(25, 65), new Vector2(58, 62),
        new Vector2(77, 43), new Vector2(76, 14), new Vector2(55, 4),
        new Vector2(23, -2), new Vector2(16, -27), new Vector2(0, -39)
    };

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

        int n = centerline.Count;
        Vector3[] left = new Vector3[n], right = new Vector3[n];
        Vector3[] outerLeft = new Vector3[n], outerRight = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tangent = (centerline[(i+1)%n] - centerline[(i-1+n)%n]).normalized;
            Vector3 side = new Vector3(-tangent.z, 0, tangent.x);
            left[i] = centerline[i] - side * roadWidth * .5f;
            right[i] = centerline[i] + side * roadWidth * .5f;
            outerLeft[i] = left[i] - side * curbWidth;
            outerRight[i] = right[i] + side * curbWidth;
        }

        Mesh roadMesh = MakeStrip(left, right, 0f);
        GetComponent<MeshFilter>().sharedMesh = roadMesh;
        GetComponent<MeshCollider>().sharedMesh = null;
        GetComponent<MeshCollider>().sharedMesh = roadMesh;
        GetComponent<MeshRenderer>().sharedMaterial = Mat(roadMaterial, new Color(.29f,.30f,.32f));

        // Curbs alternate red and white in short strips all around both edges.
        float distance = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i+1)%n;
            Vector3[] l0 = { outerLeft[i], outerLeft[next] };
            Vector3[] l1 = { left[i], left[next] };
            Vector3[] r0 = { right[i], right[next] };
            Vector3[] r1 = { outerRight[i], outerRight[next] };
            bool red = Mathf.FloorToInt(distance / Mathf.Max(.3f,stripeLength)) % 2 == 0;
            Material material = red ? Mat(redMaterial, Color.red) : Mat(whiteMaterial, Color.white);
            CreateStrip("Curb_L_"+i, l0, l1, curbHeight, material);
            CreateStrip("Curb_R_"+i, r0, r1, curbHeight, material);
            distance += Vector3.Distance(centerline[i], centerline[next]);
        }

        // Dashed white center line.
        Material white = Mat(whiteMaterial, Color.white);
        distance = 0;
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
        Vector3 forward = (centerline[3] - centerline[1]).normalized;
        Vector3 across = new Vector3(-forward.z, 0, forward.x);
        Vector3 start = centerline[2];

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

        Debug.Log("Track generated. Start cars near " + centerline[2] + " and point toward " + centerline[3]);
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

    Material Mat(Material existing, Color color)
    {
        if(existing!=null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if(shader==null) shader = Shader.Find("Standard");
        Material m = new Material(shader); m.color=color; return m;
    }

    void ClearGenerated()
    {
        for(int i=transform.childCount-1;i>=0;i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if(Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }
}
