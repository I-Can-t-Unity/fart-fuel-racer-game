using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only helper for the StarterCar model. All scene changes support Undo.
public static class CarWheelSetup
{
    private static readonly string[] MeshNames =
    {
        "LeftFrontWheel", "RightFrontWheel", "LeftRearWheel", "RightRearWheel"
    };

    private static readonly string[] ColliderNames =
    {
        "LeftFrontCollider", "RightFrontCollider", "LeftRearCollider", "RightRearCollider"
    };

    private static readonly string[] AlternateColliderNames =
    {
        "FrontLeftCollider", "FrontRightCollider", "RearLeftCollider", "RearRightCollider"
    };

    [MenuItem("Tools/Car/Align Wheel Colliders")]
    private static void Align()
    {
        GameObject car = Selection.activeGameObject;
        if (EditorApplication.isPlayingOrWillChangePlaymode || car == null ||
            EditorUtility.IsPersistent(car) || !car.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Select your car", "Exit Play mode and select PlayerCar in the Hierarchy, then run this command again.", "OK");
            return;
        }

        Transform root = car.transform;
        if ((root.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
        {
            EditorUtility.DisplayDialog("Check car scale", "PlayerCar and its parents need a scale of (1, 1, 1). Resize the visible model child if needed, then try again.", "OK");
            return;
        }

        // Validate all four wheels before changing the scene.
        var bounds = new Bounds[4];
        var existing = new Transform[4];
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < MeshNames.Length; i++)
        {
            Transform wheel = null;
            foreach (Transform child in children)
            {
                if (!string.Equals(child.name, MeshNames[i], StringComparison.OrdinalIgnoreCase))
                    continue;
                if (wheel != null)
                {
                    ShowError("More than one object is named " + MeshNames[i] + ". Select the parent containing only one car.");
                    return;
                }
                wheel = child;
            }

            if (wheel == null || !TryGetBounds(wheel, root, out bounds[i]))
            {
                ShowError("Could not find a wheel mesh named " + MeshNames[i] + " under the selected object. Select PlayerCar and check the model's wheel names.");
                return;
            }

            existing[i] = root.Find(ColliderNames[i]) ?? root.Find(AlternateColliderNames[i]);
            if (existing[i] != null && existing[i].GetComponentsInChildren<Renderer>(true).Length > 0)
            {
                ShowError(ColliderNames[i] + " contains visible geometry. Use an empty object for wheel physics.");
                return;
            }
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align Car Wheel Colliders");
        for (int i = 0; i < MeshNames.Length; i++)
        {
            Transform target = existing[i];
            if (target == null)
            {
                var obj = new GameObject(ColliderNames[i]);
                Undo.RegisterCreatedObjectUndo(obj, "Create Wheel Collider");
                Undo.SetTransformParent(obj.transform, root, "Parent Wheel Collider");
                target = obj.transform;
            }

            WheelCollider collider = target.GetComponent<WheelCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<WheelCollider>(target.gameObject);
                collider.suspensionDistance = 0.2f;
            }

            Undo.RecordObject(target, "Align Wheel Collider");
            Undo.RecordObject(collider, "Size Wheel Collider");
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
            // Wheels roll around the car's X axis. Measure their Y/Z diameter.
            collider.radius = Mathf.Max(bounds[i].extents.y, bounds[i].extents.z);
            collider.center = Vector3.zero;
            // Unity's green wheel gizmo is halfway down the suspension travel.
            target.localPosition = bounds[i].center + Vector3.up * (collider.suspensionDistance * 0.5f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log("Aligned all four wheel colliders on " + car.name + ". Check the green wheel circles with Gizmos enabled. PlayerCar should face local +Z and have one Rigidbody.", car);
    }

    private static bool TryGetBounds(Transform wheel, Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (MeshFilter filter in wheel.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            Bounds meshBounds = filter.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                point = toRoot.MultiplyPoint3x4(point);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        return found && Mathf.Max(bounds.extents.y, bounds.extents.z) > 0.0001f;
    }

    private static void ShowError(string message)
    {
        EditorUtility.DisplayDialog("Wheel setup", message, "OK");
    }
}
