using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SohamColorRaceSetup
{
    [MenuItem("Tools/Color Race/Configure Open Scene")]
    public static void Configure()
    {
        if (Application.isPlaying) { Debug.LogWarning("Exit Play mode first."); return; }
        RaceManager race = Object.FindFirstObjectByType<RaceManager>();
        if (race != null)
        {
            Undo.RecordObject(race, "Configure color race");
            race.colorSwitchingMode = true;
            race.requireCheckpoint = true;
            FuelInventory switching = null;
            foreach (FuelInventory candidate in Object.FindObjectsByType<FuelInventory>(FindObjectsSortMode.None))
                if (candidate.cars != null && candidate.cars.Length == 3) { switching = candidate; break; }
            if (switching != null) race.racers = (CarDriver[])switching.cars.Clone();
            if (race.racers == null || race.racers.Length == 0)
            {
                var roster = new List<CarDriver>();
                Add(roster, race.car);
                Add(roster, race.opponent);
                CarDriver[] found = Object.FindObjectsByType<CarDriver>(FindObjectsSortMode.InstanceID);
                foreach (CarDriver car in found) if (!roster.Contains(car)) roster.Add(car);
                race.racers = roster.ToArray();
            }
            var seen = new HashSet<CarDriver>();
            for (int i = 0; i < race.racers.Length; i++)
            {
                CarDriver car = race.racers[i];
                if (car == null || !seen.Add(car)) continue;
                AICarWaypointDriver ai = car.GetComponent<AICarWaypointDriver>();
                if (ai != null)
                {
                    Undo.RecordObject(ai, "Use fixed car colors");
                    ai.pickCarFromMenu = false;
                    ai.paintWithFuelColour = false;
                    ai.useShortcuts = false;
                    EditorUtility.SetDirty(ai);
                }
                ApplySelectedCar appearance = car.GetComponent<ApplySelectedCar>();
                if (appearance != null)
                {
                    Undo.RecordObject(appearance, "Assign physical color");
                    appearance.physicalColorIndex = i % 3;
                    EditorUtility.SetDirty(appearance);
                }
            }
            EditorUtility.SetDirty(race);
            if (race.racers.Length != 3)
                Debug.LogWarning("Grid still needs exactly THREE cars. No cars were cloned. Set Racers to Red/Blue/Yellow order, then run this command again.", race);
        }
        foreach (CarSelectionMenu selection in Object.FindObjectsByType<CarSelectionMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (selection.GetComponent<LevelSelectionMenu>() == null)
                Undo.AddComponent<LevelSelectionMenu>(selection.gameObject);
            if (selection.selectionPanel == null) continue;
            foreach (Button button in selection.selectionPanel.GetComponentsInChildren<Button>(true))
            {
                var serialized = new SerializedObject(button);
                SerializedProperty calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if (calls == null) continue;
                for (int i = 0; i < calls.arraySize; i++)
                {
                    SerializedProperty call = calls.GetArrayElementAtIndex(i);
                    if (call.FindPropertyRelative("m_MethodName").stringValue != "SelectCar" ||
                        call.FindPropertyRelative("m_Target").objectReferenceValue != selection) continue;
                    int color = call.FindPropertyRelative("m_Arguments.m_IntArgument").intValue;
                    if (color < 0 || color > 2) continue;
                    TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                    if (label != null)
                    {
                        Undo.RecordObject(label, "Rename starting color");
                        label.text = new[] { "RED", "BLUE", "YELLOW" }[color];
                        EditorUtility.SetDirty(label);
                    }
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Color race configured. Check roster order/body renderers, assign supplied tutorial/challenge scenes on LevelSelectionMenu, then save this scene.");
    }

    [MenuItem("Tools/Color Race/Check Running Race")]
    public static void CheckRunningRace()
    {
        RaceManager race = RaceManager.Instance;
        if (!Application.isPlaying || race == null || !race.RacersInitialized)
        {
            Debug.LogWarning("Start the race and wait for initialization before checking.");
            return;
        }
        int players = 0, aiCars = 0;
        bool valid = race.Racers.Count == 3;
        foreach (CarDriver car in race.Racers)
        {
            if (car == null) { valid = false; continue; }
            AICarWaypointDriver ai = car.GetComponent<AICarWaypointDriver>();
            bool controlled = car.transform == race.CurrentPlayerCar;
            if (!car.ExternalInput) players++;
            if (ai != null && ai.enabled) aiCars++;
            valid &= car.enabled && car.ExternalInput != controlled;
            valid &= ai != null && ai.enabled != controlled;
        }
        FuelInventory switching = null;
        foreach (FuelInventory candidate in Object.FindObjectsByType<FuelInventory>(FindObjectsSortMode.None))
            if (candidate.cars != null && candidate.cars.Length == 3) { switching = candidate; break; }
        valid &= switching != null && switching.CurrentCar != null
            && switching.CurrentCar.transform == race.CurrentPlayerCar;
        valid &= players == 1 && aiCars == 2;
        if (valid) Debug.Log("PASS: three cars, exactly one player, two AI competitors, and synchronized switching/race references.", race);
        else Debug.LogError("Integration check failed: player count " + players + ", AI count " + aiCars + ". Check roster and switching references.", race);
    }

    static void Add(List<CarDriver> cars, Transform root)
    {
        CarDriver driver = root != null ? root.GetComponentInParent<CarDriver>() : null;
        if (driver != null && !cars.Contains(driver)) cars.Add(driver);
    }
}
