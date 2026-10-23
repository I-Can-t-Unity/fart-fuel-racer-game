# Revised Alpha analytics

Tracks fuel collection in the revised `AlphaRaceScene` and the procedural `LoopRaceScene`. Alpha retains its two authored rows of fuel cans. `FuelPickupSpawner` places those cans at a randomly selected 3.5 m or 4.5 m lane spacing and creates approach and exit gates. The tutorial still controls when the authored fuel group becomes visible.

## Release setup

1. Replace the existing Google Apps Script source with `Analytics/google-apps-script/Code.gs`.
2. Publish a new version of the existing web app: **Deploy > Manage deployments > Edit > Version: New version > Deploy**. Keep the existing `/exec` URL. Deploy the backend before releasing the new game: the previous backend accepts events but does not store the new columns or deduplicate retries.
3. The Alpha player's `FuelPickupAnalytics.endpointUrl` and the dashboard's `ENDPOINT` already point to the same existing web app. If changing services, update both (and the Loop scene if used).
4. Build WebGL with `MainMenuCanvas` first and `AlphaRaceScene` included. Use the Alpha destination `docs/alpha-progress-check/`, then publish the updated build and `docs/analytics/` through your normal GitHub Pages workflow. The checked-in WebGL binaries have not been rebuilt by this change.
5. Play the deployed Alpha, finish a race, and confirm its `build_id = alpha-analytics-v2`, `scene = AlphaRaceScene`, and `platform = WebGLPlayer` in the sheet and dashboard. Verify both lane spacings over multiple races.

The local Unity installation currently lacks the WebGL build support module; install that module for Unity 6000.3.23f1 through Unity Hub before rebuilding if it is still unavailable.

Dashboard: https://altitudedrawer.github.io/fart-fuel-racer-game/analytics/

## Metric definitions

- One session is one loaded race, including the opening countdown. Race Again creates a new ID. Counts are races, not unique people.
- A row encounter is the first forward pass of that row per race. Multiple car colliders, reversing, and repeated crossings do not create additional row events. A reset does not create a new session or reset row eligibility.
- The approach gate is 6 m before the cans; it snapshots bag capacity and available cans. Inactive tutorial cans are unavailable. The exit gate is 6 m after the row.
- A pickup opportunity is an exited row that had space in the bag and at least one available can at the approach gate, or on which the player collected fuel. Rows with no collection and a full bag or no available fuel are excluded.
- Collection rate = eligible rows with at least one successful collection / eligible rows. A second pickup that fills the bag does not invalidate that row. If Q frees a bag after a full approach, the row stays excluded unless a collection actually occurs.
- A row counts once even if several cans are collected. Fuel shares use the first successfully collected fuel in each eligible row. Raw pickup counts include all successful collections, including those before an exit gate was reached.
- Gasoline is straw yellow (#E8D66A), Diesel golden amber (#C58A32), and Ethanol pale blue (#A6DCEF). Fuel type, rather than color text, identifies categories. Suitability follows the selected car's fuel.
- Lane spacing is randomized once per race from `abTestLaneSpacings`. Empty this list for a fixed spacing. Authored row order and fuel identities stay unchanged.

## Events and schema

The first 16 columns retain the previous order. Apps Script expands the existing sheet with seven new columns; existing rows are preserved. Local editor CSVs are similarly expanded when written again.

| Event | Recorded when |
|---|---|
| `race_start` | Tracker starts after the scene loads |
| `pickup_collected` | A player can is successfully collected |
| `pickup_blocked` | A player touches an available can with a full bag |
| `row_passed` | First forward exit of a row |
| `mix_created` | Second pickup automatically creates a mix |
| `mix_used` | Q consumes the held mix |
| `fuel_dumped` | F successfully dumps a positive amount of tank fuel |
| `race_end` | Win, AI win, timeout, fuel stall, or normal departure from the scene |

| Fields | Meaning |
|---|---|
| `session_id`, `event_id` | Random race and event IDs; event IDs remain stable across upload retries |
| `schema_version`, `build_id`, `scene` | `2`, `alpha-analytics-v2`, and scene name; update the build ID and dashboard default for future experiment versions |
| `timestamp_utc`, `race_time_s`, `platform` | UTC time, seconds since scene load (includes countdown), and Unity runtime platform |
| `car_fuel`, `lane_spacing` | Selected car's fuel type and this race's spacing |
| `row`, `lane`, `fuel_type`, `color`, `suitable` | Pickup identity and suitability; `row_passed` includes first collected fuel, if any |
| `collected_in_row`, `cubes_available`, `bag_full` | Row success and approach snapshot; successful rows are always eligible |
| `mix`, `fuel_amount` | Mix enum for creation/use, amount removed for a successful dump |
| `race_result`, `won` | Normalized result text and 1/0 win indicator |

## Delivery and data separation

Events are saved to a PlayerPrefs outbox before sending. The uploader survives scene changes, sends batches of up to 50 as text/plain, retries unacknowledged requests with a delay increasing to 30 seconds, and removes a batch only after `{ "ok": true }`. Each queued item retains its destination. A later game event starts the uploader and restores pending events after a restart. Browser storage must remain available; clearing it removes pending uploads.

The backend locks writes and ignores already stored event IDs, including duplicates within one request. Legacy clients without event IDs remain accepted, without retry deduplication. The dashboard also deduplicates IDs and session/row pairs.

Normal menu/restart departures record `abandoned` before scene loading. Abrupt tab closure or a crash cannot guarantee a final `race_end`; compare started/ended sessions to identify incomplete data. Previously persisted events can resume uploading on the next play. Events never created at shutdown cannot be recovered.

The dashboard defaults to the revised Alpha build and Alpha scene. Editor sessions are excluded unless explicitly included. The all-builds view can include legacy records, whose first-fuel row attribution is incomplete. Keep these outside the Alpha comparison.

No names, emails, or persistent player identifiers are recorded by the game. The sheet and web app remain suitable for a small public playtest, not an authenticated production ingestion service.

## Validation

- **Automated dashboard/backend checks:** from the project root, run `node Analytics/tests/dashboard-backend.test.cjs`. Covers controlled 50% arithmetic, empty/full rows, event and row deduplication, color attribution, cohort filters, rendering, sheet migration, duplicate requests, legacy clients, and readback. Apps Script is exercised with an in-memory sheet substitute, not the deployed account.
- **Unity integration check:** save your scenes, exit Play mode, then choose **Tools > Analytics > Test Alpha Tracking**. It opens Alpha with its endpoint blank, enters Play mode, exercises actual pickup and gate callbacks, checks mechanic and session events in the local CSV, stops, and restores the previous scene. Report: `/tmp/alpha-analytics-smoke-test.txt`. Gate callbacks are invoked directly; this does not replace a physical driving check.
- **Local data:** with the endpoint blank, editor events append to `Analytics/fuel_pickup_events.csv` (ignored by Git). Use **Load a CSV file**, then enable editor sessions. A blank WebGL endpoint does not create a downloadable CSV.
- **Browser release check:** drive through both rows, deliberately miss a can, fill the bag, touch another can, use Q, dump with F, restart, and verify resulting rows. Observe gate alignment and verify that selected spacing remains on the road. Test loss conditions and abrupt closure separately.
- **Network release check:** disconnect during a race, reconnect with the game open, and verify that queued events arrive once. Then repeat with closing/reopening the browser and starting another race. Inspect both the browser and the sheet. Real browser delivery, storage persistence, and the live Apps Script deployment still require this release check.

For the Alpha playtest, start with 5–10 players and about two races each. Record participant counts separately. Compare pooled collection rate and mean per-race collection rate by spacing, along with raw opportunity counts, blocked pickups, mix use, dumps, and incomplete sessions. This is exploratory: repeated rows and repeated races by one player are related, so the dashboard no longer presents a two-proportion z-test or claims statistical significance.
