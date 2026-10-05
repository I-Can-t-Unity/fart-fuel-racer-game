using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Exercises the actual keyboard input path and scene physics in Play mode.
public static class CarDrivingSmokeTest
{
    private static Keyboard keyboard;
    private static Keyboard previousKeyboard;
    private static CarDriver driver;
    private static Rigidbody body;
    private static WheelCollider[] wheels;
    private static CarBrakeLights lights;
    private static readonly Dictionary<Renderer, Material[]> OriginalMaterials = new Dictionary<Renderer, Material[]>();
    private static Vector3 startPosition;
    private static double phaseStarted;
    private static int phase;
    private static float acceleratedSpeed;
    private static Quaternion turnStartRotation;
    private static float peakYawRate;
    private static float minimumUpright;
    private static float peakSidewaysFraction;
    private static readonly StringBuilder Report = new StringBuilder();
    private static bool passed;

    [MenuItem("Tools/Car/Test Driving (Play Mode)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || keyboard != null)
        {
            Debug.LogWarning("Enter Play mode before running the driving check; only one check can run at a time.");
            return;
        }
        driver = UnityEngine.Object.FindFirstObjectByType<CarDriver>();
        if (driver == null)
        {
            Debug.LogError("No active CarDriver found.");
            return;
        }
        body = driver.GetComponent<Rigidbody>();
        wheels = driver.GetComponentsInChildren<WheelCollider>();
        lights = driver.GetComponent<CarBrakeLights>();
        OriginalMaterials.Clear();
        foreach (Renderer renderer in driver.GetComponentsInChildren<Renderer>())
            OriginalMaterials.Add(renderer, renderer.sharedMaterials);
        startPosition = body.position;
        previousKeyboard = Keyboard.current;
        keyboard = InputSystem.AddDevice<Keyboard>("CarDrivingTestKeyboard");
        phase = 0;
        phaseStarted = EditorApplication.timeSinceStartup;
        passed = true;
        Report.Clear();
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || driver == null)
        {
            Finish(false);
            return;
        }
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - phaseStarted;
            switch (phase)
            {
                case 0:
                    Queue(Key.R);
                    if (elapsed > 0.3) Next();
                    break;
                case 1:
                    Queue();
                    if (elapsed > 1.2)
                    {
                        int grounded = 0;
                        foreach (WheelCollider wheel in wheels) if (wheel.isGrounded) grounded++;
                        Check(grounded == 4, "All four wheels grounded: " + grounded);
                        CheckLights(false, "At rest");
                        Next();
                    }
                    break;
                case 2:
                    Queue(Key.W);
                    if (elapsed > 2)
                    {
                        acceleratedSpeed = driver.SpeedKmh;
                        Check(Vector3.Dot(body.linearVelocity, body.transform.forward) > 1,
                            $"Forward acceleration: {acceleratedSpeed:F1} km/h");
                        Check(Camera.main != null && Vector3.Distance(Camera.main.transform.position, body.position) < 12,
                            "Follow camera stays near the car");
                        CheckLights(false, "Accelerating");
                        Next();
                    }
                    break;
                case 3:
                    Queue(Key.Space);
                    if (elapsed > 3)
                    {
                        Check(driver.SpeedKmh < 1,
                            $"Braking: {acceleratedSpeed:F1} -> {driver.SpeedKmh:F1} km/h");
                        CheckLights(true, "Space brake");
                        Next();
                    }
                    break;
                case 4:
                    Queue(Key.S);
                    if (elapsed > 1.5)
                    {
                        float reverse = Vector3.Dot(body.linearVelocity, body.transform.forward);
                        Check(reverse < -1, $"Reverse velocity: {reverse:F2} m/s");
                        CheckLights(false, "Reversing");
                        Next();
                    }
                    break;
                case 5:
                    Queue(Key.Space, Key.D);
                    if (elapsed > 0.8)
                    {
                        float left = body.transform.Find("LeftFrontCollider").GetComponent<WheelCollider>().steerAngle;
                        float rear = body.transform.Find("LeftRearCollider").GetComponent<WheelCollider>().steerAngle;
                        Check(left > 10 && Mathf.Abs(rear) < 0.01f,
                            $"Front steering: {left:F1} degrees; rear: {rear:F1} degrees");
                        Check(Vector3.Dot(body.transform.up, Vector3.up) > 0.9f, "Car stays upright");
                        Next();
                    }
                    break;
                case 6:
                    Queue(Key.R);
                    if (elapsed > 0.3) Next();
                    break;
                case 7:
                    Queue();
                    if (elapsed > 0.8)
                    {
                        Vector3 displacement = body.position - startPosition;
                        displacement.y = 0;
                        Check(displacement.magnitude < 0.2f && driver.SpeedKmh < 1,
                            $"Reset returns to spawn: horizontal offset {displacement.magnitude:F3} m, {driver.SpeedKmh:F1} km/h");
                        CheckLights(false, "Reset");
                        turnStartRotation = body.rotation;
                        Next();
                    }
                    break;
                case 8:
                    Queue(Key.W, Key.D);
                    if (elapsed > 0.8)
                    {
                        Vector3 localVelocity = body.transform.InverseTransformDirection(body.linearVelocity);
                        float lateralFraction = Mathf.Abs(localVelocity.x) / Mathf.Max(body.linearVelocity.magnitude, 0.1f);
                        Check(Quaternion.Angle(turnStartRotation, body.rotation) > 2,
                            "Car changes heading when steering while accelerating");
                        Check(lateralFraction < 0.3f, $"Cornering sideways velocity fraction: {lateralFraction:F3}");
                        Check(Vector3.Dot(body.transform.up, Vector3.up) > 0.9f, "Car stays upright in a powered turn");
                        Next();
                    }
                    break;
                case 9:
                    Queue(Key.S);
                    if (elapsed > 0.15)
                    {
                        CheckLights(true, "Opposite-direction braking");
                        Next();
                    }
                    break;
                case 10:
                    Queue(Key.R);
                    if (elapsed > 0.3) Next();
                    break;
                case 11:
                    Queue();
                    if (elapsed > 0.8) Next();
                    break;
                case 12:
                    Queue(Key.W);
                    if (elapsed > 2)
                    {
                        Check(driver.SpeedKmh > 40, $"Fast-turn entry speed: {driver.SpeedKmh:F1} km/h");
                        BeginFastTurn();
                        Next();
                    }
                    break;
                case 13:
                    Queue(Key.W, Key.D);
                    SampleFastTurn();
                    if (elapsed > 0.9)
                    {
                        CheckFastTurn("Fast right turn");
                        BeginFastTurn();
                        Next();
                    }
                    break;
                case 14:
                    Queue(Key.W, Key.A);
                    SampleFastTurn();
                    if (elapsed > 0.9)
                    {
                        CheckFastTurn("Fast steering reversal");
                        Next();
                    }
                    break;
                case 15:
                    Queue(Key.R);
                    if (elapsed > 0.3) Next();
                    break;
                case 16:
                    Queue();
                    if (elapsed > 0.8) Finish(passed);
                    break;
            }
        }
        catch (Exception exception)
        {
            Report.AppendLine(exception.ToString());
            Finish(false);
        }
    }

    private static void Queue(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

    private static void Next()
    {
        phase++;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void Check(bool condition, string message)
    {
        passed &= condition;
        Report.AppendLine((condition ? "PASS " : "FAIL ") + message);
    }

    private static void BeginFastTurn()
    {
        turnStartRotation = body.rotation;
        peakYawRate = 0;
        minimumUpright = 1;
        peakSidewaysFraction = 0;
    }

    private static void SampleFastTurn()
    {
        peakYawRate = Mathf.Max(peakYawRate, Mathf.Abs(Vector3.Dot(body.angularVelocity, body.transform.up)));
        minimumUpright = Mathf.Min(minimumUpright, Vector3.Dot(body.transform.up, Vector3.up));
        Vector3 localVelocity = body.transform.InverseTransformDirection(body.linearVelocity);
        peakSidewaysFraction = Mathf.Max(peakSidewaysFraction,
            Mathf.Abs(localVelocity.x) / Mathf.Max(body.linearVelocity.magnitude, 0.1f));
    }

    private static void CheckFastTurn(string context)
    {
        float headingChange = Quaternion.Angle(turnStartRotation, body.rotation);
        Check(headingChange < 65 && peakYawRate < 1.2f,
            $"{context}: heading change {headingChange:F1} degrees; peak yaw {peakYawRate:F2} rad/s");
        Check(minimumUpright > 0.85f, $"{context}: upright fraction {minimumUpright:F3}");
        Check(peakSidewaysFraction < 0.35f, $"{context}: sideways velocity fraction {peakSidewaysFraction:F3}");
        float frontAngle = Mathf.Abs(body.transform.Find("LeftFrontCollider").GetComponent<WheelCollider>().steerAngle);
        Check(driver.SpeedKmh > 40 && frontAngle < 6,
            $"{context}: {driver.SpeedKmh:F1} km/h, front wheel angle {frontAngle:F1} degrees");
    }

    private static void CheckLights(bool expected, string context)
    {
        Check(driver.IsBraking == expected, context + ": braking state " + driver.IsBraking);
        if (lights == null)
            return;
        var serialized = new SerializedObject(lights);
        Material material = serialized.FindProperty(expected ? "taillightOn" : "taillightOff").objectReferenceValue as Material;
        SerializedProperty targets = serialized.FindProperty("surfaces");
        var targetedSlots = new Dictionary<Renderer, HashSet<int>>();
        bool matching = targets.arraySize > 0;
        for (int i = 0; i < targets.arraySize; i++)
        {
            SerializedProperty target = targets.GetArrayElementAtIndex(i);
            Renderer renderer = target.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
            int slot = target.FindPropertyRelative("materialSlot").intValue;
            if (renderer == null || slot >= renderer.sharedMaterials.Length)
            {
                matching = false;
                continue;
            }
            matching &= renderer.sharedMaterials[slot] == material;
            if (!targetedSlots.ContainsKey(renderer)) targetedSlots.Add(renderer, new HashSet<int>());
            targetedSlots[renderer].Add(slot);
        }
        Check(lights.LightsOn == expected && matching, context + ": correct taillight material");
        bool preserved = true;
        foreach (var entry in OriginalMaterials)
        {
            Material[] current = entry.Key.sharedMaterials;
            preserved &= current.Length == entry.Value.Length;
            for (int i = 0; i < Mathf.Min(current.Length, entry.Value.Length); i++)
            {
                if (targetedSlots.TryGetValue(entry.Key, out HashSet<int> slots) && slots.Contains(i)) continue;
                preserved &= current[i] == entry.Value[i];
            }
        }
        Check(preserved, context + ": other materials preserved");
    }

    private static void Finish(bool success)
    {
        EditorApplication.update -= Tick;
        if (keyboard != null)
            InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        if (previousKeyboard != null && previousKeyboard.added)
            previousKeyboard.MakeCurrent();
        Directory.CreateDirectory("Temp");
        string result = (success ? "PASS" : "FAIL") + "\n" + Report;
        File.WriteAllText("Temp/CarDrivingSmokeTest.txt", result);
        if (success) Debug.Log("Driving check passed.\n" + Report);
        else Debug.LogError("Driving check failed.\n" + Report);
    }
}
