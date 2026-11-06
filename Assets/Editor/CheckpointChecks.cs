using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CheckpointChecks
{
    [MenuItem("Tools/Color Race/Check Checkpoint Isolation")]
    public static void Run()
    {
        RaceManager race = RaceManager.Instance;

        if (!Application.isPlaying || !EditorApplication.isPaused ||
            race == null || !race.RacersInitialized || !race.IsRacing ||
            !race.requireCheckpoint || race.Racers.Count != 3)
        {
            Debug.LogWarning(
                "Start the race, wait until GO disappears, then pause."
            );
            return;
        }

        FieldInfo field = typeof(RaceManager).GetField(
            "eligibleCars",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        var checkpoints = field?.GetValue(race) as HashSet<CarDriver>;

        if (checkpoints == null)
        {
            Debug.LogError("Could not find the checkpoint registry.");
            return;
        }

        CarDriver a = race.Racers[0];
        CarDriver b = race.Racers[1];
        CarDriver c = race.Racers[2];

        if (a == null || b == null || c == null ||
            a == b || a == c || b == c)
        {
            Debug.LogError("The roster needs three different cars.");
            return;
        }

        var savedCheckpoints = new HashSet<CarDriver>(checkpoints);
        Transform savedPlayer = race.CurrentPlayerCar;

        try
        {
            checkpoints.Clear();

            race.ReachCheckpoint(a.transform);

            Check(race.HasCheckpoint(a.transform),
                "A should qualify after reaching its checkpoint.");

            Check(!race.HasCheckpoint(b.transform) &&
                  !race.HasCheckpoint(c.transform),
                "Qualifying A must not qualify B or C.");

            Check(race.SetControlledCar(b.transform),
                "Could not select B.");

            Check(race.HasCheckpoint(a.transform) &&
                  !race.HasCheckpoint(b.transform),
                "Switching to B transferred or lost checkpoint progress.");

            race.CrossFinish(b.transform);

            Check(!race.IsFinished,
                "B finished without reaching its checkpoint.");

            race.ReachCheckpoint(b.transform);

            Check(race.SetControlledCar(c.transform),
                "Could not select C.");

            Check(race.HasCheckpoint(a.transform) &&
                  race.HasCheckpoint(b.transform) &&
                  !race.HasCheckpoint(c.transform),
                "Checkpoint progress is not independent for each car.");

            race.CrossFinish(c.transform);

            Check(!race.IsFinished,
                "C finished without reaching its checkpoint.");

            Check(race.SetControlledCar(a.transform),
                "Could not switch back to A.");

            Check(race.HasCheckpoint(a.transform) &&
                  race.HasCheckpoint(b.transform),
                "Switching back lost checkpoint progress.");

            Debug.Log(
                "PASS: checkpoint progress stays with each physical car; " +
                "unqualified cars cannot finish; switching back preserves progress."
            );
        }
        catch (Exception error)
        {
            Debug.LogError("CHECKPOINT TEST FAILED: " + error.Message);
        }
        finally
        {
            checkpoints.Clear();

            foreach (CarDriver car in savedCheckpoints)
                checkpoints.Add(car);

            if (!race.IsFinished && savedPlayer != null)
                race.SetControlledCar(savedPlayer);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}