# Vehicle handling tuning

The player and opponent share `CarDriver` wheel physics. The opponent retains its own engine and target-speed tuning. Handling changes apply at runtime; no setup menu needs to be run.

## Controls and grip

- Steering now smooths normalized input before calculating wheel angles. Full steering takes the same time at low and high speed. The saved response of 180 degrees/second gives the LoopRaceScene cars roughly 0.21 seconds from centre to full input and 0.42 seconds for a complete reversal.
- Tire Grip defaults to 1.2 times the scene's existing forward and sideways stiffness. This multiplication uses the initial stiffness, so it does not accumulate each physics step.
- Downforce Acceleration defaults to 5 m/s² at the configured top speed. It increases with speed squared, is capped, and requires two grounded wheels. It does not pull airborne jump vehicles down.
- Yaw Stability defaults to 2.5. It damps rotation beyond the requested cornering rate, without setting the car's heading or deleting sideways collision velocity.
- Max Boost Grip defaults to 1.6. Cars with Hold Turn Radius Above Top Speed retain their normal radius through moderate boosts; beyond the grip cap they need wider turns.
- The existing race and sample scenes use a lower, less rearward centre of mass and a 15 m/s² cornering limit. Existing engine, fuel, brake and boost tuning remains in place.

## Contact handling

Non-trigger chassis colliders receive a temporary zero-friction, zero-bounce material. WheelColliders keep their own tire friction, and pickup triggers retain their materials. Original chassis materials are restored if the driver is removed. Vehicle solver iterations increase to 12 position / 4 velocity iterations, with a 3 m/s depenetration cap. Wheel simulation uses additional substeps.

Generated track walls use the `TrackBarrier` layer. Only WheelColliders exclude this layer, preventing suspension rays from treating the wall top as road; the chassis still collides normally. The global collision matrix is unchanged.

`VehicleBarrierContacts` adjusts lateral barrier contacts before the physics solver runs. For cars supported by at least two wheels and reasonably upright, it applies side-wall impulses at the car's centre-of-mass height and removes small vertical components from side-face normals. This prevents a sideways scrape from levering the chassis upward while preserving horizontal deflection and yaw. Top-face contacts, airborne cars, road contacts and car-to-car contacts retain their normal solver response. No vertical velocity cap is applied, and the earlier suspension, inertia and handling settings remain in place.

The callback handles both discrete and continuous collision detection, uses thread-safe caches, and unregisters cars and generated barriers on destruction. This follows Unity's [contact modification API requirements](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.ContactModifyEvent.html).

Both cars receive the same grounded stability. The AI suspends recovery assistance during vehicle contact and briefly after the last contact callback. Away from contact, its recovery uses forces and torque rather than assigning lateral velocity or rotating its body directly. Its existing stuck-car respawn remains available.

## Verification

`Assets/Editor/CarHandlingRegression.cs` runs actual WheelCollider and rigidbody simulation on a large flat road using a copy of the LoopRaceScene player car. It checks 50, 70 and 100 km/h turns and reversals, sideswipes, sustained inward steering against another car, rear impacts, airborne gravity and rotation, and the AI recovery contact guard.

Run in a disposable project copy with Unity 6000.3.23f1:

```text
Unity -batchmode -nographics -projectPath <project-copy> -executeMethod CarHandlingRegression.RunBatch -logFile <log-path>
```

The process exits with 0 on success and 1 on failure. Results are written to `Library/car-handling-regression.txt`. The check does not save scenes or assets. Input is supplied through `SetInput`; the AI contact guard is tested by setting its contact state explicitly. This isolates physics behavior but does not replace a full-track keyboard/gamepad playtest with fuel hunting and actual AI contact callbacks.

All 22 initial handling checks passed. During two seconds of sustained door-to-door pressure, both cars stayed upright and maintained at least 66 km/h. The final report is saved in `docs/vehicle-handling-regression.txt`.

The initial handling before/after comparison reduced peak sideways velocity fraction in the turn/reversal checks from 0.084–0.095 to 0.030–0.033. Sideswipe peak yaw fell from 0.542 to 0.197 rad/s. Airborne vertical velocity remained unchanged. The original AI recovery failed the contact guard check; the revised code passed.

For feel tuning, change Steering Response first, then Max Cornering Acceleration. Use Tire Grip and Downforce Acceleration for traction, and Yaw Stability for unwanted spin. Higher grip alone can make collision recovery and cornering feel abrupt.

## Barrier launch regression

The expanded suite exercises 288 scrapes: eight track locations, both barriers, 70/100/130 km/h, 5/15-degree approach angles, and initial body rolls of -5/0/5 degrees. Each case includes two seconds of steering toward the barrier. It checks both peak vertical speed and height gained from the settled position, and records any suspension contact with wall geometry. `RunBarrierBatch` runs just this matrix.

Before the barrier fix, three scenarios exceeded the launch limits (0.7 m rise or 3 m/s upward speed), and the wheels registered nine suspension contacts on barriers. Excluding barriers alone removed those contacts but did not resolve all rebound cases, so the final fix also changes the leverage of grounded lateral barrier contacts before the collision solver runs. Airborne gravity and all three rotation axes are tested separately.

All 26 full-suite checks passed, including all 288 barrier-impact scenarios. Worst body height gain was 0.09 m and worst upward velocity was 0.84 m/s; there were no suspension contacts on barrier geometry. The full suite also verifies that the chassis remains blocked by barriers and can land on top of one. These are deterministic isolated physics checks; a full-track playtest remains useful for feel and geometry outside the generated track.
