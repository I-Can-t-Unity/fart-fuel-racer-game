using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CarSelectionMenu : MonoBehaviour
{
    const float RotationSpeed = 36f;
    const float InitialAngle = 24f;
    static readonly Color SelectedBlock = new Color32(47, 68, 88, 255);
    static readonly Color IdleBlock = new Color32(26, 39, 53, 255);

    public GameObject selectionPanel;
    [SerializeField] Button[] colorButtons;
    [SerializeField] VehicleColorBlock[] colorBlocks;
    [SerializeField] Camera previewCamera;
    [SerializeField] Transform previewCar;
    [SerializeField] MeshRenderer previewBody;
    [SerializeField] Material[] bodyColors;

    int selectedLevel = -1;
    int selectedColor;
    float previewAngle;
    Vector3 previewLocalCenter;
    Vector3 previewWorldCenter;

    internal static FuelType SelectedColor => (FuelType)Mathf.Clamp(
        PlayerPrefs.GetInt("SelectedCarColor", PlayerPrefs.GetInt("SelectedCarFuel", 0)), 0, 2);

    void Awake()
    {
        CloseSelection();
        FramePreviewCar();
    }

    void FramePreviewCar()
    {
        Bounds bounds = previewBody.bounds;
        foreach (MeshRenderer renderer in previewCar.GetComponentsInChildren<MeshRenderer>())
            bounds.Encapsulate(renderer.bounds);

        previewLocalCenter = previewCar.InverseTransformPoint(bounds.center);
        // Center the rotation axis in the preview while keeping the camera's viewing distance.
        Transform cameraTransform = previewCamera.transform;
        float depth = cameraTransform.InverseTransformPoint(bounds.center).z;
        previewWorldCenter = cameraTransform.TransformPoint(0f, 0f, depth);
    }

    public void OpenSelection(int level)
    {
        if (selectionPanel == null || !LevelSelectionMenu.IsUnlocked(level))
            return;

        selectedLevel = level;
        previewAngle = InitialAngle;
        selectionPanel.SetActive(true);
        selectionPanel.transform.SetAsLastSibling();
        SelectCar((int)SelectedColor);
        previewCamera.enabled = true;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(colorButtons[selectedColor].gameObject);
    }

    public void CloseSelection()
    {
        selectedLevel = -1;
        if (previewCamera != null)
            previewCamera.enabled = false;
        if (selectionPanel != null)
            selectionPanel.SetActive(false);
    }

    public void SelectCar(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex >= colorButtons.Length || !LevelSelectionMenu.IsUnlocked(selectedLevel))
            return;

        selectedColor = colorIndex;
        Material[] materials = previewBody.sharedMaterials;
        materials[0] = bodyColors[colorIndex];
        previewBody.sharedMaterials = materials;
        for (int i = 0; i < colorButtons.Length; i++)
        {
            colorButtons[i].targetGraphic.color = i == selectedColor ? SelectedBlock : IdleBlock;
            colorBlocks[i].Angle = InitialAngle;
        }
        UpdatePreviewRotation();
    }

    void Update()
    {
        if (selectedLevel < 0)
            return;

        previewAngle = Mathf.Repeat(previewAngle + RotationSpeed * Time.unscaledDeltaTime, 360f);
        UpdatePreviewRotation();
    }

    void UpdatePreviewRotation()
    {
        previewCar.localRotation = Quaternion.Euler(0f, previewAngle, 0f);
        previewCar.position = previewWorldCenter - previewCar.TransformVector(previewLocalCenter);
        colorBlocks[selectedColor].Angle = previewAngle;
    }

    public void Next()
    {
        if (!LevelSelectionMenu.IsUnlocked(selectedLevel))
            return;

        SaveSelectedColor((FuelType)selectedColor);
        int level = selectedLevel;
        CloseSelection();
        LevelSelectionMenu.StartRace(level);
    }

    internal static void SaveSelectedColor(FuelType color)
    {
        int colorIndex = (int)color;
        PlayerPrefs.SetInt("SelectedCarColor", colorIndex);
        // Compatibility only; Jason's color system should read SelectedCarColor.
        PlayerPrefs.SetInt("SelectedCarFuel", colorIndex);
        PlayerPrefs.Save();
    }
}
