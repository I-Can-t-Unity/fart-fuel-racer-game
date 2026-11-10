using UnityEngine;

// Cube strategy for an AI car. The AI doesn't run on fuel any more; this decides which
// colour cube it goes for and what happens when it gets one.
//
// Leading: it grabs its own colour for a boost. 2nd or 3rd: it grabs the leader's colour so
// the leader can't use them. Two of the player's colour in a row and it steals the player's car.
//
// AICarWaypointDriver adds this at runtime and does the steering. The fuel and mix bits below
// are only here because RaceManager, the jump gate and the shortcut code still look for them.
public class AIFuelTank : MonoBehaviour, IFuelMixHolder
{
    [Tooltip("This car's colour. Set at the start from its slot in RaceManager > Racers.")]
    public FuelType ownFuel = FuelType.Diesel;
    [Tooltip("Logs what the AI is going for and what it picks up.")]
    public bool logFuelDecisions = true;

    [HideInInspector] public float fuel = 100f;
    [HideInInspector] public float drainPerSecond, drainPerSpeed, drainPerThrottle;

    // The AI never holds a mix, so it never pays for a shortcut.
    public FuelMix HeldMix => FuelMix.None;
    public bool TryConsumeMix(FuelMix mix) => false;
    FuelMixType IFuelMixHolder.HeldMix => FuelMixType.None;
    bool IFuelMixHolder.TryConsumeMix(FuelMixType mix) => false;

    public FartBoost Fart => fart;
    FartBoost fart;

    void Awake()
    {
        fart = GetComponent<FartBoost>();
        if (fart == null)
            fart = gameObject.AddComponent<FartBoost>();
    }

    [Header("Colour cubes")]
    [Tooltip("Boost from its own colour, in seconds. Copied from the player's at the start.")]
    public float cubeBoostSeconds = .7f;
    [Tooltip("A car has to be this many metres ahead to count as ahead, so cars side by side don't keep swapping places.")]
    public float positionMarginMetres = 2f;

    public int RacePosition { get; private set; } = 1;
    public FuelType LeaderColour { get; private set; }
    public FuelType CubeColour => RacePosition <= 1 ? ownFuel : LeaderColour;

    Rigidbody[] racerBodies;
    bool boostCopied;
    FuelType lastCubeColour;
    bool cubeColourLogged;

    // Called by the driver every physics step. Figures out our place and who's leading.
    public void UpdateStanding(Rigidbody body)
    {
        if (!boostCopied)
        {
            boostCopied = true;
            FuelInventory switching = SwitchingInventory();
            if (switching != null)
                cubeBoostSeconds = switching.pickupFartSeconds;
        }

        RacePosition = 1;
        LeaderColour = ownFuel;
        RaceManager race = RaceManager.Instance;
        RacePositionHUD standings = RacePositionHUD.Instance;
        if (race == null || standings == null || race.Racers == null || body == null)
            return;
        if (racerBodies == null || racerBodies.Length != race.Racers.Count)
        {
            racerBodies = new Rigidbody[race.Racers.Count];
            for (int i = 0; i < racerBodies.Length; i++)
                racerBodies[i] = race.Racers[i] != null ? race.Racers[i].GetComponent<Rigidbody>() : null;
        }
        if (!standings.TryGetMetres(body, out float mine))
            return;

        float leaderMetres = mine;
        for (int i = 0; i < racerBodies.Length; i++)
        {
            Rigidbody other = racerBodies[i];
            if (other == null || other == body || !standings.TryGetMetres(other, out float metres))
                continue;
            if (metres > mine + positionMarginMetres)
                RacePosition++;
            if (metres > leaderMetres + positionMarginMetres)
            {
                leaderMetres = metres;
                LeaderColour = race.ColorForRacerIndex(i);
            }
        }
        if (RacePosition > 1 && LeaderColour == ownFuel)
            RacePosition = 1;

        if (logFuelDecisions && (!cubeColourLogged || CubeColour != lastCubeColour))
        {
            cubeColourLogged = true;
            lastCubeColour = CubeColour;
            Debug.Log(RacePosition <= 1
                ? $"[AI] {name} is leading: going for its own {ownFuel} cubes."
                : $"[AI] {name} is P{RacePosition}: going for the leader's {LeaderColour} cubes.", this);
        }
    }

    // Anything that isn't the colour we're after gets left for the others.
    public bool WantsCube(FuelType type)
    {
        if (RaceManager.Instance != null && !RaceManager.Instance.IsRacing)
            return false;
        return type == CubeColour;
    }

    public void TakeCube(FuelType type)
    {
        if (type == ownFuel && fart != null)
            fart.Fart(cubeBoostSeconds);
        if (logFuelDecisions)
            Debug.Log(type == ownFuel
                ? $"[AI] {name} took its own {type} cube: boost."
                : $"[AI] {name} took the leader's {type} cube away.", this);
        CountTowardPlayersCar(type);
    }

    [Header("Stealing the player's car")]
    [Tooltip("Two of the player's cubes in a row and this AI swaps cars with the player.")]
    public bool takeOverPlayersCar = true;
    public int cubesToTakeOver = 2;
    [Tooltip("Shortest gap between two steals by any AI car, in seconds.")]
    public float takeOverCooldownSeconds = 10f;

    int playersCubesInARow;
    // Shared by both AI cars so the player doesn't get swapped twice back to back.
    static float lastTakeOver = -100f;
    static int lastTakeOverScene = -1;

    // Same idea as the player's own switch: a cube of any other colour resets the count.
    void CountTowardPlayersCar(FuelType type)
    {
        if (!takeOverPlayersCar || !PlayerColour(out FuelType playerColour) || type != playerColour || type == ownFuel)
        {
            playersCubesInARow = 0;
            return;
        }
        playersCubesInARow++;
        FuelInventory switching = SwitchingInventory();
        if (switching == null)
            return;

        if (playersCubesInARow < cubesToTakeOver)
        {
            switching.Announce($"{switching.NameOf(ownFuel)} TOOK YOUR CUBE {playersCubesInARow}/{cubesToTakeOver}");
            return;
        }

        bool coolingDown = lastTakeOverScene == gameObject.scene.handle && Time.time - lastTakeOver < takeOverCooldownSeconds;
        // Blocked for now: hold at one short, so the next cube finishes it.
        if (coolingDown || switching.IsImmune || !switching.ForceSwitchTo(GetComponent<CarDriver>()))
        {
            playersCubesInARow = cubesToTakeOver - 1;
            return;
        }
        lastTakeOver = Time.time;
        lastTakeOverScene = gameObject.scene.handle;
        playersCubesInARow = 0;
        if (logFuelDecisions)
            Debug.Log($"[AI] {name} took {cubesToTakeOver} {playerColour} cubes in a row and stole the player's car.", this);
    }

    static bool PlayerColour(out FuelType colour)
    {
        colour = FuelType.Gasoline;
        RaceManager race = RaceManager.Instance;
        if (race == null || race.car == null || race.Racers == null)
            return false;
        CarDriver playerCar = race.car.GetComponentInParent<CarDriver>();
        for (int i = 0; i < race.Racers.Count; i++)
        {
            if (race.Racers[i] == playerCar)
            {
            colour = race.ColorForRacerIndex(i);
                return true;
            }
        }
        return false;
    }

    // AICar2 also has a FuelInventory with no cars in it, so look for the one that has the car list.
    static FuelInventory SwitchingInventory()
    {
        foreach (FuelInventory inventory in FindObjectsByType<FuelInventory>(FindObjectsSortMode.None))
            if (inventory.cars != null && inventory.cars.Length > 0)
                return inventory;
        return null;
    }
}
