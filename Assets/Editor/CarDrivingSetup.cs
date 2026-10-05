using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarDrivingSetup
{
    [MenuItem("Tools/Car/Use Grippy Tires")]
    private static void SetGrip()
    {
        GameObject car = Selection.activeGameObject;
        if (EditorApplication.isPlayingOrWillChangePlaymode || car == null ||
            EditorUtility.IsPersistent(car) || !car.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Select PlayerCar", "Exit Play mode and select PlayerCar in the Hierarchy.", "OK");
            return;
        }
        WheelCollider[] tires = car.GetComponentsInChildren<WheelCollider>(true);
        if (tires.Length != 4)
        {
            EditorUtility.DisplayDialog("Select PlayerCar", "Select the car parent containing all four WheelColliders.", "OK");
            return;
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Increase Tire Grip");
        foreach (WheelCollider tire in tires)
        {
            Undo.RecordObject(tire, "Increase Tire Grip");
            WheelFrictionCurve forward = tire.forwardFriction;
            forward.stiffness = 1.5f;
            tire.forwardFriction = forward;
            WheelFrictionCurve sideways = tire.sidewaysFriction;
            sideways.stiffness = 1.8f;
            tire.sidewaysFriction = sideways;
            PrefabUtility.RecordPrefabInstancePropertyModifications(tire);
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log("Updated all four tires: forward grip 1.5, sideways grip 1.8. Suspension settings preserved.", car);
    }

    [MenuItem("Tools/Car/Set Up Driving")]
    private static void SetUp()
    {
        GameObject car = Selection.activeGameObject;
        if (EditorApplication.isPlayingOrWillChangePlaymode || car == null ||
            EditorUtility.IsPersistent(car) || !car.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Select PlayerCar", "Exit Play mode and select PlayerCar in the Hierarchy.", "OK");
            return;
        }
        string[] names = { "LeftFront", "RightFront", "LeftRear", "RightRear" };
        string[] alternateNames = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
        Transform[] children = car.GetComponentsInChildren<Transform>(true);
        var wheels = new CarDriver.Wheel[4];
        for (int i = 0; i < wheels.Length; i++)
        {
            Transform mesh = children.FirstOrDefault(t => t.name == names[i] + "Wheel");
            Transform physics = car.transform.Find(names[i] + "Collider")
                ?? car.transform.Find(alternateNames[i] + "Collider");
            WheelCollider collider = physics == null ? null : physics.GetComponent<WheelCollider>();
            if (mesh == null || collider == null)
            {
                EditorUtility.DisplayDialog("Missing wheel", "Could not find " + names[i] + "Wheel and its WheelCollider. Run Align Wheel Colliders first.", "OK");
                return;
            }
            wheels[i] = new CarDriver.Wheel { collider = collider, mesh = mesh, steering = i < 2 };
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up Car Driving");
        Rigidbody body = car.GetComponent<Rigidbody>() ?? Undo.AddComponent<Rigidbody>(car);
        Undo.RecordObject(body, "Configure Car Physics");
        body.mass = 1000;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.isKinematic = false;
        body.useGravity = true;

        CarDriver driver = car.GetComponent<CarDriver>() ?? Undo.AddComponent<CarDriver>(car);
        Undo.RecordObject(driver, "Connect Wheels");
        driver.ConfigureWheels(wheels);

        // Keep the chassis box above the tire contact patches so it cannot hold
        // the car up instead of the suspension. Reuse the user's existing box.
        BoxCollider box = car.GetComponent<BoxCollider>() ?? Undo.AddComponent<BoxCollider>(car);
        Undo.RecordObject(box, "Adjust Chassis Collider");
        float bottom = wheels.Average(w => car.transform.InverseTransformPoint(w.mesh.position).y
            - w.collider.radius * 0.25f);
        float top = Mathf.Max(box.center.y + box.size.y * 0.5f, bottom + 0.5f);
        box.center = new Vector3(box.center.x, (top + bottom) * 0.5f, box.center.z);
        box.size = new Vector3(box.size.x, top - bottom, box.size.z);

        foreach (CarDriver.Wheel wheel in wheels)
        {
            Undo.RecordObject(wheel.collider, "Tune Suspension");
            Undo.RecordObject(wheel.collider.transform, "Align Suspension");
            wheel.collider.suspensionDistance = 0.2f;
            JointSpring spring = wheel.collider.suspensionSpring;
            spring.spring = 35000;
            spring.damper = 4500;
            spring.targetPosition = 0.5f;
            wheel.collider.suspensionSpring = spring;
            // Align nominal centers after standardizing suspension travel.
            wheel.collider.transform.position = wheel.mesh.position
                + car.transform.up * (wheel.collider.suspensionDistance * 0.5f);
        }

        Camera camera = Camera.main;
        if (camera != null && camera.gameObject.scene == car.scene)
        {
            CarFollowCamera follow = camera.GetComponent<CarFollowCamera>()
                ?? Undo.AddComponent<CarFollowCamera>(camera.gameObject);
            Undo.RecordObject(follow, "Connect Follow Camera");
            follow.SetTarget(car.transform);
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log("Driving ready: WASD or arrows to drive, Space to brake, R to reset. Click the Game view after pressing Play.", car);
    }
}
