# Shortcut HUD testing
Owner: Soham
Scene tested: SohamHUDTest
Environment: Unity Editor
Date: 2026-10-29

## Verified in the Editor
- Rival display changes from NO MIX to HYBRID.
- Rival display returns to NO MIX after the mix is spent.
- Entering without Super Fart displays JUMP NEEDS SUPER FART.
- Successful activation displays SUPER FART JUMP!
- Player mix icon clears after successful activation.
- Race Again resets the visible player bag, rival display and messages.
- Race Again restores fuel to 100%.
- Instruction panel explains the recipe, Q and saving the mix.
- Sign uses yellow text on a dark background.

## Pending checks
- Integrate prefabs into the shared race scene with Subhanjana.
- Check sign readability at racing speed in the browser.
- Check HUD at 1280x720 and 1920x1080, including fullscreen.
- Verify shortcut access resets after Race Again.
- Verify rival display for Premium, Super Fart and Toxic Sludge.

## Bug tracker
Record issues here; pending checks are not confirmed bugs.

| ID | Steps to reproduce | Expected | Actual | Owner | Status |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

## Integration notes
- Place RivalMixPanel and JumpInstructionsPanel under RaceHUD.
- Place SuperFartJumpSign beside the shortcut approach.
- Test-scene sign position: (174.7, 0.12, -53.47).
- Test-scene sign rotation: (0, 35, 0); scale: (1.5, 1.5, 1.5).
- Recheck placement if the track changes.
- Existing gate messages were tested; no gate logic was changed.

# Friday observation sheet
Copy this section for each participant.

Participant ID:
Build/commit:
Browser and window size:
Selected car:

Observe without coaching:
- Can they explain how to craft Super Fart?
- Do they understand Q spends the mix immediately?
- Do they notice the sign before choosing a route?
- Do they deliberately save a mix for the jump?
- Can they explain the rival mix display?
- Do they understand success and missing-mix messages?
- Does Race Again leave any stale UI or shortcut access?

Route chosen:
Jump attempts and outcomes:
Confusing moments or exact comments:
Bugs and reproduction steps:
Suggested improvement:
