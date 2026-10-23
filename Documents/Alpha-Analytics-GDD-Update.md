# Analytics updates for the revised Alpha GDD

Add an Analytics and Playtesting section after Detailed Design, and update the existing gameplay descriptions to match the revised Alpha scene. These notes describe implemented tracking and a proposed playtest; they do not claim that user testing or deployment has already happened.

## Correct the current gameplay descriptions

- **Logline and win conditions:** distinguish the revised Alpha tutorial course from the older loop race. Alpha currently has no AI opponent reference, no checkpoint requirement, and a 30-second race duration. Describe the loop scene separately if it remains part of the deliverable.
- **Fuel pickup:** replace four shuffled rows of cubes with two authored rows of fuel cans for Alpha. Each contains one of each fuel. The row's lane spacing is randomly selected once per race: 3.5 m or 4.5 m. Fuel order is authored rather than shuffled in this scene.
- **Palette:** Gasoline = straw yellow (#E8D66A), Diesel = golden amber (#C58A32), Ethanol = pale blue (#A6DCEF). Replace old brown/green/purple references, including the Toxic Sludge recipe. Matching the selected car's fuel gives a refuel; other fuel gives a boost.
- **Bag and Q control:** the second pickup automatically makes a mix. Q uses the held mix and frees the bag. Remove instructions to press Q once to mix and a second time to use. Apply this correction to Controls, core mechanics, both matrices, and the core-loop diagram.
- **Fuel drain:** Alpha currently includes a base drain in addition to speed and throttle drain; remove the absolute claim that time never affects fuel. Describe the actual scene settings if documenting numeric tuning.
- **Core-loop diagram:** drive and burn fuel → choose a fuel lane → collect → automatically combine the second pickup → choose when to use Q → repeat or dump fuel for a boost.

## Suggested Analytics and Playtesting section

### Research question

Can first-time players collect the fuel they intend to collect and understand when to use their automatically created mix? Does increasing lane spacing from 3.5 m to 4.5 m improve fuel collection on the revised Alpha course?

### Primary metric

Fuel pickup collection rate is the number of eligible rows with at least one successful collection divided by the number of eligible rows passed. A row is eligible if the player had room in the bag and at least one fuel can was available at the approach gate, or if a pickup actually occurred before the exit gate. Full-bag and unavailable rows without a collection are excluded. Each row counts once per race; taking multiple cans from a row does not increase row success beyond one.

The approach gate is 6 m before the row and the exit gate is 6 m after it. This is an operational measure of pickup opportunity, rather than proof that a player saw or consciously chose a fuel can. A pickup without a subsequent exit remains in raw pickup counts but does not enter the row collection rate.

### Supporting measures

Record raw pickups by fuel type, blocked touches with a full bag, mix creation and use, fuel dumps and amounts, race outcomes, and started versus ended sessions. Record the selected car's fuel type to interpret matching and nonmatching collections. Fuel shares use the first collected fuel per eligible row. Mix-use and dump counts indicate mechanic engagement; they do not prove understanding or strategic intent. Ask players what they intended to collect and why they used Q or F.

### Experiment and sample

Randomly assign 3.5 m or 4.5 m spacing at race start while keeping the remaining Alpha layout and mechanics consistent. Assignment can produce unequal group sizes. Start with 5–10 participants playing approximately two races each; record participant counts in facilitator notes because session IDs identify races, not people. Let participants play without coaching, observe steering and fuel choices, and ask short questions afterward.

Report opportunities, successful rows, pooled collection rate, and mean per-race collection rate for each spacing. Exclude zero-opportunity races from the per-race mean and report incomplete sessions. Treat the comparison as exploratory; do not claim statistical significance from related row observations or repeated races by one participant.

### Instrumentation and reliability

The game records race_start, pickup_collected, pickup_blocked, row_passed, mix_created, mix_used, fuel_dumped, and race_end. Events include random session/event IDs, UTC timestamps, platform, scene, build ID, selected car fuel, lane spacing, and relevant row, fuel, mix, amount, and outcome fields.

Unity sends batches to Google Apps Script, which stores events in Google Sheets. The web dashboard filters the revised Alpha build and scene and excludes editor tests by default. Uploads are saved locally, retried after failures, and deduplicated by event ID on the updated backend. Normal departures record abandonment; abrupt tab closure or crashes can leave a session without an ending event. No names, email addresses, or persistent participant IDs are collected by the game.

### Validation and completion criteria

Before recruiting participants, verify that four eligible row records with two successful collections produce 50%, excluded rows do not change that rate, duplicate callbacks or retried uploads do not increase counts, and restarting starts a new session. Test the authored Alpha scene in Unity and confirm that browser events appear in the sheet and dashboard. Check real driving through gates, both spacings, loss outcomes, offline recovery, and tab closure.

Implementation checks have passed in the Unity editor and automated dashboard/backend tests. Deployment of the updated Apps Script, a fresh WebGL build, and the real browser release checks are still required before claiming end-to-end validation.

### Results to add after the playtest

Include the playtest date, build ID, number of participants and races, opportunities and collected rows for each spacing, pooled and mean per-race rates, blocked pickups, mix and dump usage, missing ending events, dashboard screenshots, observations, and the resulting design decision. Until those observations exist, label this section Planned Alpha playtest and do not invent results.

## Other GDD edits

- Add the analytics dashboard to Links: https://altitudedrawer.github.io/fart-fuel-racer-game/analytics/.
- Add the instrumentation work to Weekly Progress with the actual contributor and commit after committing; do not invent a commit hash.
- Keep the playable Alpha link but rebuild its WebGL files before describing it as the instrumented version.
- Update screenshots and matrices that still show the previous palette, cube models, loop layout, four rows, AI race, or manual mixing as the revised Alpha implementation.
