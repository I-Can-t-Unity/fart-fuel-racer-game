using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SuperFartJumpTestSceneSetup
{
    public const string ScenePath = "Assets/Scenes/SuperFartJumpTest.unity";

    [MenuItem("Tools/Super Fart Jump/Create Test Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorSceneManager.OpenScene(ScenePath);
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        const string sourcePath = "Assets/Scenes/LoopRaceScene.unity";
        Scene source = SceneManager.GetSceneByPath(sourcePath);
        bool openedSource = !source.isLoaded;
        if (openedSource) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
        FuelInventory sourceCar = source.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<FuelInventory>(true)).First();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        GameObject player = Object.Instantiate(sourceCar.gameObject);
        player.name = "PlayerCar";
        SceneManager.MoveGameObjectToScene(player, scene);
        player.transform.SetPositionAndRotation(new Vector3(0, .8f, -30), Quaternion.identity);
        foreach (FuelPickupAnalytics analytics in player.GetComponentsInChildren<FuelPickupAnalytics>(true))
            Object.DestroyImmediate(analytics);
        FuelInventory inventory = player.GetComponent<FuelInventory>();
        var driverSettings = new SerializedObject(player.GetComponent<CarDriver>());
        driverSettings.FindProperty("showControls").boolValue = false;
        driverSettings.ApplyModifiedPropertiesWithoutUndo();

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.backgroundColor = new Color(.32f, .49f, .65f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.farClipPlane = 250;
        cameraObject.AddComponent<CarFollowCamera>().SetTarget(player.transform);
        var lightObject = new GameObject("Sun", typeof(Light));
        lightObject.GetComponent<Light>().type = LightType.Directional;
        lightObject.GetComponent<Light>().intensity = 1.4f;
        lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
        RenderSettings.ambientLight = new Color(.55f, .55f, .55f);

        Material road = Material("JumpTestRoad", new Color(.24f, .25f, .28f));
        Material landing = Material("JumpTestLanding", new Color(.2f, .48f, .3f));
        Material marker = Material("JumpTestMarker", new Color(1f, .35f, .7f));
        Cube("Approach - ends at Z 0", new Vector3(0, -.5f, -25), new Vector3(12, 1, 50), road);
        Cube("Landing - starts at Z 14", new Vector3(0, -.5f, 39.5f), new Vector3(18, 1, 51), landing);
        Cube("Launch line", new Vector3(0, .015f, -2), new Vector3(12, .025f, .4f), marker);
        // Gate is a trigger only; it must not physically obstruct the car.
        var gateObject = new GameObject("Jump Gate - forward is +Z", typeof(BoxCollider));
        gateObject.transform.position = new Vector3(0, 2, -2);
        gateObject.GetComponent<BoxCollider>().size = new Vector3(12, 5, 1);
        SuperFartJumpGate gate = gateObject.AddComponent<SuperFartJumpGate>();
        gateObject.AddComponent<SuperFartJumpLauncher>();

        var restart = new GameObject("Recovery Point");
        restart.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
        var area = new GameObject("Jump Test Controls").AddComponent<SuperFartJumpTestArea>();
        area.inventory = inventory;
        area.gate = gate;
        area.restartPoint = restart.transform;

        // With a permanent scene, simple material assets keep colors after reopening.
        EditorSceneManager.SaveScene(scene, ScenePath);
        if (openedSource) EditorSceneManager.CloseScene(source, true);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Selection.activeGameObject = GameObject.Find("Jump Gate - forward is +Z");
        Debug.Log("Created " + ScenePath + ". Press Play, then drive forward. F1 supplies a fresh mix; F2 tests denial.");
    }

    static Material Material(string name, Color color)
    {
        const string folder = "Assets/Materials/JumpTest";
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials", "JumpTest");
        string path = folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Cube(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = material;
    }
}
