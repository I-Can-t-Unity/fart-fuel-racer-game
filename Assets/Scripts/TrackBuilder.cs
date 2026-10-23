using UnityEngine;

public class TrackBuilder : MonoBehaviour
{
    public int segmentCount = 22;
    public float segmentLength = 12f;
    public float roadWidth = 12f;
    public float curveAmplitude = 6f;
    public int seed = 12;
    public Transform finishLine;
    public Vector3 startPosition => new Vector3(0, 0.5f, 2f);

    Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    GameObject Cube(string label, Vector3 pos, Vector3 scale, Material material, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = label;
        obj.transform.SetParent(parent);
        obj.transform.position = pos;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    void Start()
    {
        var asphalt = MakeMaterial(new Color(.18f,.20f,.24f));
        var stripe = MakeMaterial(Color.white);
        var ground = MakeMaterial(new Color(.5f,.5f,.5f));
        Cube("Ground terrain", new Vector3(0,-.6f,segmentCount*segmentLength/2f), new Vector3(150,1,segmentCount*segmentLength+100), ground,transform);
        for (int i=0;i<segmentCount;i++)
        {
            float z = i*segmentLength+segmentLength/2f;
            float x = curveAmplitude*Mathf.Sin(i*.22f);
            Cube("Road segment " + i,new Vector3(x,0,z),new Vector3(roadWidth,.25f,segmentLength+.1f),asphalt,transform);
            if(i%2==0) Cube("Center dash",new Vector3(x,.15f,z),new Vector3(.22f,.03f,4),stripe,transform);
            foreach (int side in new int[]{-1,1})
            {
                Cube("Edge line",new Vector3(x+side*(roadWidth/2-.35f),.15f,z),new Vector3(.16f,.03f,segmentLength),stripe,transform);
            }
        }
        float finishZ=segmentCount*segmentLength-6;
        float finishX=curveAmplitude*Mathf.Sin((segmentCount-1)*.22f);
        var finish=Cube("Finish Line Trigger",new Vector3(finishX,.8f,finishZ),new Vector3(roadWidth,1.5f,1),stripe,transform);
        finish.GetComponent<Renderer>().enabled=false;
        finish.GetComponent<BoxCollider>().isTrigger=true;
        finish.AddComponent<FinishLine>();
        finishLine=finish.transform;
        for(int j=0;j<10;j++)
        {
            var mat=(j%2==0)?stripe:asphalt;
            Cube("Checkered tile",new Vector3(finishX-roadWidth/2+(j+.5f)*roadWidth/10,.16f,finishZ),new Vector3(roadWidth/10,.03f,1),mat,transform);
        }
    }
}
