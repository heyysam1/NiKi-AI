09 --- Recommended Build Plan

This plan is the authoritative sequential implementation roadmap for
Niki AI.

It preserves all completed and previously approved phases and defines
how the approved Desktop Pet specifications are implemented
incrementally without creating a second roadmap.

Roadmap Authority

This file is the authority for implementation order and phase
sequencing.

The following specifications define what must be implemented when a
phase reaches the relevant scope:

01_CORE_CONCEPT.md

02_FEATURES_AND_SCOPE.md

03_DESIGN_SYSTEM.md

04_ARCHITECTURE.md

05_SECURITY_AND_PERMISSIONS.md

06_AVOID_AND_GUARDRAILS.md

07_QA_TESTING.md

10_DESKTOP_PET_SYSTEM.md

11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md

12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md

10_DESKTOP_PET_SYSTEM.md is the parent Desktop Pet specification.

11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md is authoritative for
character identity, anatomy/layer structure, capabilities, motion
primitives, animation, personality integrity, expressions,
character-specific reactions, and character growth settings.

12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md is authoritative for
AI-directed contextual/spontaneous behavior, behavior composition, mood,
personality adaptation, familiarity/growth, expression intent,
priorities, cooldowns, budgets, and capability-aware behavior selection.

These supplemental specifications do not create separate phases and do
not override the core architecture, security, guardrails, QA
requirements, or this roadmap.

Phase 0 --- Repository foundation

Create: - solution; - projects; - test projects; - shared design
tokens; - logging; - configuration; - secure settings abstraction.

Verify: - clean build; - app launches; - test runner works.

No Desktop Pet-specific scope is pulled into this phase beyond
establishing abstractions that later phases can safely reuse.

Phase 1 --- Companion shell

Build: - transparent always-on-top window; - default bottom-right
position; - drag; - size; - opacity; - tray; - show/hide; - hotkey.

Add one placeholder 2D/pixel companion.

Verify all window behaviors before building AI logic.

Existing Phase 1 behavior remains intact.

Phase 2 --- Character runtime

Build: - sprite model; - state machine; - Niki; - at least two animal
companions; - idle/listening/thinking/working/happy/notification/sleep.

Verify: - animation throttling; - scale; - drag; - state transitions.

The Character Runtime must remain extensible for the later approved
character system.

The runtime architecture must support future character-specific
capabilities and articulated movement without replacing the core
character engine.

Do not pull the complete 11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md
implementation into this phase unless the existing Phase 2 scope
explicitly requires it.

Phase 3 --- Local task system

Build: - task model; - task repository; - task state machine; - task
history; - basic task detail view.

Verify persistence across restart.

Preserve clean task-state signals so later Desktop Pet behavior can
react to real task states without duplicating task logic.

Phase 4 --- AI provider abstraction

Build: - provider interface; - one provider implementation; - secure
API-key storage; - connection test; - simple chat.

Do not add multiple providers until the abstraction is stable.

Keep the provider boundary separate from character behavior. AI must not
directly manipulate the character renderer or WPF/OS controls.

Phase 5 --- Tool registry

Implement a few low-risk tools: - open app; - reminder; - search web; -
clipboard.

Add: - schema validation; - timeouts; - cancellation; - audit events.

Keep browser use Edge/Brave only.

Do not implement the Desktop Pet movement/behavior system here.

The Desktop Pet specifications are future scope at this point and must
not expand Phase 5.

Phase 6 --- Permission engine

Implement: - risk levels; - Allow Once; - Always Allow; - Deny; - audit
trail.

Test denial and cancellation before enabling autonomous mode.

All future Desktop Pet automation, contextual behavior, and AI-directed
behavior must remain subordinate to this permission architecture.

The character system must never bypass permission checks.

Phase 7 --- Scheduler and notifications

Build: - reminders; - recurring tasks; - native notifications; - result
popup.

Verify restart persistence.

Keep the Result Popup and notification surfaces separate from the
free-floating Desktop Pet.

Desktop Pet behavior may react to actual scheduler/notification events
later, but the Scheduler/Notification core must not depend on the
character renderer.

Preserve all verified Phase 7 behavior.

Phase 8 --- Browser automation

Use Edge or Brave.

Build: - search; - page read; - structured extraction.

Test: - prompt-injection handling; - browser failure; - timeout; -
cancellation; - unavailable browser; - malformed/unexpected page
behavior.

Browser content is untrusted data and must never override
system/developer instructions, permissions, security controls, or user
intent.

If Chrome is unavailable, ensure Edge or Brave serves as the active browser path.

Desktop Pet scope: - no broad Desktop Pet implementation is required
merely because browser automation exists; - browser/task state may later
provide low-risk contextual signals to the Behavior Director, but
browser content must not directly control character behavior.

Phase 9 --- Apps and Windows automation

Build: - app list; - recent apps; - UI Automation; - focus; - simple
interaction.

Do not add broad arbitrary OS access.

All sensitive actions must continue through the established permission
and validation architecture.

Desktop Pet integration --- Stage B: Desktop/window awareness

Implement the relevant subset of 10_DESKTOP_PET_SYSTEM.md that depends
on native application/window geometry:

safe desktop/work-area representation;

PetSurface abstraction or equivalent;

native top-level window observation;

target selection;

active-window following mode;

surface invalidation/recovery;

taskbar/work-area interaction where reliably supported;

multi-monitor handling;

DPI/scaling-aware positioning;

safe fallback when window/surface information is unavailable.

The pet may observe window geometry for movement.

It must not automatically read window contents or manipulate application
windows merely because they are detected.

Actual application control remains under Tool Registry + Permission
Engine.

Character system integration

Where required by the movement layer, connect the existing Character
Runtime to:

movement intent;

facing;

walk/run;

jump/fall/landing capability checks;

character-specific movement capabilities.

Do not assume every character supports every movement.

11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md remains authoritative for
capabilities and character identity.

Phase 10 --- Memory

Build: - short-term; - explicit long-term memory; - project memory; -
timeline.

Add memory controls.

Do not silently convert character interactions, desktop observations, or
pet behavior into permanent memory.

Desktop Pet integration --- Stage C foundation

Add only the memory/context integration required by approved behavior
specifications.

The Behavior Director may consume permitted low-risk context and
preference signals, but:

sensitive profiling is prohibited;

personality adaptation must be user-controlled;

no adaptive personality behavior is enabled merely because memory
exists;

memory controls remain authoritative.

The Desktop Pet may use relevant non-sensitive context such as task
state, notification state, active/idle state, work-session duration,
time-of-day where appropriate, and recent pet interaction, subject to
privacy and settings controls.

Phase 11 --- Widgets

Implement the 12 core widgets in small slices.

Build shared widget shell first.

Do not write 12 unrelated widget implementations.

Maintain separation between widgets and the Desktop Pet.

The pet may expose a compact interaction surface that opens existing
utility UI, but the pet must not become a widget container or persistent
widget panel.

Widget/task state may provide contextual signals to character behavior
where appropriate.

Phase 12 --- Voice

Add: - push-to-talk; - speech-to-text; - state synchronization; -
background execution.

Keep a text fallback.

Integrate voice state with the existing Character Runtime where
appropriate:

listening;

thinking;

talking;

waiting;

error;

completion reactions.

Do not create a second voice-driven character state machine.

Voice input/output must remain separate from the AI Behavior Director's
authority over behavior composition.

Foundational Application Lifecycle:
- Start with Windows: real user-controlled ON/OFF setting registering/enabling startup via registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), reusing the existing secure settings store.
- Desktop Pet Show/Hide: clear user-controlled Show/Hide independent of background assistant runtime; hiding the pet leaves background services and scheduler active; showing restores the pet surface without application restart.
- Close vs Background Behavior: user-controlled choice between Minimize to Tray (keeps assistant/background services active while UI/Pet is hidden) and Full Exit.
- Full Exit: explicit clean shutdown disposing scheduler, widget coordinator, voice, hotkeys, and tray with zero orphaned processes.

Phase 13 --- Workflows [COMPLETED]

Status: Fully implemented and verified.
- Multi-step sequential execution engine (`WorkflowEngine`) with timeouts and conditions.
- SQLite persistence (`workflows`, `workflow_runs`) via Migration V5; `workflow_runs` stores operational metadata only (run_id, workflow_id, status, current_step, timestamps, duration, sanitized status); never sensitive inputs/outputs/tools/payloads.
- 3 Seeded Workflows ("Start Work", "Prepare a Research Session", "End Workday") seeded in database, verified inactive on application startup (zero auto-executions).
- Triggers: Scheduled (reuses `ISchedulerService` and `ScheduledItem`), Hotkey (reuses `GlobalHotkeyManager`), Event (constrained strictly to `ITaskRepository.StatusChanged` lifecycle events; zero generic event bus).
- Security & Permissions: `PermissionEngine` remains sole security authority; `IToolExecutor` used for all tool actions; `RequiresApproval` acts as additional checkpoint; safe Phase 13 default of no automatic tool retry.
- PromptAgent strictly bounded: cannot mutate workflow definition graph, cannot alter permissions or bypass subsequent steps.
- Timeline integration: Reuses existing `ITimelineRepository` with operational metadata only (no raw data or sensitive payloads).
- Desktop Pet integration: Purely visual/behavioral reactions via `TaskLifecycleSignal` (`Working`, `Thinking`, `WaitingForApproval`, `TaskComplete`/`Happy`, `Error`, `Notification`). No task titles or "Done" text drawn above/on the pet.
- Runtime & Test Verification: 385 solution tests passing (100%), `--verify-phase13` passed cleanly with code 0, regressions for Phases 9–12 passed cleanly with zero warnings/errors and clean process exits.

Desktop Pet integration --- event-driven behavior signals

Allow approved workflow/task lifecycle events to provide contextual
signals to the Desktop Pet:

task started;

task waiting;

approval required;

task completed;

task failed;

workflow notification.

Character reactions must remain visual/behavioral.

Do not place arbitrary task titles, result summaries, or "Done" text
above the pet unless a later explicit product feature defines that
behavior.

The Result Popup remains a separate utility surface.

Phase 14 --- Screen awareness & AI/Natural Voice [COMPLETED]

Status: Fully implemented and verified.
- On-Demand Screen Awareness: `IScreenCaptureService` and `ScreenCaptureService` using Win32 and Phase 9 geometry (`Rectangle`, window handles) with strictly volatile in-memory image buffers.
- Zero Continuous Capture: Zero background capture loops or polling. Monotonically increasing `CaptureCount` asserts 0 captures during startup or unapproved execution.
- Gated Authorization: `CaptureScreenTool` (`capture_screen`) and `AnalyzeScreenTool` (`analyze_screen`) declare `ToolRiskLevel.Sensitive` (Risk Level 2), enforced exclusively by the existing `ToolExecutor` / `PermissionEngine` pipeline.
- User Privacy Gate: `ScreenAwarenessEnabled` boolean property failing closed immediately when disabled.
- Untrusted Data Boundary: Screen text and vision observations quarantined inside `=== UNTRUSTED SCREEN CONTENT START ===` and `=== UNTRUSTED SCREEN CONTENT END ===` blocks to prevent prompt injection.
- Strict Data Privacy: Captured bytes and `VisionAnalysisResult` remain ephemeral in memory; strictly prohibited from Timeline, `WorkflowRun`, Memory, `PetContext`, logs, or disk persistence.
- Generic Multimodal Vision: `IVisionProvider` decoupled from specific vendors, implemented via `CustomHttpVisionProvider`, `OpenAiVisionProvider`, and `MockVisionProvider`.
- Generic AI/Natural Voice: `ITtsProvider` decoupled from specific vendors, implemented via `CustomHttpTtsProvider`, `OpenAiTtsProvider`, `WindowsSapiTtsProvider`, and `PluggableTextToSpeechService`.
- Resilient SAPI Fallback: Automatic fallback to local Windows SAPI on network timeout, 4xx/5xx error, or missing credentials without caller exceptions.
- Native Audio Player: `WindowsAudioPlayer` providing concurrency serialization (`SemaphoreSlim`), cancellation, replacement, and clean disposal.
- Provider-Scoped DPAPI Credentials: Encrypted under `vision_credential_{providerId}` and `tts_credential_{providerId}` via `ISecureSettingsStore`.
- Preserved Orchestration: `VoiceService.cs` and `CharacterStateMachine` orchestration preserved untouched.
- Verification: 419 unit/integration tests passing (100%), `--verify-phase14` passed with code 0, and Phase 12/13 regression verification passed with code 0.

Phase 15 --- Character intelligence, adaptive behavior, and Desktop Pet completion [COMPLETED]

Status: Fully implemented and verified.
- Sole Authoritative Registry: Reconciled `CharacterRegistry` as the single runtime source with 8 runtime identity profiles. Niki and Dog/Biscuit are currently asset-backed/usable; the six approved mockup identities (`character-03-astronaut-cat`, `character-04-knight`, `character-05-orange-astronaut-cat`, `character-06-goth-girl`, `character-07-retro-boy`, `character-08-red-cap-adventurer`) have runtime identity and capability profiles registered, with their final visual assets belonging to Phase 16; legacy character slot `mochi` is legacy/deprecated and non-destructively hidden from active selection.
- Articulated Motion Primitives: 16 atomic primitives (`Idle`, `Blink`, `SlowBlink`, `LookLeft`, `LookRight`, `HeadTiltLeft`, `HeadTiltRight`, `Nod`, `ShakeHead`, `EarTwitch`, `TailWag`, `VisorCheck`, `ShiftWeight`, `Bounce`, `FistPump`, `Smile`) composed via `MotionSequence` and `MotionPrimitiveStep`.
- Visual Physics Separation: `MotionPhysicsSimulator` evaluates temporary visual `RenderTransform` offsets `(OffsetX, OffsetY, ScaleX, ScaleY)` only during active motion steps. Reset to exact identity on finish. Strictly decoupled from window Left/Top, PetSurface, and navigation coordinates.
- Zero Continuous Physics/Behavior Loops: On-demand simulation only; zero background timers running physics during idle.
- View-Only CompanionWindow: `CompanionWindow` hosts `ExpressionOverlayControl` and applies visual render transforms; zero behavior scheduling or orchestration inside the view.
- 100% Offline AI Behavior Director & Local Engine: Deterministic offline `LocalBehaviorEngine` handles all context, task, and user interaction plans without cloud/network dependencies.
- Capability & Constraint Validation: `CharacterCapabilityValidator` verifies anatomical capabilities and safely substitutes unsupported primitives (e.g. TailWag -> ShiftWeight).
- Bounded Priority Scheduling: `BehaviorExecutionCoordinator` enforces 7-level priority hierarchy, 45s spontaneous cooldown, and 15s expression cooldown.
- Personality Adaptation STRICT OFF Guarantee: Privacy-safe `PersonalityAdaptationService` with zero metric collection, zero calculation, and zero influence when disabled (default).
- Native Expression Composer: `ExpressionComposer` maps themes to non-intrusive symbol canvas overlays; zero text banners, zero task-title overlays, zero unsolicited voice.
- Reduced Motion Compliance: Full damping suppressing physics offsets to identity, reducing locomotion, and simplifying expressions.
- Phase 9 Movement Regressions Preserved: Safe-area clamping, active-window following, multi-monitor/DPI awareness, and no-focus-stealing guarantees intact.
- Verification: 463 tests passing across 13 test assemblies (100%), `--verify-phase15` passed all 14 checkpoints with exit code 0, `--verify-phase12`, `--verify-phase13`, and `--verify-phase14` regression suites passed with exit code 0.

This phase is the main integration phase for the advanced requirements
from:

10_DESKTOP_PET_SYSTEM.md

11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md

12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md

Do not create a parallel character engine or AI planner.

Stage A --- Pet movement foundation

Complete the remaining movement foundation where not already
implemented:

movement controller;

virtual desktop/work-area awareness;

safe surface representation;

walk/stop/facing;

jump/fall/landing where supported;

safe clamping;

drag integration;

movement interpolation;

deterministic tests;

graceful fallback when a target/surface becomes invalid.

The movement system must use the existing Character Runtime.

Character system implementation

Implement the approved character system incrementally:

approved character roster;

stable CharacterId mapping after asset inventory inspection;

removal/deprecation of the specified legacy character slots only
after verifying the actual asset inventory;

character profile;

character identity;

body/layer decomposition;

character-specific capability definitions;

motion primitives;

articulated full-body/component-level movement;

animation composition;

personality baseline;

emotional range;

mannerisms;

idle personality;

signature animations;

signature reactions;

character-specific success/failure/notification/greeting reactions;

character-native expression language;

expression/effect layer;

natural motion physics where appropriate;

inertia;

weight shift;

anticipation/follow-through;

secondary motion;

squash/stretch where visually appropriate;

safe animation fallbacks.

Do not implement generic movement by deforming the entire image or
relying only on edge/pixel deformation.

The system must preserve character identity and must not force
unsupported anatomy or capabilities onto a character.

Stage C --- Living-pet behavior

Implement bounded autonomous behavior:

calm idle/fidget behavior;

autonomous roaming;

short movement decisions;

playful reactions;

sit/rest/sleep;

contextual reactions to actual Niki states;

anti-annoyance rules;

user interaction reactions;

priority handling;

cooldowns;

interruption policy;

idle/resource budgets;

local fallback behavior when AI is unavailable.

Use event-driven triggers and bounded decision frequency.

The AI must not continuously think about the pet.

AI Behavior Director

Implement the architecture:

Niki AI / Context → Low-Risk Context & Preference Signals →
AI Behavior Director → Behavior Intent / Behavior Plan →
Character Identity + Capability Validation →
Motion Composer + Expression Composer →
Animation Engine / Expression Renderer → Desktop Pet

The Behavior Director may compose approved motion primitives into novel
sequences.

It must not directly manipulate:

WPF controls;

window handles;

processes;

files;

browser automation;

OS input;

privileged tools.

Before execution, validate:

character identity;

personality;

body capabilities;

requested primitives;

motion constraints;

personality consistency;

priority;

cooldown;

resource budget;

interruptibility.

Unsupported behavior must: - use a compatible fallback; - reduce the
sequence; - or return safely to idle.

Mood and personality

Implement the lightweight mood model where appropriate:

calm;

curious;

happy;

excited;

tired;

focused;

surprised;

confused;

disappointed;

sleepy;

playful.

Mood must not replace the character's base personality.

Personality Adaptation

Add:

Settings → Desktop Pet → Personality Adaptation

Values: - OFF; - ON.

Default behavior must follow the approved specification and explicit
product decision.

When OFF: - adaptive preference signals must not influence personality
selection; - the character follows its base identity/profile.

When ON: - only low-risk behavioral preference signals may influence
behavior; - adaptation remains bounded and explainable; - no sensitive
psychological profiling; - the character remains itself.

Familiarity / growth

Where Personality Adaptation is enabled:

Base Character Identity + Observed Low-Risk Preference Signals +
Interaction Feedback → Familiarity Profile →
Behavior Weight Adjustment

This must remain lightweight and user-controlled.

Expression Composer

Implement structured expression intent rather than arbitrary AI-drawn
UI.

The expression layer must use each character's native visual language.

Examples include: - space/sci-fi motifs for astronaut characters; -
heraldic/impact effects for the knight; - restrained symbols for dark
characters; - energetic bursts for energetic characters; -
character-appropriate feline effects for cat characters.

Behavior priority

Use the approved priority model:

User interaction

Explicit user-triggered character action

Important system/task attention

Notification attention

Contextual reaction

Spontaneous behavior

Idle behavior

Lower-priority behavior must not interrupt higher-priority behavior.

Definition of done for the integrated Desktop Pet layer

Verify:

transparent, borderless, free-floating character;

movement on safe desktop/work-area surfaces;

taskbar/window-edge behavior where reliably supported;

multi-monitor/DPI handling;

drag/click/double-click/context interaction;

pause/sleep/hide/settings;

actual Niki task/application states drive character reactions;

autonomous behavior is calm and cancellable;

no focus stealing;

no persistent utility UI around the pet;

no arbitrary task-status text above the pet;

AI-directed behavior respects capabilities, permissions, security,
and performance limits;

missing AI/API availability does not break the pet runtime;

reduced-motion support;

no regression in previously completed functionality.

Phase 16 --- Polish [COMPLETED]

Status: Fully implemented and verified.
- Sole Authoritative CharacterRegistry: Maintained `CharacterRegistry` as the single authoritative registry. `ReloadCharacters()` refreshes asset-backed animation manifests without mutating or losing any of the 8 authoritative identity profiles.
- Strict Asset Validation: `IsAssetBacked()` performs real referenced-frame validation on disk (resolving frame paths relative to `character.json` and asserting file existence and non-zero byte length). Niki and Dog/Biscuit validated true; the six registered candidate identities validated false and throw `InvalidOperationException` if attempted to be activated.
- Visual Physics Polish: Polished `MotionPhysicsSimulator` with smooth sinusoidal and exponential easing curves, subtle squash-and-stretch factors, and damped visual offsets. Physics remains strictly visual-only (zero modification to window coordinates or logical navigation position); returns exact identity transform under Reduced Motion.
- Zero Continuous Physics Loops: Physics calculated strictly on-demand during active motion steps and resets cleanly to identity.
- Frame Pacing & Timer Throttling: `CharacterAnimationController` and `CasualIdleController` fully throttle and stop tick timers when companion window is hidden or minimized (`SetThrottled(true)`).
- Native Expression Polish: `ExpressionComposer` polished with bounded durations [0.5s, 2.5s], subtle vertical floating offsets, and character-specific expression themes (`DarkReserved` -> `DarkOrb`, `Heraldic` -> `HeraldicMark`, `CelestialQuiet` -> `FocusSpark`).
- SpeechBubble Banner Removal: Completely removed legacy `SpeechBubbleBorder` text badge from `CharacterView.xaml` and `CharacterView.xaml.cs`.
- Active Roster & Deprecation: Niki and Dog/Biscuit remain the only active selectable characters. Mochi preserved as legacy identity profile (`GetIdentityProfile("mochi")`), hidden from active context menu.
- Reduced Motion Persistence & Propagation: Decoupled `ReducedMotion` from `SystemParameters.HighContrast`. Reused `ISecureSettingsStore` under key `Companion.ReducedMotion` with synchronous load at startup and asynchronous update. Propagated through `CharacterAnimationController`, `CasualIdleController`, and `AiBehaviorDirector`.
- Verification: 471 automated tests passing across 13 test assemblies (100%), `--verify-phase16` passed all 10 checkpoints with exit code 0, `--verify-phase12`, `--verify-phase13`, `--verify-phase14`, and `--verify-phase15` regression suites passed with exit code 0.

Now refine the complete product and the Desktop Pet system:

spacing;

motion;

icons;

empty states;

accessibility;

reduced motion;

performance;

animation timing;

transition quality;

character-specific visual polish;

interaction feedback;

context-menu polish;

settings clarity;

error states.

Do not change the product identity.

Do not use polish work to compensate for unresolved architectural or
functional problems.

Do not introduce heavy 3D rendering or GPU-heavy continuous effects.

The Desktop Pet remains lightweight and visually separate from utility
surfaces.

Phase 17 --- Hardening and release candidate [COMPLETED]

Status: Fully implemented, hardened, verified, and release candidate approved.
- Security & Permission Hardening: Fail-closed rule loading verified on store corruption; untrusted screen content boundaries (`=== UNTRUSTED SCREEN CONTENT START ===`) verified; secret redaction of OpenAI, Gemini, Anthropic keys verified in logs and prompt traces; Windows DPAPI user-scoped credential protection (`DataProtectionScope.CurrentUser`) verified without cross-process assumptions under same user context.
- Tool Safety & Browser Automation Fallback: Genuinely prohibited executables (`cmd.exe`, `powershell.exe`) fail closed with structured policy violations; multi-browser discovery supports Chrome when available, and safely falls back to Microsoft Edge or Brave when Chrome is absent; no blanket product-wide Chrome prohibition; no unauthorized automatic retries.
- Desktop Pet & Character Runtime Hardening: Verified 150x100 presentation size; nearest-neighbor pixel-preserving scaling; real frame on-disk decodability via `BitmapDecoder` for Niki and Dog/Biscuit; asset-less candidate identities remain registered but non-selectable (`IsAssetBacked = false`); non-activating window flags (`WS_EX_NOACTIVATE`, `Focusable = false`, `Topmost = true`).
- Deterministic Timer Throttling & Loop Checks: Animation frame pacing and casual idle timers stop completely when hidden/minimized (`SetThrottled(true)`); rapid throttle/unthrottle cycling verified without timer leaks or duplicate timers; `MotionPhysicsSimulator` guarantees zero continuous physics loops during idle and returns exact identity transform under Reduced Motion; Reduced Motion persists in `ISecureSettingsStore` and propagates to all subsystems.
- Voice / Screen / Storage Resilience: On-demand screen awareness privacy gate (fail-closed, 0 persistence); push-to-talk state transitions and cancellation verified; pluggable TTS provider falls back to local Windows SAPI on credential or network errors; SQLite database integrity verified via `PRAGMA integrity_check` = ok.
- Application Lifecycle & Deterministic Normal Shutdown: Windows startup toggle persistence verified; Desktop Pet show/hide independent of background runtime verified; clean resource disposal and deterministic normal shutdown verified with 0 orphan processes.
- Verification Pyramid: 481 automated tests passing across 13 test assemblies (100%), `--verify-phase17` passed all 12 checkpoints cleanly with exit code 0, `--verify-phase12` through `--verify-phase16` active regression suites passed with exit code 0, and 0 orphan processes confirmed.

Post-Phase-17 Product Recovery & Architecture Realignment (Master Implementation Plan v3.0) [COMPLETED]

Status: Fully implemented, architecturally realigned, and verified across all Tracks A through M.
- AgentOperator Security Boundary (Track E): Enforced absolute decoupling. `NikiAI.Agent` has zero references to `NikiAI.Security` or `PermissionEngine`. `AgentOperator` cannot predict approvals; every tool call reaches `ToolExecutor` which owns lookup, schema validation, permission checks, approval handling, execution, and audit.
- Structured Tool Result Contract & Self-Correction (Track E): `ExecuteAgentToolCallAsync` returns structured result contracts for success, validation failure, denial, approval wait, and execution failure. LLM invalid argument failures return structured diagnostics to the model in a bounded multi-turn conversation loop, enabling autonomous self-correction without crashing out of the loop.
- Task Lifecycle Integrity (Tracks E & G): Enforced canonical lifecycle `Draft -> Pending -> Running <-> Waiting / NeedsApproval -> Completed / Failed / Cancelled`. Removed false completion on LLM response or turn exhaustion; max-turn exhaustion transitions to `Failed`. Dynamic promotion preserves audit continuity and lifecycle state for long-running operations.
- Browser Agnosticism (Track D): Dynamic capability-based discovery and adapter registry (`IBrowserAdapterRegistry`, `IBrowserAdapter`, `ChromiumCdpAdapter`). Zero hardcoded browser whitelists or blacklists. Removed blanket Chrome prohibitions; search queries containing browser names are handled as standard user inputs. CDP automation is capability-verified at runtime.
- Provider Neutrality & DPAPI Persistence (Tracks C & K): Provider-agnostic tool calling abstractions (`ToolDefinition`, `AgentToolCall`, `AgentToolResult`, `AgentMessage`). `OpenAiCompatibleProvider` encapsulates OpenAI-specific serialization/deserialization. API credentials and endpoints persist via DPAPI (`ISecureSettingsStore`).
- MainWindow Application Shell (Track H): Implemented `MainWindow.xaml` as the official primary application shell with tabbed navigation (Chat Workspace, Task Manager, Workflows, Explicit Memory, and Provider Settings). `CompanionWindow` remains an independent floating 2D desktop companion.
- Voice Routing (Track I): Routed speech input through the conversational operator loop: `STT -> AgentOperator -> OperatorResponse -> TTS`.
- Desktop Pet Canonical Pipeline (Track J): Maintained the 8-stage pipeline (`Signals -> AiBehaviorDirector -> MoodEngine -> PersonalityAdaptationService -> CharacterCapabilityValidator -> ExpressionComposer / MotionPhysicsSimulator -> CharacterAnimationController -> Visual Output`). Task lifecycle signals act as environmental triggers, not static state mappings.
- Repository Hygiene & Harness Decoupling (Track B): Removed 469 tracked bin/obj artifacts from Git tracking. Updated `.gitignore`. Decoupled 3,550 lines of CLI verification harnesses from production `App.xaml.cs` into `src/NikiAI.App/Verification/PhaseVerificationHarness.cs`.
- Verification Suite (Track L): 491 automated tests passing across 13 test assemblies (0 failed, 0 skipped), up from 483 baseline. Build is 0 errors / 0 warnings. Runtime CLI harnesses for Phase 1, Phase 11, Phase 15, and Phase 17 pass cleanly with exit code 0.

Release-candidate verification

Verify:

clean machine setup;

no strict Chrome dependency (graceful Edge/Brave fallback);

Edge/Brave browser path;

secure configuration;

no secret leakage;

installer/startup behavior;

uninstall/cleanup behavior;

clean application shutdown;

no unresolved Blocker/Critical issues;

Desktop Pet does not become an unrestricted OS/UI controller;

character behavior remains bounded and resource-efficient;

Personality Adaptation OFF actually disables adaptive behavior.

Success rule

Do not skip phases just because code generation is fast.

Do not pull future-phase scope into an earlier phase merely because an
architectural dependency exists.

When a Desktop Pet capability is required by a later specification,
implement it in the phase assigned by this roadmap.

Preserve all verified functionality from completed phases.

Every phase must follow:

Inspect → Analyze → Plan → Implement → Build → Test → Runtime Verify → Regression Check

A feature is not considered implemented merely because its code or
specification exists. It must be verified at the appropriate phase.

Cross-phase Desktop Pet implementation rule

The Desktop Pet specifications are implemented incrementally, not as one
large feature drop.

The intended dependency flow is:

Phase 2 Character Runtime → Phase 9 Window/UI Automation →
Phase 10 Context/Memory Controls → Phase 12 Voice State Integration
→ Phase 13 Workflow/Task Signals → Phase 14 On-demand Context →
Phase 15 Character Intelligence + Desktop Pet Behavior →
Phase 16 Polish → Phase 17 Hardening/Release

10_DESKTOP_PET_SYSTEM.md,
11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md, and
12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md must therefore be read whenever
the current phase touches character runtime, desktop pet behavior,
movement, window awareness, contextual behavior, personality adaptation,
animation, or character expressions.

No phase may replace the authority of the core architecture, security,
permissions, guardrails, or QA specifications.