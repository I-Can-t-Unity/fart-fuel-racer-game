
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
            "WELCOME TO FUEL FRENZY!\n\n" +
            "WASD / Arrow Keys - Drive and Steer\n" +
            "SPACE - Brake\n\n" +
            "Start driving to continue...";
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
            "COLLECT YOUR FUEL!\n\n" +
            "Drive into the colored fuel blocks\n" +
            "to collect them!";
    }
}
