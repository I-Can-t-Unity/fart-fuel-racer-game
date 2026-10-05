using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarBrakeLightSetup
{
    [MenuItem("Tools/Car/Set Up Brake Lights")]
    private static void SetUp()
    {
        GameObject car = Selection.activeGameObject;
        if (EditorApplication.isPlayingOrWillChangePlaymode || car == null ||
            EditorUtility.IsPersistent(car) || !car.scene.IsValid() || car.GetComponent<CarDriver>() == null)
        {
            EditorUtility.DisplayDialog("Select PlayerCar", "Exit Play mode and select PlayerCar, which has the Car Driver component.", "OK");
            return;
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("The Universal Render Pipeline/Lit shader was not found.");
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        Material off = GetMaterial("TaillightOff", shader, false);
        Material on = GetMaterial("TaillightOn", shader, true);
        var targets = new List<CarBrakeLights.Surface>();
        var report = new StringBuilder();
        foreach (Renderer renderer in car.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                string materialName = materials[slot] == null ? "(empty)" : materials[slot].name;
                report.AppendLine(renderer.name + " / Element " + slot + ": " + materialName);
                if (IsTaillight(materialName) || (materials.Length == 1 && IsTaillight(renderer.name)))
                    targets.Add(new CarBrakeLights.Surface { renderer = renderer, materialSlot = slot });
            }
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up Brake Lights");
        CarBrakeLights lights = car.GetComponent<CarBrakeLights>() ?? Undo.AddComponent<CarBrakeLights>(car);
        // Preserve manually selected slots on subsequent setup runs.
        var serialized = new SerializedObject(lights);
        if (targets.Count == 0 && serialized.FindProperty("surfaces").arraySize > 0)
        {
            Debug.Log("Kept the existing brake-light setup.\n" + report, car);
            return;
        }
        Undo.RecordObject(lights, "Connect Brake Lights");
        lights.Configure(car.GetComponent<CarDriver>(), off, on, targets.ToArray());
        foreach (CarBrakeLights.Surface target in targets)
        {
            Undo.RecordObject(target.renderer, "Assign Off Material");
            Material[] materials = target.renderer.sharedMaterials;
            materials[target.materialSlot] = off;
            target.renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(target.renderer);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(lights);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(car.scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/CarBrakeLightSetup.txt", "Matched taillight slots: " + targets.Count + "\n" + report);
        Debug.Log(targets.Count > 0
            ? "Brake lights ready. Connected " + targets.Count + " material slot(s)."
            : "Brake-light script and materials are ready. Give the taillight faces a material named Taillights in Blender, re-export, then run Set Up Brake Lights again.\n" + report, car);
    }

    private static bool IsTaillight(string name)
    {
        string normalized = name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        return normalized.Contains("taillight") || normalized.Contains("brakelight") || normalized.Contains("rearlight");
    }

    private static Material GetMaterial(string name, Shader shader, bool illuminated)
    {
        string path = "Assets/Materials/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;
        // The FBX may already contain the user's Blender material. Reuse it.
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Cars/StarterCar.fbx"))
        {
            if (asset is Material imported && imported.name == name)
                return imported;
        }
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", illuminated ? new Color(0.9f, 0.015f, 0.01f) : new Color(0.18f, 0.008f, 0.005f));
        material.SetFloat("_Smoothness", 0.6f);
        if (illuminated)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(3f, 0.025f, 0.015f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
