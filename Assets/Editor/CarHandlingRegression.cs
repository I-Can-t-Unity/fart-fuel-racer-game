using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run in a disposable copy of the project with -batchmode -nographics
// -executeMethod CarHandlingRegression.RunBatch. Does not save scenes or assets.
public static class CarHandlingRegression
{
    private static readonly MethodInfo AwakeDriver = typeof(CarDriver).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo TickDriver = typeof(CarDriver).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly StringBuilder Report = new StringBuilder();
    private static bool passed = true;
    private const float Dt = 0.02f;
    private static GameObject template;

    public static void RunBatch() => Run(false);
    public static void RunBarrierBatch() => Run(true);

    private static void Run(bool barriersOnly)
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run this check in a disposable batch-mode project copy.");
        var previousSimulation = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            EditorSceneManager.OpenScene("Assets/Scenes/LoopRaceScene.unity");
            template = UnityEngine.Object.FindObjectsByType<CarDriver>(FindObjectsSortMode.None)
                .First(car => car.name == "PlayerCar").gameObject;
            template = UnityEngine.Object.Instantiate(template);
            template.name = "HandlingTemplate";
            template.SetActive(false);
            Scene sourceScene = template.scene;
            Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(template, testScene);
            EditorSceneManager.CloseScene(sourceScene, true);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Flat test road";
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(2000, 1, 2000);
            if (barriersOnly)
                TestBarriers();
            else
            {
                TestTurn(50f);
                TestTurn(70f);
                TestTurn(100f);
                TestSideSwipe();
                TestDoorContact();
                TestRearImpact();
                TestAirborne();
                TestAIContactRecovery();
                TestBarriers();
                TestBarrierBlockingAndLanding();
            }
        }
        catch (Exception error)
        {
            passed = false;
            Report.AppendLine(error.ToString());
        }
        finally
        {
            Physics.simulationMode = previousSimulation;
            if (template != null) UnityEngine.Object.DestroyImmediate(template);
            Directory.CreateDirectory("Library");
            File.WriteAllText("Library/car-handling-regression.txt", Report.ToString());
            Debug.Log(Report.ToString());
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    private static CarDriver Spawn(Vector3 position)
    {
        GameObject car = UnityEngine.Object.Instantiate(template);
        car.transform.SetPositionAndRotation(position, Quaternion.identity);
        foreach (MonoBehaviour script in car.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(script is CarDriver)) script.enabled = false;
        car.SetActive(true);
        CarDriver driver = car.GetComponent<CarDriver>();
        AwakeDriver.Invoke(driver, null);
        driver.ExternalInput = true;
        Physics.SyncTransforms();
        return driver;
    }

    private static void Step(CarDriver[] cars, float throttle, float steer, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            foreach (CarDriver car in cars)
            {
                car.SetInput(throttle, steer, false);
                TickDriver.Invoke(car, null);
            }
            Physics.Simulate(Dt);
        }
    }

    private static void Check(bool condition, string message)
    {
        passed &= condition;
        Report.AppendLine((condition ? "PASS " : "FAIL ") + message);
    }

    private static void TestTurn(float speedKmh)
    {
        CarDriver car = Spawn(new Vector3(0, 0.6f, 0));
        Rigidbody body = car.GetComponent<Rigidbody>();
        var cars = new[] { car };
        Step(cars, 0, 0, 100);
        Check(car.GetComponentsInChildren<WheelCollider>().Count(w => w.isGrounded) == 4, "Four wheels grounded before turn");
        body.linearVelocity = Vector3.forward * speedKmh / 3.6f;
        float peakSlip = 0f, peakYaw = 0f, upright = 1f, firstTurnHeading = 0f;
        for (int i = 0; i < 100; i++)
        {
            Step(cars, 1, i < 50 ? 1 : -1, 1);
            Vector3 local = car.transform.InverseTransformDirection(body.linearVelocity);
            peakSlip = Mathf.Max(peakSlip, Mathf.Abs(local.x) / Mathf.Max(1f, body.linearVelocity.magnitude));
            peakYaw = Mathf.Max(peakYaw, Mathf.Abs(body.angularVelocity.y));
            upright = Mathf.Min(upright, Vector3.Dot(car.transform.up, Vector3.up));
            if (i == 49)
                firstTurnHeading = Vector3.SignedAngle(Vector3.forward, car.transform.forward, Vector3.up);
        }
        Check(firstTurnHeading > 8f, $"{speedKmh:0} km/h steering changes heading: {firstTurnHeading:F1} degrees");
        Check(peakSlip < 0.25f && peakYaw < 1.8f && upright > 0.9f,
            $"{speedKmh:0} km/h turn/reversal: sideways fraction {peakSlip:F3}, yaw {peakYaw:F3}, upright {upright:F3}");
        Check(body.linearVelocity.magnitude * 3.6f > speedKmh * 0.65f,
            $"Turn exit speed {body.linearVelocity.magnitude * 3.6f:F1} km/h");
        UnityEngine.Object.DestroyImmediate(car.gameObject);
    }

    private static void TestSideSwipe()
    {
        CarDriver a = Spawn(new Vector3(-1.23f, 0.6f, 0));
        CarDriver b = Spawn(new Vector3(1.23f, 0.6f, 0.5f));
        var cars = new[] { a, b };
        Step(cars, 0, 0, 100);
        Rigidbody ra = a.GetComponent<Rigidbody>(), rb = b.GetComponent<Rigidbody>();
        ra.linearVelocity = new Vector3(2f, 0, 20f);
        rb.linearVelocity = new Vector3(-2f, 0, 17f);
        float minForward = float.PositiveInfinity;
        float peakYaw = 0f;
        for (int i = 0; i < 100; i++)
        {
            Step(cars, 1, 0, 1);
            minForward = Mathf.Min(minForward, Vector3.Dot(ra.linearVelocity, a.transform.forward),
                Vector3.Dot(rb.linearVelocity, b.transform.forward));
            peakYaw = Mathf.Max(peakYaw, Mathf.Abs(ra.angularVelocity.y), Mathf.Abs(rb.angularVelocity.y));
        }
        Check(minForward > 10f && peakYaw < 1.5f,
            $"During sideswipe: minimum forward speed {minForward * 3.6f:F1} km/h, peak yaw {peakYaw:F3}");
        Check(Vector3.Dot(ra.linearVelocity, a.transform.forward) > 10f &&
              Vector3.Dot(rb.linearVelocity, b.transform.forward) > 10f,
            $"Sideswipe maintains forward motion: {a.SpeedKmh:F1}, {b.SpeedKmh:F1} km/h");
        Check(Vector3.Dot(a.transform.forward, Vector3.forward) > 0.8f &&
              Vector3.Dot(b.transform.forward, Vector3.forward) > 0.8f,
            $"Sideswipe heading: {a.transform.eulerAngles.y:F1}, {b.transform.eulerAngles.y:F1} degrees");
        Check(!Physics.ComputePenetration(a.GetComponent<BoxCollider>(), ra.position, ra.rotation,
            b.GetComponent<BoxCollider>(), rb.position, rb.rotation, out _, out float overlap) || overlap < 0.03f,
            "Sideswipe cars separate without persistent chassis overlap");
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(b.gameObject);
    }

    private static void TestDoorContact()
    {
        CarDriver a = Spawn(new Vector3(-1.23f, 0.6f, 0));
        CarDriver b = Spawn(new Vector3(1.23f, 0.6f, 0.3f));
        Step(new[] { a, b }, 0, 0, 100);
        Rigidbody ra = a.GetComponent<Rigidbody>(), rb = b.GetComponent<Rigidbody>();
        ra.linearVelocity = rb.linearVelocity = Vector3.forward * 19f;
        BoxCollider shapeA = a.GetComponent<BoxCollider>(), shapeB = b.GetComponent<BoxCollider>();
        int touchingSteps = 0;
        float minimumSpeed = float.PositiveInfinity;
        for (int i = 0; i < 100; i++)
        {
            a.SetInput(1, 0.25f, false);
            b.SetInput(1, -0.25f, false);
            TickDriver.Invoke(a, null);
            TickDriver.Invoke(b, null);
            Physics.Simulate(Dt);
            Vector3 pointB = shapeB.ClosestPoint(shapeA.bounds.center);
            Vector3 pointA = shapeA.ClosestPoint(pointB);
            if ((pointA - pointB).sqrMagnitude < 0.04f * 0.04f)
                touchingSteps++;
            minimumSpeed = Mathf.Min(minimumSpeed, Vector3.Dot(ra.linearVelocity, a.transform.forward),
                Vector3.Dot(rb.linearVelocity, b.transform.forward));
        }
        Check(touchingSteps >= 10, $"Sustained door contact exercised for {touchingSteps * Dt:F2} seconds");
        Check(minimumSpeed > 10f && Vector3.Dot(a.transform.up, Vector3.up) > 0.9f &&
            Vector3.Dot(b.transform.up, Vector3.up) > 0.9f,
            $"Cars pushing together stay upright and moving: minimum {minimumSpeed * 3.6f:F1} km/h");
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(b.gameObject);
    }

    private static void TestRearImpact()
    {
        CarDriver a = Spawn(new Vector3(0, 0.6f, -4.5f));
        CarDriver b = Spawn(new Vector3(0, 0.6f, 0));
        var cars = new[] { a, b };
        Step(cars, 0, 0, 100);
        a.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 22f;
        b.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 12f;
        Step(cars, 1, 0, 75);
        Check(a.SpeedKmh > 35 && b.SpeedKmh > 35 &&
            Vector3.Dot(a.transform.forward, b.transform.forward) > 0.9f,
            $"Rear impact remains drivable: {a.SpeedKmh:F1}, {b.SpeedKmh:F1} km/h");
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(b.gameObject);
    }

    private static void TestAirborne()
    {
        CarDriver car = Spawn(new Vector3(0, 30, 0));
        Rigidbody body = car.GetComponent<Rigidbody>();
        body.linearVelocity = new Vector3(0, 5, 30);
        body.angularVelocity = new Vector3(0.5f, 1f, 0.75f);
        Step(new[] { car }, 1, 1, 10);
        Check(Mathf.Abs(body.linearVelocity.y - (5f - 9.81f * 10 * Dt)) < 0.05f,
            $"Airborne vertical velocity follows gravity: {body.linearVelocity.y:F3} m/s");
        Check(body.angularVelocity.y > 0.95f && body.angularVelocity.x > 0.45f && body.angularVelocity.z > 0.7f,
            "Grounded stability does not cancel airborne pitch, yaw or roll");
        UnityEngine.Object.DestroyImmediate(car.gameObject);
    }
    private static void TestBarriers()
    {
        var trackObject = new GameObject("Barrier regression track");
        var track = trackObject.AddComponent<LoopTrackGenerator>();
        track.Generate();
        Physics.SyncTransforms();
        int cases = 0, failures = 0, wallWheelHits = 0;
        float worstRise = 0f, worstUp = 0f;
        foreach (int index in new[] { 0, 24, 48, 72, 96, 120, 144, 168 })
        foreach (int sideSign in new[] { -1, 1 })
        foreach (float speed in new[] { 70f, 100f, 130f })
        foreach (float angle in new[] { 5f, 15f })
        foreach (float roll in new[] { 0f, -5f, 5f })
        {
            Vector3 along = track.GetForward(index);
            Vector3 outward = new Vector3(-along.z, 0, along.x) * sideSign;
            float wallInside = track.roadWidth * 0.5f + track.curbWidth + track.wallGap;
            Vector3 start = (track.GetPoint(index) + track.GetPoint(index + 1)) * 0.5f
                + outward * (wallInside - 1.7f);
            start.y = 0.6f;
            CarDriver car = Spawn(start);
            Rigidbody body = car.GetComponent<Rigidbody>();
            body.rotation = Quaternion.LookRotation(along);
            Physics.SyncTransforms();
            Step(new[] { car }, 0, 0, 75);
            float restY = body.position.y;
            Vector3 heading = (along * Mathf.Cos(angle * Mathf.Deg2Rad)
                + outward * Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
            body.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0, 0, roll);
            body.linearVelocity = heading * speed / 3.6f;
            Physics.SyncTransforms();
            float rise = 0f, upward = 0f;
            var wheels = car.GetComponentsInChildren<WheelCollider>();
            for (int step = 0; step < 100; step++)
            {
                Step(new[] { car }, 1, -0.25f * sideSign, 1);
                rise = Mathf.Max(rise, body.position.y - restY);
                upward = Mathf.Max(upward, body.linearVelocity.y);
                foreach (var wheel in wheels)
                    if (wheel.GetGroundHit(out WheelHit hit) && hit.collider.name.StartsWith("Wall_"))
                        wallWheelHits++;
            }
            cases++;
            worstRise = Mathf.Max(worstRise, rise);
            worstUp = Mathf.Max(worstUp, upward);
            if (rise > 0.7f || upward > 3f)
            {
                failures++;
                Report.AppendLine($"Barrier launch: segment {index}, side {sideSign}, {speed} km/h, {angle} degrees, roll {roll}: rise {rise:F2} m, upward {upward:F2} m/s");
            }
            UnityEngine.Object.DestroyImmediate(car.gameObject);
        }
        Check(failures == 0, $"Barrier matrix: {cases - failures}/{cases} stable, worst rise {worstRise:F2} m, upward {worstUp:F2} m/s");
        Check(wallWheelHits == 0, $"Suspension contacts on barrier geometry: {wallWheelHits}");
        UnityEngine.Object.DestroyImmediate(trackObject);
    }

    private static void TestBarrierBlockingAndLanding()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_ChassisProbe";
        wall.layer = LayerMask.NameToLayer("TrackBarrier");
        wall.transform.position = new Vector3(2.5f, 0.35f, 0);
        wall.transform.localScale = new Vector3(0.6f, 1.3f, 100);
        int wallId = wall.GetComponent<Collider>().GetInstanceID();
        VehicleBarrierContacts.RegisterBarrier(wallId);
        CarDriver car = Spawn(new Vector3(0, 0.6f, 0));
        Step(new[] { car }, 0, 0, 75);
        Rigidbody body = car.GetComponent<Rigidbody>();
        body.linearVelocity = new Vector3(4f, 0, 25f);
        float furthestX = body.position.x;
        for (int i = 0; i < 100; i++)
        {
            Step(new[] { car }, 1, 0.3f, 1);
            furthestX = Mathf.Max(furthestX, body.position.x);
        }
        Check(furthestX > 0.5f && furthestX < 1.9f,
            $"Chassis still blocked by barrier: furthest centre {furthestX:F2} m, wall inner face 2.20 m");
        UnityEngine.Object.DestroyImmediate(car.gameObject);
        VehicleBarrierContacts.RemoveBarrier(wallId);
        UnityEngine.Object.DestroyImmediate(wall);

        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_LandingProbe";
        wall.layer = LayerMask.NameToLayer("TrackBarrier");
        wall.transform.position = new Vector3(0, 0.5f, 0);
        wall.transform.localScale = new Vector3(8, 1, 12);
        wallId = wall.GetComponent<Collider>().GetInstanceID();
        VehicleBarrierContacts.RegisterBarrier(wallId);
        car = Spawn(new Vector3(0, 3, 0));
        body = car.GetComponent<Rigidbody>();
        Step(new[] { car }, 0, 0, 120);
        Check(body.position.y > 0.5f && Mathf.Abs(body.linearVelocity.y) < 0.3f,
            $"Barrier top still supports chassis landing: height {body.position.y:F2} m, vertical speed {body.linearVelocity.y:F2} m/s");
        UnityEngine.Object.DestroyImmediate(car.gameObject);
        VehicleBarrierContacts.RemoveBarrier(wallId);
        UnityEngine.Object.DestroyImmediate(wall);
    }

    private static void TestAIContactRecovery()
    {
        CarDriver car = Spawn(new Vector3(0, 30, 0));
        Rigidbody body = car.GetComponent<Rigidbody>();
        body.useGravity = false;
        body.angularDamping = 0f;
        body.linearVelocity = new Vector3(3, 0, 12);
        body.angularVelocity = Vector3.up;
        var ai = car.gameObject.AddComponent<AICarWaypointDriver>();
        ai.enabled = false;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(AICarWaypointDriver).GetField("rb", fields).SetValue(ai, body);
        typeof(AICarWaypointDriver).GetField("driver", fields).SetValue(ai, car);
        typeof(AICarWaypointDriver).GetField("recoverUntil", fields).SetValue(ai, Time.time + 10f);
        typeof(CarDriver).GetField("lastVehicleContact", fields)?.SetValue(car, Time.fixedTime);
        typeof(CarDriver).GetField("<GroundedWheelCount>k__BackingField", fields)?.SetValue(car, 4);
        typeof(AICarWaypointDriver).GetMethod("Recover", fields).Invoke(ai,
            new object[] { Vector3.forward, Vector3.right, 70f, 43f });
        Physics.Simulate(Dt);
        Check(Mathf.Abs(body.linearVelocity.x - 3f) < 0.01f &&
            Mathf.Abs(body.linearVelocity.z - 12f) < 0.01f &&
            Mathf.Abs(body.angularVelocity.y - 1f) < 0.01f,
            "AI recovery leaves contact velocity and spin to the collision solver");
        UnityEngine.Object.DestroyImmediate(car.gameObject);
    }

}
