# 07 — QA, Testing, and Definition of Done

## Quality philosophy

The project must be developed as if a human QA engineer will manually verify every claim.

A green build is not enough.

Tests must verify not only that code works in isolation, but also that new behavior remains consistent with the existing architecture, security model, and approved product behavior.

## Definition of Done

A feature is complete only when:

1. code compiles;

2. automated tests pass;

3. relevant manual workflow is verified;

4. no obvious UI regression exists;

5. error path is handled;

6. cancellation path is handled;

7. permission path is handled where applicable;

8. logs contain no new unexplained errors;

9. documentation is updated if behavior changed;

10. existing verified functionality remains intact.

## Build verification

After meaningful changes:

- restore dependencies;

- build the solution;

- run relevant test projects;

- verify there are no new compiler warnings that indicate bugs;

- verify app startup.

Do not repeatedly rebuild unrelated projects without need.

When fixing an issue, prefer targeted verification of the affected subsystem followed by the relevant regression suite.

## UI test matrix

Minimum:

- 1366 × 768

- 1920 × 1080

- Windows display scaling above 100%

- light/dark desktop background

- character near each supported edge position

Verify:

- no clipping;

- no overlapping hitboxes;

- no accidental clicks through critical controls;

- correct always-on-top behavior;

- tray access;

- hotkey;

- focus order;

- companion remains visually separate from utility/result surfaces;

- no unnecessary focus stealing;

- no visual obstruction of normal user work.

## Character tests

Verify the character system supports the approved runtime behavior without assuming that every character has identical anatomy or capabilities.

Minimum verification should cover:

- idle behavior;

- listening;

- thinking;

- working;

- notification;

- approval;

- success;

- error;

- sleep;

- drag;

- size changes;

- opacity changes;

- pause;

- reduced motion;

- supported body-part movement;

- unsupported movement fallback;

- character-specific reactions;

- composed behavior sequences;

- contextual behavior;

- spontaneous behavior cooldown/limits;

- user-controlled personality adaptation when enabled;

- personality adaptation remains disabled when the setting is disabled.

Check that hidden/minimized character animation is suspended or reduced.

Check that character behavior does not create arbitrary OS/UI actions.

Check that the character does not become a replacement for result, notification, or task UI.

Do not require every character to perform every animation. Test according to the capabilities of the actual character.

## AI-driven character behavior tests

Where AI-generated character behavior is implemented, verify:

- behavior plans remain within approved character capabilities;

- unsupported actions receive a valid fallback;

- behavior remains consistent with the character's established identity;

- contextual behavior can override normal idle behavior when appropriate;

- spontaneous behavior respects cooldowns and resource limits;

- AI failure does not break the character runtime;

- local/default behavior remains available when AI/API generation is unavailable;

- AI behavior cannot directly manipulate WPF/OS internals;

- AI behavior cannot bypass permission/security controls;

- AI-generated visual reactions remain within the approved rendering system;

- user-controlled personality adaptation is respected.

The test should verify the behavior pipeline rather than testing a fixed list of every possible AI-generated reaction.

## Agent tests

Test requests such as:

- open VS Code;

- set a reminder;

- find web references;

- create a task;

- run a saved workflow;

- summarize a result;

- show task history.

Verify that agent decisions continue through the established tool validation and permission flow before sensitive execution.

## Tool tests

Every tool needs:

- valid input;

- invalid input;

- timeout;

- cancellation;

- unavailable dependency;

- permission denied;

- successful execution.

Where a tool integrates with Scheduler or Notification services, verify both the tool result and the resulting persisted/system behavior.

## Browser tests

Use:

- Microsoft Edge;

- Brave.

If Chrome is unavailable, use Edge or Brave.

Test:

- search;

- open result;

- read page;

- extraction;

- timeout;

- blocked navigation;

- browser unavailable;

- malformed page;

- prompt injection text on page.

External page content must remain treated as untrusted data.

## Windows UI Automation tests

Test:

- target app found;

- element found;

- element missing;

- duplicate candidates;

- inaccessible control;

- target window closed;

- user changes focus during execution.

Verify that UI automation does not silently fall back to unsafe arbitrary coordinate actions when a safer application/API/UI Automation path is available.

## Notification tests

Test:

- immediate notification;

- scheduled notification;

- canceled notification;

- duplicate notification;

- notification click-to-result;

- app closed/restarted before reminder time;

- notification history persistence;

- notification history retrieval;

- notification dismissal;

- notification content redaction;

- native notification dispatch;

- result popup dispatch;

- result popup remains separate from the Desktop Pet.

Verify that notification content never exposes secrets or sensitive internal data that should have been redacted.

## Scheduler tests

Test:

- one-time schedule;

- recurring schedule;

- relative delay;

- timezone/local clock handling;

- snooze;

- cancellation;

- dismissal;

- restart persistence;

- missed schedule after system sleep/reboot;

- startup recovery;

- recurring schedule advancement;

- pending schedule retrieval;

- scheduled item status transitions;

- duplicate trigger prevention;

- scheduler startup;

- scheduler shutdown.

Verify that scheduling is event-driven or timer-based rather than implemented as an unnecessary tight polling loop.

## Storage and persistence tests

Test persistence for all state that is explicitly required to survive restart.

Minimum:

- scheduled items persist;

- scheduled item status persists;

- recurring schedule state persists;

- notification history persists;

- notification read/dismissed state persists where applicable;

- corrupted/unavailable storage fails safely;

- database migrations apply correctly;

- existing data remains compatible after migration.

For restart tests:

1. create the state;

2. persist it;

3. stop the application cleanly;

4. restart;

5. reload the state;

6. verify behavior resumes correctly.

## Memory tests

Test:

- save;

- retrieve;

- edit;

- delete;

- clear all;

- disabled memory;

- accidental sensitive memory;

- corrupted record.

Character behavior must not silently turn normal user interaction into permanent memory.

## Permission tests

Test each risk level.

Especially:

- deny;

- allow once;

- always allow;

- expired permission;

- permission changed in settings;

- task retries after denial;

- sensitive action without approval;

- high-risk action requiring approval;

- invalid/stale stored permission;

- storage read failure;

- permission race conditions.

Verify that character or AI-generated behavior cannot bypass the established permission engine.

## Phase 14 screen awareness and natural voice tests

Verify the on-demand screen awareness subsystem and generic natural voice system:

1. **On-demand capture authorization**:
   - `capture_screen` and `analyze_screen` tools require Risk Level 2 (`ToolRiskLevel.Sensitive`) authorization enforced exclusively via `ToolExecutor` and `PermissionEngine`.
   - No secondary permission prompt or custom modal is created.
   - Denial at the permission prompt safely aborts tool execution.

2. **Privacy gate enforcement**:
   - Setting `ScreenAwarenessEnabled = false` immediately blocks screen acquisition via `IScreenCaptureService` with an explicit disabled message.
   - Screen capture counter accurately increments only on completed captures.
   - Zero continuous capture or background polling loops exist.

3. **Prompt injection defense**:
   - Analyzed screen content and OCR/text extractions are strictly quarantined between `=== UNTRUSTED SCREEN CONTENT START ===` and `=== UNTRUSTED SCREEN CONTENT END ===` markers.
   - Injected adversarial directives inside captured screen text are treated as passive data and never executed as system instructions.

4. **Zero-persistence privacy boundary**:
   - Raw image bytes and `VisionAnalysisResult` instances are strictly ephemeral.
   - Verified that no screen bytes or vision results enter Timeline, `WorkflowRun`, Memory, `PetContext`, application logs, or SQLite database.

5. **Audio player serialization and replacement**:
   - `WindowsAudioPlayer` serializes playback requests via `SemaphoreSlim(1,1)`.
   - Starting a new playback cleanly cancels and replaces any currently playing audio stream.
   - Explicit `Stop()` halts playback immediately without hanging.
   - System behaves gracefully when native audio endpoints/drivers are absent.

6. **Generic TTS and resilient fallback**:
   - Pluggable provider architecture (`ITtsProvider`) seamlessly routes between custom HTTP providers, OpenAI TTS, and local SAPI.
   - Network failures, HTTP 401/403/500 errors, or provider exceptions trigger automatic, silent fallback to `WindowsSapiTtsProvider` without crashing or surfacing exceptions to caller.

7. **Provider-scoped DPAPI credential security**:
   - Provider credentials are encrypted at rest using DPAPI via `ISecureSettingsStore` under keys `vision_credential_{providerId}` and `tts_credential_{providerId}`.
   - Credentials never appear in logs, error messages, model prompts, or diagnostics.

Automated verification command:
```powershell
dotnet run --project src/NikiAI.App -- --verify-phase14
```

## Task lifecycle tests

Test valid transitions and reject invalid ones.

Examples:

- Pending → Running;

- Running → Waiting;

- Waiting → Running;

- Running → Completed;

- Running → Failed;

- Running → Cancelled.

Test duplicate completion events.

Verify that a successful task is never reported as completed without an actual successful execution result.

## Long-running task tests

Simulate:

- 30 seconds;

- 2 minutes;

- cancellation;

- network loss;

- provider timeout;

- browser crash;

- app restart.

The task should return to a recoverable state.

## Failure messaging

Failure UI should answer:

- what failed;

- whether any partial work happened;

- what the user can do next;

- whether retry is safe.

Avoid raw stack traces in normal user UI.

Verify that failure states are not incorrectly represented as successful character/task states.

## Performance verification

Measure rather than assume:

- idle CPU;

- idle memory;

- animation CPU/GPU;

- widget refresh cost;

- browser session overhead;

- task concurrency;

- scheduler overhead;

- notification overhead;

- character animation overhead;

- AI behavior generation frequency.

Target values should be recorded as engineering goals, then checked on the target machine.

Character behavior must not consume continuous resources merely because the companion is visible.

Verify that:

- hidden/minimized animation is suspended or reduced;

- unnecessary animation recomposition is avoided;

- spontaneous behavior has bounded frequency;

- AI behavior generation does not become a continuous background loop.

## Regression prevention

Whenever a bug is found:

1. reproduce;

2. write a test;

3. fix;

4. rerun related tests;

5. verify the original reproduction;

6. note the cause.

Do not solve a regression by rewriting unrelated subsystems.

## Manual release checklist

- app launches;

- companion appears;

- tray works;

- chat works;

- reminder works;

- scheduler works;

- notification works;

- notification history works;

- task queue works;

- Edge/Brave browser automation works;

- browser automation gracefully falls back to available browsers (Edge or Brave) when Chrome is not installed;

- permission prompts work;

- memory controls work;

- widgets render;

- settings persist;

- app closes cleanly;

- no secrets appear in logs;

- on-demand screen awareness works with Risk Level 2 permission prompt and privacy gate;

- zero continuous screen capture or background polling loops run;

- screen capture and vision analysis data remain strictly ephemeral and unpersisted;

- AI and natural voice playback works with SAPI and cloud/custom TTS providers with graceful fallback;

- voice audio replacement and cancellation work cleanly without audio stutter or overlap;

- provider credentials are DPAPI-encrypted and never leaked to logs or prompts;

- Desktop Pet remains separate from result/notification surfaces;

- character behavior remains responsive without excessive resource usage;

- Desktop Pet Character Intelligence and 100% offline LocalBehaviorEngine verified;

- motion physics simulation verified to affect visual RenderTransform offsets only without altering window Left/Top or navigation coordinates;

- no continuous physics simulation or autonomous behavior loop running during idle;

- Personality Adaptation verified to perform zero collection, calculation, and behavioral weighting when disabled (default);

- character native expression overlay renders without text banners or unsolicited voice audio playback;

- all 14 checkpoints in `--verify-phase15` pass cleanly with exit code 0;

- Phase 16 Polish verified with all 10 checkpoints in `--verify-phase16` passing with exit code 0;

- Sole authoritative CharacterRegistry verified maintaining 8 authoritative identity profiles across ReloadCharacters();

- IsAssetBacked() verified validating real referenced frame files on disk (Niki and Dog true; candidates false and non-selectable);

- MotionPhysicsSimulator verified returning exact identity under ReducedMotion and zero alteration to window/surface coordinates;

- CharacterAnimationController and CasualIdleController verified stopping tick timers when hidden or minimized;

- ExpressionComposer verified applying theme mappings and bounded durations [0.5s, 2.5s] with zero text banners;

- ReducedMotion verified persisted in ISecureSettingsStore under Companion.ReducedMotion and decoupled from HighContrast;

- Full regression test suites `--verify-phase12`, `--verify-phase13`, `--verify-phase14`, and `--verify-phase15` passing cleanly with exit code 0;

- Phase 17 Hardening & Release Candidate verified with all 12 checkpoints in `--verify-phase17` passing with exit code 0;

- Security and Permission fail-closed integrity verified on store corruption;

- Untrusted content quarantine and secret redaction verified across logs, traces, and prompts;

- Tool safety and prohibited executables fail-closed verified; multi-browser discovery with Edge/Brave fallback and Chrome support verified;

- Asset validation verified on-disk via BitmapDecoder frame decodability (no fabricated assets);

- Deterministic timer throttling verified (hidden/minimized animation and casual idle timers stop, no duplicate timers created, zero continuous physics loops during idle);

- Privacy and memory integrity verified (0 captures at idle, volatile-only image buffers, SQLite PRAGMA integrity_check);

- Application lifecycle and deterministic clean shutdown verified with 0 orphan processes following normal exit;

- Full regression verification suite (`--verify-phase12` through `--verify-phase16`) passing cleanly with exit code 0;

- Full automated test suite passes 100% across all 13 test assemblies (481 passed, 0 failed, 0 skipped).


## Evidence rule

The implementation agent must not state that a workflow is verified unless it actually ran the workflow or a meaningful automated equivalent.

If a workflow was not tested, state that it was not tested.

Do not infer successful runtime behavior from a successful compilation alone.

## Bug priority

Use:

- Blocker

- Critical

- Major

- Minor

- Cosmetic

Do not hide a Blocker/Critical bug under a cosmetic polish pass.