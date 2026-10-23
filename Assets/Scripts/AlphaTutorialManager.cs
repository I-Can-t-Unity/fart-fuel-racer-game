
using UnityEngine;
using TMPro;

public class AlphaTutorialManager : MonoBehaviour
{
    [Header("References")]
    public Transform playerCar;
    public GameObject tutorialFuelGroup;
    public TMP_Text tutorialMessage;

    [Header("Settings")]
    public float movementDistance = 3f;

    private Vector3 startingPosition;
    private int tutorialStage = 0;

    void Start()
    {
        startingPosition = playerCar.position;

        tutorialFuelGroup.SetActive(false);

        tutorialMessage.text =
            "<b>GET MOVING</b>\n" +
            "WASD / arrows: drive and steer.\n" +
            "Space: brake.\n" +
            "Drive forward to continue.";
    }

    void Update()
    {
        if (tutorialStage == 0)
        {
            float distance =
                Vector3.Distance(playerCar.position, startingPosition);

            if (distance >= movementDistance)
            {
                ShowFuelTutorial();
            }
        }
    }

    void ShowFuelTutorial()
    {
        tutorialStage = 1;

        tutorialFuelGroup.SetActive(true);

        tutorialMessage.text =
            "<b>COLLECT FUEL</b>\n" +
            "Drive through a fuel can.\n" +
            "Match your car's fuel color\n" +
            "to refuel.";
    }
}
