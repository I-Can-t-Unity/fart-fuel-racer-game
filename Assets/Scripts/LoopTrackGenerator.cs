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

    public enum Layout
    {
        Classic,
        [InspectorName("Level 1 - Bridge Circuit")] BridgeCircuit,
        [InspectorName("Level 2 - Figure Eight")] FigureEight,
        [InspectorName("Level 3 - Flyover")] Flyover,
        [InspectorName("Level 4 - Whirlpool")] Whirlpool,
        [InspectorName("Level 5 - Sky Helix")] SkyHelix,
        Tutorial
    }

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

    static readonly Vector2[] tutorialAnchors = {
        new Vector2(-95, -55), new Vector2(0, -55), new Vector2(75, -55),
        new Vector2(120, -35), new Vector2(140, 5), new Vector2(125, 46),
        new Vector2(85, 65), new Vector2(15, 65), new Vector2(-60, 65),
        new Vector2(-112, 47), new Vector2(-135, 5), new Vector2(-125, -34)
    };

    // LEVEL 1 - Bridge Circuit (EASY, ~1.41 km). Start-straight chicane, right sweeper, the arch,
    // top hairpin loop, S-bends through the middle, left S-bend before home.
    // 18 corners under 35 m radius, longest straight ~110 m, tightest corner 13.5 m radius.
    static readonly Vector2[] bridgeAnchors = {
        new Vector2(-110,-110), new Vector2(0,-118), new Vector2(38,-110), new Vector2(72,-124),
        new Vector2(106,-110), new Vector2(140,-85), new Vector2(160,-40), new Vector2(160,-5),
        new Vector2(200,20), new Vector2(225,55), new Vector2(200,90), new Vector2(160,115),
        new Vector2(158,150), new Vector2(130,185), new Vector2(80,192), new Vector2(35,180),
        new Vector2(20,150), new Vector2(40,125), new Vector2(85,115), new Vector2(105,85),
        new Vector2(90,50), new Vector2(40,30), new Vector2(-10,50), new Vector2(-40,90),
        new Vector2(-90,105), new Vector2(-140,85), new Vector2(-162,45), new Vector2(-152,10),
        new Vector2(-174,-28), new Vector2(-162,-62), new Vector2(-150,-85)
    };

    // LEVEL 2 - Figure Eight (MEDIUM, ~1.56 km). Anchors are (x, z, HEIGHT). Ramp up and OVER the
    // middle on a bridge, right lobe = snake with a 180 hairpin, back UNDER the bridge, left loop
    // (tunnel), left-right flick into the finish. Bridge, ramps and tunnel area are unchanged.
    static readonly Vector3[] figureEightAnchors = {
        new Vector3(-150,-95,0), new Vector3(-110,-98,0), new Vector3(-82,-87,0.2f), new Vector3(-52,-60,2.6f),
        new Vector3(-25,-31,6.1f), new Vector3(0,0,6.6f), new Vector3(25,31,6.1f), new Vector3(52,60,2.6f),
        new Vector3(82,87,0.2f), new Vector3(115,100,0), new Vector3(165,110,0), new Vector3(212,100,0),
        new Vector3(228,75,0), new Vector3(210,54,0), new Vector3(175,53,0), new Vector3(155,52,0),
        new Vector3(137.3f,44.7f,0), new Vector3(130,27,0), new Vector3(137.3f,9.3f,0), new Vector3(155,2,0),
        new Vector3(185,-5,0), new Vector3(225,-8,0), new Vector3(245,-35,0), new Vector3(235,-72,0),
        new Vector3(200,-88,0), new Vector3(160,-102,0), new Vector3(105,-98,0), new Vector3(55,-68,0),
        new Vector3(22,-28,0), new Vector3(0,0,0), new Vector3(-22,28,0), new Vector3(-55,68,0),
        new Vector3(-100,96,0), new Vector3(-150,104,0), new Vector3(-200,86,0), new Vector3(-232,40,0),
        new Vector3(-232,-28,0), new Vector3(-224,-48,0), new Vector3(-214,-74,0)
    };

    // LEVEL 3 - Flyover (HARD, ~2.1 km). Start straight + JUMP GAP, right side = hairpin snake,
    // top straight (tunnel), inner loop with a chicane, up and over the flyover, far-left home.
    // Start/jump, tunnel and flyover are unchanged.
    static readonly Vector3[] flyoverAnchors = {
        new Vector3(-150,-142,0), new Vector3(-90,-150,0), new Vector3(-30,-152,0), new Vector3(30,-152,0),
        new Vector3(90,-148,0), new Vector3(140,-134,0), new Vector3(185,-118,0), new Vector3(228,-104,0),
        new Vector3(246,-82,0), new Vector3(236,-58,0), new Vector3(205,-52,0), new Vector3(175,-50,0),
        new Vector3(158,-50,0), new Vector3(141.7f,-43.3f,0), new Vector3(135,-27,0), new Vector3(141.7f,-10.7f,0),
        new Vector3(158,-4,0), new Vector3(205,-5,0), new Vector3(240,15,0), new Vector3(242,50,0),
        new Vector3(215,75,0), new Vector3(190,122,0), new Vector3(120,150,0), new Vector3(30,160,0),
        new Vector3(-60,155,0), new Vector3(-130,135,0), new Vector3(-175,100,0), new Vector3(-185,55,0),
        new Vector3(-180,10,0), new Vector3(-155,-35,0), new Vector3(-120,-58,0), new Vector3(-88,-47,0),
        new Vector3(-58,-58,0), new Vector3(-24,-51,0), new Vector3(8,-32,0), new Vector3(24,2,0),
        new Vector3(8,34,0), new Vector3(-20,55,0), new Vector3(-65,62,0), new Vector3(-115,60,3.4f),
        new Vector3(-160,50,6.6f), new Vector3(-200,32,6.8f), new Vector3(-238,5,6.4f), new Vector3(-262,-38,2.4f),
        new Vector3(-255,-95,0), new Vector3(-215,-132,0)
    };

    // LEVEL 4 - Whirlpool (~1.9 km). A double spiral: wind INTO the centre on one arm (corners keep
    // getting tighter), flick through an S-bend in the middle, then wind back OUT on the other arm
    // (corners keep opening up), hairpin, repeat. No crossings, no straights - you are always turning.
    static readonly Vector3[] whirlpoolAnchors = {
        new Vector3(-117.6f,56,0), new Vector3(-99.1f,79.3f,0), new Vector3(-75.7f,97.5f,0), new Vector3(-48.5f,109.7f,0),
        new Vector3(-19.3f,114.7f,0), new Vector3(10.3f,112.1f,0), new Vector3(38.1f,101.8f,0), new Vector3(62,84.3f,0),
        new Vector3(80,60.9f,0), new Vector3(90.3f,33.2f,0), new Vector3(91.6f,3.7f,0), new Vector3(83.4f,-24.6f,0),
        new Vector3(66.1f,-48.4f,0), new Vector3(41.5f,-64.4f,0), new Vector3(12.8f,-69.8f,0), new Vector3(-15.5f,-62.9f,0),
        new Vector3(-37.6f,-44.3f,0), new Vector3(-52,-8,0), new Vector3(-38,20,0), new Vector3(-12,16,0),
        new Vector3(12,-16,0), new Vector3(38,-20,0), new Vector3(52,8,0), new Vector3(40.3f,40.3f,0),
        new Vector3(16.5f,62.4f,0), new Vector3(-14.6f,69.8f,0), new Vector3(-45.4f,62.7f,0), new Vector3(-70.5f,43.9f,0),
        new Vector3(-86.6f,17.1f,0), new Vector3(-92.2f,-13.4f,0), new Vector3(-87.4f,-44,0), new Vector3(-73.2f,-71.4f,0),
        new Vector3(-51.5f,-93.3f,0), new Vector3(-24.5f,-107.9f,0), new Vector3(5.5f,-114.4f,0), new Vector3(36.1f,-112.7f,0),
        new Vector3(65.2f,-103.2f,0), new Vector3(90.9f,-86.7f,0), new Vector3(111.8f,-64.5f,0), new Vector3(126.9f,-37.9f,0),
        new Vector3(135.5f,-8.7f,0), new Vector3(137.2f,21.7f,0), new Vector3(132.3f,51.8f,0), new Vector3(121.1f,80,0),
        new Vector3(104.2f,105.3f,0), new Vector3(82.5f,126.6f,0), new Vector3(56.9f,143,0), new Vector3(28.7f,154.1f,0),
        new Vector3(-1.2f,159.5f,0), new Vector3(-31.5f,159.2f,0), new Vector3(-61.2f,153.2f,0), new Vector3(-89.3f,141.8f,0),
        new Vector3(-114.9f,125.5f,0), new Vector3(-137.2f,105,0), new Vector3(-155.5f,80.9f,0), new Vector3(-169.4f,54,0),
        new Vector3(-178.4f,25.1f,0), new Vector3(-182.2f,0,0), new Vector3(-177.9f,-13.4f,0), new Vector3(-166.5f,-21.7f,0),
        new Vector3(-152.4f,-21.7f,0), new Vector3(-141,-13.4f,0), new Vector3(-136.7f,0,0), new Vector3(-130.3f,29.1f,0)
    };

    // LEVEL 5 - Sky Helix (~2.1 km). Ground road snakes across the middle, then a corkscrew climbs
    // 2.5 turns to 17.5 m, a skyway twists back across the map OVER the ground road, and a second
    // corkscrew spirals you back down. Pillars and tunnels are placed by hand.
    static readonly Vector3[] skyHelixAnchors = {
        new Vector3(-165,-32,0), new Vector3(-120,-40,0), new Vector3(-85,-25,0), new Vector3(-70,20,0),
        new Vector3(-40,70,0), new Vector3(10,85,0), new Vector3(50,60,0), new Vector3(60,10,0),
        new Vector3(100,-20,0), new Vector3(140,-60,0), new Vector3(180,-42,0), new Vector3(210,-30,0.25f),
        new Vector3(231.2f,-21.2f,1.11f), new Vector3(240,0,1.98f), new Vector3(231.2f,21.2f,2.84f), new Vector3(210,30,3.7f),
        new Vector3(188.8f,21.2f,4.56f), new Vector3(180,0,5.42f), new Vector3(188.8f,-21.2f,6.29f), new Vector3(210,-30,7.15f),
        new Vector3(231.2f,-21.2f,8.01f), new Vector3(240,0,8.88f), new Vector3(231.2f,21.2f,9.74f), new Vector3(210,30,10.6f),
        new Vector3(188.8f,21.2f,11.46f), new Vector3(180,0,12.32f), new Vector3(188.8f,-21.2f,13.19f), new Vector3(210,-30,14.05f),
        new Vector3(231.2f,-21.2f,14.91f), new Vector3(240,0,15.78f), new Vector3(231.2f,21.2f,16.64f), new Vector3(210,30,17.5f),
        new Vector3(165,32,17.5f), new Vector3(120,25,17.5f), new Vector3(85,-15,17.5f), new Vector3(60,-75,17.5f),
        new Vector3(15,-110,17.5f), new Vector3(-28,-88,17.5f), new Vector3(-18,-38,17.5f), new Vector3(-40,10,17.5f),
        new Vector3(-80,45,17.5f), new Vector3(-120,60,17.5f), new Vector3(-160,40,17.5f), new Vector3(-210,30,17.5f),
        new Vector3(-231.2f,21.2f,16.64f), new Vector3(-240,0,15.78f), new Vector3(-231.2f,-21.2f,14.91f), new Vector3(-210,-30,14.05f),
        new Vector3(-188.8f,-21.2f,13.19f), new Vector3(-180,0,12.32f), new Vector3(-188.8f,21.2f,11.46f), new Vector3(-210,30,10.6f),
        new Vector3(-231.2f,21.2f,9.74f), new Vector3(-240,0,8.88f), new Vector3(-231.2f,-21.2f,8.01f), new Vector3(-210,-30,7.15f),
        new Vector3(-188.8f,-21.2f,6.29f), new Vector3(-180,0,5.43f), new Vector3(-188.8f,21.2f,4.56f), new Vector3(-210,30,3.7f),
        new Vector3(-231.2f,21.2f,2.84f), new Vector3(-240,0,1.97f), new Vector3(-231.2f,-21.2f,1.11f), new Vector3(-210,-30,0.25f)
    };

    [Header("Jump gap (Level 3 only) - centre-line point numbers")]
    [Tooltip("Ramp starts rising here.")] public int jumpRampStart = 26;
    [Tooltip("Top of the ramp; the road stops here.")] public int jumpLip = 31;
    [Tooltip("The road starts again here (landing ramp).")] public int jumpLanding = 33;
    [Tooltip("Landing ramp is back down at road level here.")] public int jumpLandingEnd = 39;
    public float jumpHeight = 2.5f;

    // Anchors as (x, z, height) for every layout.
    Vector3[] anchors
    {
        get
        {
            if (layout == Layout.FigureEight) return figureEightAnchors;
            if (layout == Layout.Flyover) return flyoverAnchors;
            if (layout == Layout.Whirlpool) return whirlpoolAnchors;
            if (layout == Layout.SkyHelix) return skyHelixAnchors;
            Vector2[] flat = layout == Layout.Tutorial ? tutorialAnchors
                : layout == Layout.Classic ? classicAnchors : bridgeAnchors;
            var a = new Vector3[flat.Length];
            for (int i = 0; i < flat.Length; i++) a[i] = new Vector3(flat[i].x, flat[i].y, 0f);
            return a;
        }
    }

    bool HasJump => layout == Layout.Flyover;
    // Segment i (point i to i+1) has no road: the jump gap.
    bool InGap(int i) => HasJump && i >= jumpLip && i < jumpLanding;

    void Start() { Generate(); }

    [ContextMenu("Regenerate Track")]
    public void Generate()
    {
        ClearGenerated();
        centerline.Clear();
        Vector3[] A = anchors;
        for (int i = 0; i < A.Length; i++)
        {
            Vector3 p0 = A[(i - 1 + A.Length) % A.Length];
            Vector3 p1 = A[i];
            Vector3 p2 = A[(i + 1) % A.Length];
            Vector3 p3 = A[(i + 2) % A.Length];
            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;
                Vector3 p = 0.5f * ((2f * p1) + (-p0 + p2) * t +
                    (2f*p0 - 5f*p1 + 4f*p2 - p3) * t*t +
                    (-p0 + 3f*p1 - 3f*p2 + p3) * t*t*t);
                // p = (x, z, height)
                centerline.Add(new Vector3(p.x * scale, surfaceY + Mathf.Max(0f, p.z) * scale, p.y * scale));
            }
        }

        // Level 3 jump: straight ramp up to the lip, gap, landing ramp back down.
        if (HasJump)
            for (int i = jumpRampStart; i <= jumpLandingEnd && i < centerline.Count; i++)
            {
                float h;
                if (i <= jumpLip) h = jumpHeight * (i - jumpRampStart) / (float)(jumpLip - jumpRampStart);
                else if (i < jumpLanding) h = jumpHeight;
                else h = (jumpHeight - 0.5f) * (1f - (i - jumpLanding) / (float)(jumpLandingEnd - jumpLanding));
                Vector3 c = centerline[i];
                centerline[i] = new Vector3(c.x, surfaceY + h, c.z);
            }

        // Levels 2 and 3 only work in their designed direction.
        if (reverseDirection && (layout == Layout.Classic || layout == Layout.BridgeCircuit)) centerline.Reverse();
        FitGround();

        int n = centerline.Count;
        Vector3[] left = new Vector3[n], right = new Vector3[n];
        Vector3[] sides = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tangent = centerline[(i+1)%n] - centerline[(i-1+n)%n];
            tangent.y = 0f; tangent.Normalize();
            Vector3 side = new Vector3(-tangent.z, 0, tangent.x);
            sides[i] = side;
            left[i] = centerline[i] - side * roadWidth * .5f;
            right[i] = centerline[i] + side * roadWidth * .5f;
        }

        Mesh roadMesh = MakeStrip(left, right, 0f, false);
        GetComponent<MeshFilter>().sharedMesh = roadMesh;
        GetComponent<MeshCollider>().sharedMesh = null;
        GetComponent<MeshCollider>().sharedMesh = roadMesh;
        GetComponent<MeshRenderer>().sharedMaterial = Mat(roadMaterial, new Color(.29f,.30f,.32f));

        // Underside of the road, so bridges look solid when you drive under them.
        var underL = new Vector3[n]; var underR = new Vector3[n];
        for (int i = 0; i < n; i++) { underL[i] = left[i] + Vector3.down * 0.6f; underR[i] = right[i] + Vector3.down * 0.6f; }
        GameObject under = new GameObject("RoadUnderside");
        under.transform.SetParent(transform, false);
        under.AddComponent<MeshFilter>().sharedMesh = MakeStrip(underL, underR, 0f, true);
        under.AddComponent<MeshRenderer>().sharedMaterial = Mat(null, new Color(.22f,.22f,.24f));

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
                if (InGap(i)) continue;
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
            if (!InGap(i) && Mathf.FloorToInt(distance / 2.8f) % 2 == 0)
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
        Vector3 forward = GetPoint(startIndex + 1) - GetPoint(startIndex - 1);
        forward.y = 0f; forward.Normalize();
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


    Mesh MakeStrip(Vector3[] a, Vector3[] b, float height) => MakeStrip(a, b, height, false);

    // faceDown = visible from below (the bridge underside). Gap segments are left out.
    Mesh MakeStrip(Vector3[] a, Vector3[] b, float height, bool faceDown)
    {
        int count = a.Length;
        bool closed = count > 2;
        int segments = closed ? count : count - 1;

        Vector3[] verts = new Vector3[count * 2];
        Vector2[] uv = new Vector2[count * 2];
        var tris = new List<int>(segments * 6);

        for (int i = 0; i < count; i++)
        {
            verts[i * 2] = a[i] + Vector3.up * height;
            verts[i * 2 + 1] = b[i] + Vector3.up * height;

            uv[i * 2] = new Vector2(0, i);
            uv[i * 2 + 1] = new Vector2(1, i);
        }

        for (int i = 0; i < segments; i++)
        {
            if (closed && InGap(i)) continue;   // the jump gap (only on the main road strip)
            int x = i * 2;
            int y = ((i + 1) % count) * 2;

            // Single-sided triangles facing upward (or downward for the underside).
            if (!faceDown) tris.AddRange(new[] { x, x + 1, y, x + 1, y + 1, y });
            else tris.AddRange(new[] { x, y, x + 1, x + 1, y, y + 1 });
        }

        Mesh mesh = new Mesh();
        mesh.name = "Track Strip";
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.SetTriangles(tris, 0);

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
