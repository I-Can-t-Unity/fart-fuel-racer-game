# Fuel Pickup Analytics

Measures the **Fuel Pickup Collection Rate**: which fuel colours players pick up, and what share of the pickups available to them they actually collect.

```
Game (editor or WebGL on GitHub Pages)
  FuelPickupAnalytics.cs  ──POST──▶  Google Apps Script (Code.gs)  ──▶  Google Sheet "events"
                                                   │
docs/analytics/index.html  ◀──────GET─────────────┘   (charts, tables, hypothesis test)
```

| File | Role |
|---|---|
| `Assets/Scripts/FuelPickupAnalytics.cs` | Records events during a race (on PlayerCar in LoopRaceScene) |
| `Assets/Scripts/AnalyticsUploader.cs` | Sends events to the web app in the background |
| `Assets/Scripts/FuelRowGate.cs` | Invisible line after each row of cubes that marks the row as passed |
| `Analytics/google-apps-script/Code.gs` | Stores events in a Google Sheet and serves them back as JSON |
| `docs/analytics/index.html` | The analytics page, published by GitHub Pages |

## One-time setup

1. Create a Google Sheet (for example "Fuel Frenzy Analytics").
2. In the sheet, open **Extensions > Apps Script**. Replace the contents of `Code.gs` with `Analytics/google-apps-script/Code.gs` from this repo, and save.
3. Click **Deploy > New deployment**, choose the type **Web app**, set **Execute as: Me** and **Who has access: Anyone**, then **Deploy**. Authorise it when asked. Google warns that the app isn't verified; choose **Advanced > Go to (project name)**.
4. Copy the **Web app URL** (it ends in `/exec`) and paste it in two places:
   - Unity: LoopRaceScene > **PlayerCar** > **Fuel Pickup Analytics** > **Endpoint Url**. Save the scene.
   - `docs/analytics/index.html`: the `ENDPOINT` line near the bottom of the file.
5. Make a new WebGL build into `docs/` and push, so that plays on GitHub Pages send data.
6. The analytics page is at `https://<github-user>.github.io/<repo>/analytics/`, for this repo `https://altitudedrawer.github.io/fart-fuel-racer-game/analytics/`. This assumes GitHub Pages serves the `docs` folder, as it does for the WebGL build.

If you change `Code.gs` later, publish the change with **Deploy > Manage deployments > Edit > Version: New version**. The URL stays the same.

Without an endpoint URL the game writes `Analytics/fuel_pickup_events.csv` instead (in the editor). The analytics page can open that file with **Load a CSV file**.

## How the numbers are defined

- Each row of cubes has one green (Diesel), one purple (Ethanol) and one brown (Gasoline, the car's own fuel) cube. A player can take at most one per row.
- A **pickup opportunity** is a row the player drives past while the bag has room and at least one cube is still there. The AI rival also takes cubes.
- **Collection rate** = rows where the player picked up a cube ÷ pickup opportunities.
- **A/B test:** each race uses a lane spacing picked at random from `Ab Test Lane Spacings` on the FuelPickups object (3.5 m and 4.5 m). The page compares the collection rate between the narrowest and widest spacing with a one-sided two-proportion z-test. To stop the experiment, empty that list; every race then uses `Lane Spacing`.
- Races played in the Unity editor are recorded with an editor platform and hidden on the page unless "Include editor test sessions" is ticked.

## Events

| Event | When |
|---|---|
| `race_start` | A race begins (records the lane spacing for that race) |
| `pickup_collected` | The player picks up a cube |
| `pickup_blocked` | The player hits a cube but the bag is full |
| `row_passed` | The player drives through the gate after a row |
| `race_end` | The race ends: win, AI win, time up, out of fuel, or abandoned |

| Column | Meaning |
|---|---|
| `session_id` | One race; Race Again starts a new one |
| `timestamp_utc` | When the event happened |
| `platform` | `WebGLPlayer` for GitHub Pages, `WindowsEditor` / `OSXEditor` for editor tests |
| `event` | One of the events above |
| `race_time_s` | Seconds since the race started |
| `lane_spacing` | Metres between cube centres in this race |
| `row`, `lane` | Which row (0–3) and lane (0–2) the cube was in |
| `fuel_type`, `color` | Gasoline / brown, Diesel / green, Ethanol / purple |
| `suitable` | 1 if the fuel matches the car |
| `collected_in_row` | `row_passed` only: 1 if the player took a cube from this row |
| `cubes_available` | `row_passed` only: cubes still in the row when the player arrived |
| `bag_full` | `row_passed` only: 1 if the bag had no room, so the row isn't counted as an opportunity |
| `race_result`, `won` | `race_end` only |

No personal data is collected. Anyone who has the web app URL can add rows to the sheet, which is acceptable for playtesting.
