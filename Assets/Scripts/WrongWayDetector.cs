using UnityEngine;

// Runs after the HUD has updated physical-car progress for this frame.
[DisallowMultipleComponent]
public class WrongWayDetector : MonoBehaviour
{
    [SerializeField, Min(0f)] private float minimumSpeed = 2f;
    [SerializeField, Min(0f)] private float warningDelay = .6f;
    private RacePositionHUD standings;
    private Rigidbody playerBody;
    private CarDriver playerDriver;
    private float wrongWaySeconds;

    private void Awake() => standings = GetComponent<RacePositionHUD>();

    private void LateUpdate()
    {
        RaceManager race = RaceManager.Instance;
        Rigidbody controlled = race != null ? race.carBody : null;
        if (controlled != playerBody)
        {
            UnbindPlayer();
            playerBody = controlled;
            playerDriver = playerBody != null ? playerBody.GetComponent<CarDriver>() : null;
            if (playerDriver != null) playerDriver.Respawned += ClearWarning;
            ClearWarning();
        }

        if (race == null || !race.IsRacing || playerDriver == null || !playerDriver.CanDrive
            || standings == null || !standings.isActiveAndEnabled
            || !standings.TryGetTrackForward(playerBody, out Vector3 forward))
        {
            ClearWarning();
            return;
        }

        Vector3 velocity = Vector3.ProjectOnPlane(playerBody.linearVelocity, Vector3.up);
        // Use travel direction, so reversing along the lap is also detected.
        // Sideways slides and brief collision deflections do not show a banner.
        bool wrongWay = velocity.magnitude >= minimumSpeed
            && Vector3.Dot(velocity.normalized, forward) < -.35f;
        if (!wrongWay)
        {
            ClearWarning();
            return;
        }

        wrongWaySeconds += Time.deltaTime;
        WrongWayWarning.SetVisible(wrongWaySeconds >= warningDelay);
    }

    private void ClearWarning()
    {
        wrongWaySeconds = 0f;
        WrongWayWarning.SetVisible(false);
    }

    private void UnbindPlayer()
    {
        if (playerDriver != null) playerDriver.Respawned -= ClearWarning;
        playerDriver = null;
        playerBody = null;
    }

    private void OnDisable()
    {
        UnbindPlayer();
        ClearWarning();
    }
}
