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
                    EditorUtility.SetDirty(ai);
                }
                // Reuse the body's exact renderer/slot instead of painting wheels/windows.
                FuelInventory bag = car.GetComponent<FuelInventory>();
                if (bag == null) bag = Undo.AddComponent<FuelInventory>(car.gameObject);
                if (bag.bodyRenderer == null && ai != null)
                {
                    Undo.RecordObject(bag, "Assign car body renderer");
                    bag.bodyRenderer = ai.bodyRenderer;
                    bag.bodyMaterialSlot = ai.bodyMaterialSlot;
                }
                ApplySelectedCar appearance = car.GetComponent<ApplySelectedCar>();
                if (appearance == null) appearance = Undo.AddComponent<ApplySelectedCar>(car.gameObject);
                Undo.RecordObject(appearance, "Assign physical color");
                appearance.physicalColorIndex = i % 3;
                EditorUtility.SetDirty(appearance);
                EditorUtility.SetDirty(bag);
            }
            EditorUtility.SetDirty(race);
            if (race.racers.Length != 3)
                Debug.LogWarning("Grid still needs exactly THREE cars. No cars were cloned. Set Racers to Brown/Green/Purple order, then run this command again.", race);
        }
        foreach (CarSelectionMenu selection in Object.FindObjectsByType<CarSelectionMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(selection, "Connect level selection");
            LevelSelectionMenu levels = selection.GetComponent<LevelSelectionMenu>();
            if (levels == null) levels = Undo.AddComponent<LevelSelectionMenu>(selection.gameObject);
            selection.levelSelection = levels;
            EditorUtility.SetDirty(selection);
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
                        label.text = new[] { "BROWN", "GREEN", "PURPLE" }[color];
                        EditorUtility.SetDirty(label);
                    }
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Color race configured. Check roster order/body renderers, assign supplied tutorial/challenge scenes on LevelSelectionMenu, then save this scene.");
    }

    static void Add(List<CarDriver> cars, Transform root)
    {
        CarDriver driver = root != null ? root.GetComponentInParent<CarDriver>() : null;
        if (driver != null && !cars.Contains(driver)) cars.Add(driver);
    }
}
