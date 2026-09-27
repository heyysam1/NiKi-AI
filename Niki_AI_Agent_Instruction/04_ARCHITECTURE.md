04 — Technical Architecture
Architecture rule

The LLM is the planner/decision-maker. It is not the operating system controller.

Never build:

LLM → arbitrary OS access

Build:

LLM → structured tool call → runtime validation → permission gate → tool executor → observation → result

Recommended Windows-first stack

Primary language:

C#

Desktop UI:

WPF initially

Runtime:

.NET current supported release

Windows integration:

Win32 where required;
Windows App SDK where useful;
Windows UI Automation;
Windows notifications;
system tray integration;
native process APIs.

Browser automation:

Playwright or an equivalent mature browser automation layer

Allowed browsers:

Browser-agnostic capability model. Any installed browser with a verified compatible automation adapter (e.g., Chromium DevTools Protocol / CDP or WebDriver). Edge, Chrome, Brave, etc. are candidate runtime examples, not a fixed whitelist.

Local data:

SQLite

Optional semantic memory:

local embeddings/vector index later;
do not introduce a vector database in the first slice unless necessary.
Module structure

Suggested solution structure:

NikiAI.sln

src/
  NikiAI.App/
  NikiAI.Core/
  NikiAI.Agent/
  NikiAI.Tools/
  NikiAI.Automation/
  NikiAI.Browser/
  NikiAI.Memory/
  NikiAI.Scheduler/
  NikiAI.Notifications/
  NikiAI.Character/
  NikiAI.Widgets/
  NikiAI.Voice/
  NikiAI.Workflows/
  NikiAI.Security/
  NikiAI.Storage/

tests/
  NikiAI.Core.Tests/
  NikiAI.Agent.Tests/
  NikiAI.Security.Tests/
  NikiAI.Automation.Tests/
  NikiAI.Scheduler.Tests/
  NikiAI.Voice.Tests/
  NikiAI.Workflows.Tests/

Names may be adjusted, but responsibilities should stay separated.

App layer

Responsible for:

windows;
navigation;
desktop companion;
system tray;
settings;
application lifecycle (Start with Windows, Close vs Tray, Clean Exit);
UI binding;
theme tokens;
user interaction.

It should not contain model-prompt logic.

Core layer

Responsible for:

domain models;
task states;
workflow models;
tool contracts;
agent events;
shared interfaces.

Examples:

AgentTask
TaskStatus
ToolCall
ToolResult
ApprovalRequest
WorkflowDefinition
MemoryItem
Agent layer

Responsible for:

prompt assembly;
provider abstraction;
planning;
tool-call parsing;
task orchestration;
retry policy;
result synthesis.
Tool layer

Each tool should have:

stable ID;
description;
JSON input schema;
validation;
permission level;
timeout;
cancellation;
result schema;
audit metadata.

Example conceptual interface:

ITool

  Id
  Description
  InputSchema
  RiskLevel
  ExecuteAsync(...)
Permission layer

Every tool call passes through the permission engine.

Conceptual flow:

ToolCall
  ↓
ValidateSchema
  ↓
RiskClassification
  ↓
PermissionPolicy
  ↓
Approve / Deny / Ask
  ↓
Execute
Agent task orchestration

Use a persistent task queue.

Recommended state transition:

Draft
  → Pending
  → Running
  → Waiting
  → NeedsApproval
  → Running
  → Completed

Failure path:

Running → Failed

User cancellation:

Pending/Running/Waiting → Cancelled

Illegal transitions should be rejected.

Background execution

Prefer asynchronous event-driven workers.

Avoid:

tight polling loops;
one timer per task;
constant screen scraping;
continuous high-frequency UI inspection.

Use:

one scheduler service;
event-driven task signals;
cancellation tokens;
bounded concurrency.
Browser subsystem

The browser layer should:

dynamically discover installed browsers;
verify adapter compatibility based on runtime capabilities;
fail clearly if no compatible adapter is available;
isolate browser sessions;
support cancellation;
capture page metadata;
prevent accidental navigation into unrelated tasks.

Do not hardcode a single browser or maintain an arbitrary whitelist; select compatible adapters via capability verification at runtime.

Windows automation subsystem

Preferred order:

Native application API, when available.
Windows UI Automation.
Browser automation for browser tasks.
Keyboard/mouse simulation as fallback.
Screenshot/vision assistance when explicitly enabled.

Coordinate-only automation should be the last reasonable fallback.

Vision and Screen Awareness subsystem

On-demand screenshot analysis only by default.

Architecture:

Request
→ ToolExecutor / PermissionEngine pipeline (Risk Level 2: Sensitive)
→ Privacy gate check (ScreenAwarenessEnabled)
→ IScreenCaptureService on-demand capture (Phase 9 geometry / volatile in-memory bytes)
→ IVisionProvider (CustomHttpVisionProvider / OpenAiVisionProvider / MockVisionProvider)
→ Untrusted content boundary quarantine (=== UNTRUSTED SCREEN CONTENT START/END ===)
→ Ephemeral memory-only lifecycle (zero persistence in Timeline, WorkflowRun, Memory, or database)
→ Planner / Tool result

Do not continuously stream the screen or poll in the background.

Memory subsystem

Tables:

memory_items
projects
tasks
task_events
workflows
tool_runs
approvals
notifications
settings

Keep memory user-controllable.

Scheduler

A central scheduler should manage:

reminders;
recurring tasks;
delayed execution;
notification timing;
retry windows.

Persist scheduled items so they survive app restarts.

Notification subsystem

Use native Windows notification functionality where practical.

Notification payload should link back to a task/result route inside the app.

Voice subsystem

Responsible for:

push-to-talk and click-to-toggle audio capture;
speech recognition abstraction (System.Speech.Recognition / SAPI with device fallback);
pluggable speech synthesis provider abstraction (`ITtsProvider`, `CustomHttpTtsProvider`, `OpenAiTtsProvider`, `WindowsSapiTtsProvider`);
resilient fallback to Windows SAPI on network timeout, 4xx/5xx error, or missing credentials;
native audio player (`WindowsAudioPlayer`) providing concurrency serialization (`SemaphoreSlim`), cancellation, replacement, and clean disposal;
provider-scoped DPAPI credential resolution (`tts_credential_{providerId}`);
character runtime state synchronization (Listening, Thinking, Speaking, Idle);
text fallback and headless execution;
preservation of existing `VoiceService.cs` and `CharacterStateMachine` orchestration;
non-blocking background processing.

Workflows and Task Lifecycle subsystem

Responsible for:

multi-step sequential workflow execution and timeout enforcement;
safe template argument substitution (`{{inputs.key}}`);
triggers: scheduled (`ISchedulerService`), hotkey (`GlobalHotkeyManager`), and task lifecycle events (`ITaskRepository.StatusChanged`);
operational-only execution history persistence (`workflow_runs`);
non-sensitive task lifecycle signal hub (`ITaskLifecycleSignalHub` / `TaskLifecycleSignal`) broadcasting to UI and character runtime;
safe Phase 13 default of no automatic tool retry;
strict permission pipeline adherence via `IToolExecutor` and `PermissionEngine`.

Character runtime

Character rendering and behavior must remain decoupled from agent task state.

The character system should be data-driven and extensible, so adding or evolving a character does not require rewriting the core runtime.

The runtime should separate:

character identity;
personality profile;
visual/asset definition;
body-part capabilities;
animation primitives;
composed behaviors;
expressions and reactions;
contextual behavior;
optional user-adaptive personality;
rendering.

Character behavior must not be limited to a fixed list of predefined animation clips. The runtime should support composing behaviors from approved movement and expression primitives.

Full-body movement

Characters should be treated as fully movable 2D entities, rather than sprites whose outer pixels merely deform or shift.

Where the character design supports it, independently controllable components may include:

head;
eyes;
mouth/facial features;
neck;
torso;
arms;
hands;
legs;
feet;
tail;
ears;
wings;
hair;
clothing/accessories;
other character-specific movable parts.

The exact movable-part set must be determined from each character's actual design and assets. Do not assume every character has the same anatomy.

Movement should support natural coordination between body parts, including:

walking;
running;
jumping;
sitting;
standing;
leaning;
turning;
looking;
gesturing;
stretching;
falling;
recovering;
character-specific actions.

The animation system should support both reusable primitives and dynamically composed sequences. A character may therefore perform different movement combinations without requiring a separate hardcoded animation for every possible behavior.

Character-specific behavior

Characters must not share an identical personality or reaction profile by default.

Each character should have a distinct:

personality;
emotional range;
behavioral tendencies;
preferred reactions;
movement style;
idle behavior;
expression style;
interaction style.

Behavior should remain consistent with the character's visual identity and established personality. Avoid combinations that feel contradictory or arbitrary unless the behavior is intentionally contextualized.

Character-specific capabilities should determine which movement primitives and behaviors are available to that character. Unsupported movements must use an appropriate fallback rather than forcing anatomically or visually incorrect animation.

AI-directed character behavior

The character runtime may receive high-level behavior plans from the AI behavior system.

The AI may compose approved character capabilities into contextual or spontaneous behavior sequences, but it must not directly manipulate WPF, OS controls, arbitrary files, or other system resources.

Conceptually:

AI / Context
    ↓
Behavior Plan
    ↓
Character Capability Validation
    ↓
Animation / Expression Composition
    ↓
Character Runtime
    ↓
Rendered Character

The behavior system should remain event-driven and resource-bounded. Characters must continue to function with local fallback behavior when AI-driven behavior generation is unavailable.

In Phase 15, this is realized via `AiBehaviorDirector`, `LocalBehaviorEngine` (100% offline, deterministic heuristic generator), `CharacterCapabilityValidator` (anatomical capability verification and primitive substitution), and `BehaviorExecutionCoordinator` (priority scheduling, cooldowns, step runner, and temporary visual transform physics). `MotionPhysicsSimulator` produces temporary visual render transform offsets only, leaving window/surface coordinates under `PetMovementController`. `PersonalityAdaptationService` strictly collects zero metrics and performs zero calculations when disabled (default).

In Phase 16 (Polish), `CharacterRegistry` is established as the sole authoritative registry providing dynamic manifest refresh via `ReloadCharacters()` while strictly preserving all 8 authoritative identity profiles, coupled with real on-disk referenced-frame validation via `IsAssetBacked()`. Legacy `SpeechBubbleBorder` text badge is completely removed in favor of pure non-intrusive symbolic expression overlays via `ExpressionComposer` and `ExpressionOverlayControl`. `MotionPhysicsSimulator` adds smooth easing curves while preserving strictly visual-only RenderTransform offsets. `ReducedMotion` is decoupled from `SystemParameters.HighContrast` and persisted via `ISecureSettingsStore` under `Companion.ReducedMotion`. Frame pacing and idle timers fully throttle/stop when hidden or minimized.

User-adaptive personality

The character system may support optional adaptation based on user interaction patterns, preferences, and explicitly permitted behavioral signals.

This adaptation must be:

user-controlled;
disabled by default unless explicitly enabled by the user;
reversible;
privacy-aware;
bounded to character behavior;
independent from security permissions.

When enabled, the system may gradually adapt behavioral tendencies to relevant user preferences and interaction patterns rather than attempting to imitate the user completely.

The character must retain its original identity and personality foundation even after adaptation.

Expressions and auxiliary visual elements

Character expression is not limited to body animation.

The runtime may support dynamically generated or selected auxiliary visual elements such as:

reaction symbols;
small stickers;
speech/visual bubbles;
celebratory elements;
contextual icons;
emotion indicators;
character-specific graphical effects.

These elements should be generated or composed through the approved visual system rather than requiring a unique hardcoded implementation for every possible reaction.

Auxiliary elements must remain visually consistent with the character and product design system and must not obstruct the user's work.

Widget runtime

Each widget should have:

ID;
title;
category;
size options;
configuration;
update policy;
view model;
permissions if needed.

Avoid independent polling timers for every widget.

Design tokens

Keep all visual constants in one place:

colors;
spacing;
radii;
typography;
icon sizes;
elevation;
animation timings.

No hardcoded one-off colors in individual views unless they are semantic state tokens.

Secrets

API keys must never be:

committed to source;
printed to logs;
shown in task history;
included in screenshots;
returned in prompts.

Use Windows-protected storage or a secure secret mechanism appropriate to the application.

Error handling

Every external boundary should have:

timeout;
cancellation;
structured error;
user-facing summary;
developer diagnostic path.

Never swallow exceptions without recording a useful diagnostic event.

Observability

Development builds should support:

structured logs;
correlation/task IDs;
tool timing;
error category;
safe debug traces.

Production logs must redact secrets and sensitive content.

Dependency philosophy

Prefer:

platform APIs;
mature libraries;
small dependency count;
pinned versions;
documented reasons for every major dependency.

Do not add an entire framework to solve a small problem.

Performance strategy

Character:

efficient 2D/sprite or layered rendering;
no real-time 3D;
pause animations when hidden;
adaptive animation frequency;
bounded behavior generation;
avoid unnecessary full-body recomposition when no visible change is required.

Widgets:

event-driven refresh;
adaptive refresh intervals;
suspend when hidden if appropriate.

Agent:

background worker pool;
bounded concurrency;
cancellation support.

Browser:

reuse sessions where safe;
close idle pages;
avoid unnecessary screenshots.
Future cross-platform path

Do not design the core domain so tightly around WPF that all business logic becomes UI-specific.

The goal is Windows-first now, while keeping:

agent core;
tools;
memory;
scheduler;
security

portable enough for a later Tauri/Rust or another client layer if desired.

Phase 17 release candidate architecture notes

- Release candidate hardening verified through unified CLI routine (`--verify-phase17`) executing all 12 checkpoints across real production execution paths without altering core contracts or bypassing security controls.
- Fail-closed security architecture verified: corrupted or throwing rule repositories default to an empty cache requiring explicit interactive approval for sensitive tools.
- Multi-browser discovery supported: Niki AI is browser-agnostic; browsers and adapters are selected dynamically based on verified runtime capabilities.
- Credentials hardened using user-scoped Windows DPAPI (`DataProtectionScope.CurrentUser`) without making cross-process claims under the same user context.
- Application lifecycle finalized: deterministic clean shutdown disposes all background workers (scheduler, widgets, voice, hotkeys, tray, timers) cleanly on normal application and CLI exit paths, leaving 0 orphaned background processes.
- Security & Tool Execution Boundary: `ToolExecutor` is the sole execution and authorization boundary. `AgentOperator` never queries or references `PermissionEngine`. `ToolExecutor` performs registry lookup, schema validation, `PermissionEngine` evaluation, user approval prompts, execution, and audit logging.
- Canonical Task Lifecycle: `Draft` -> `Pending` -> `Running` -> `Waiting` -> `NeedsApproval` -> `Completed` / `Failed` / `Cancelled`. Casual conversations remain transient; background or workflow tasks stay in `Running` or `Waiting` until genuine completion.
- Desktop Pet Runtime Pipeline: Operator / Context / Task signals -> `AiBehaviorDirector` -> `MoodEngine` -> `PersonalityAdaptationService` -> `CharacterCapabilityValidator` -> `ExpressionComposer` / `MotionPhysicsSimulator` -> `CharacterAnimationController` -> Visual Output. Task and status signals are environmental inputs, not hardcoded animations.