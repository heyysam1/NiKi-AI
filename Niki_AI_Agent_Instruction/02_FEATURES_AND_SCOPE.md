# 02 — Features and Scope

This document consolidates the full approved feature set, including the original 35-point concept and later additions.

Character-specific identity, animation, capability, and AI-driven behavior details are defined by:
- `11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md`
- `12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md`

Those documents extend this scope and must not create a second, conflicting character or animation system.

## A. Desktop companion

1. Floating transparent companion window.

2. Always-on-top option.

3. Draggable positioning.

4. Bottom-right default placement.

5. Left/right/top custom placement.

6. Size presets.

7. Opacity control.

8. Pause/hide controls (user-controlled Show/Hide independent of background runtime; hiding the companion keeps background services and scheduler active without terminating the application).

9. Optional click-through mode.

10. System tray access.

11. Global hotkey to open the compact assistant.

12. Double-click to open full panel.

13. Single-click quick interaction.

14. Right-click context menu.

15. Smooth 2D/pixel animation.

16. Animation throttling when inactive: frame pacing and casual idle timers stop completely when the companion window is hidden or minimized.

17. The companion must remain a separate, free-floating character surface. Task result popups, notifications, widgets, and other utility UI must not box, replace, or visually modify the companion itself.

18. Character rendering must support articulated, body-part-level movement rather than relying only on whole-sprite translation, edge deformation, or simple idle bobbing.

19. Application lifecycle and startup controls:
- Start with Windows: real user-controlled ON/OFF setting registering/unregistering startup via registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), reusing existing secure settings.
- Close vs Background behavior: user-controlled choice between Minimize to Tray (maintains active assistant runtime) and Full Exit.
- Full Exit: complete clean shutdown disposing scheduler, widget coordinator, voice, hotkeys, and tray with zero orphaned processes.

20. Reduced Motion accessibility: dedicated `ReducedMotion` setting persisted in secure settings (`Companion.ReducedMotion`), decoupled from Windows high contrast, zeroing physics offsets, suppressing casual hops, and simplifying expression overlays.


## B. Character behavior and states

Required base states:

- Idle
- Walk
- Run
- Jump
- Listening
- Thinking
- Working
- Talking
- Happy
- Notification
- Sleep
- Error
- Waiting for Approval
- Task Complete
- Busy / Do Not Disturb

The runtime should expose a simple state/capability system so different character identities can use compatible shared state concepts while retaining character-specific behavior.

Character behavior must not assume that every character has the same personality or emotional style.

Each character should have:

- a distinct visual identity;
- a coherent personality direction derived from its approved character design;
- character-specific moods and emotional tendencies;
- characteristic idle behavior;
- unique movement language;
- signature animations;
- signature reactions;
- character-specific interaction responses;
- character-specific limitations and movement capabilities.

Character personality must remain visually and behaviorally coherent. A character should not routinely perform actions that contradict its established identity merely because a generic animation exists.

The character system must support full-body articulated movement. Where the character design permits it, independently movable parts may include:

- head;
- face/eyes;
- mouth;
- hair;
- ears;
- arms;
- hands;
- torso;
- clothing;
- accessories;
- legs;
- feet;
- tail;
- wings;
- props;
- equipment;
- character-specific mechanical or costume components.

The exact movable parts and capabilities are character-specific and are defined in `11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md`.

Movement should be composable rather than limited to a fixed list of complete canned animations. The AI behavior layer may compose approved movement primitives into new sequences while respecting the character's capabilities, personality, anatomy, and visual identity.

Examples may include:

- looking around;
- blinking;
- turning the head;
- shifting posture;
- stepping;
- walking;
- running;
- sitting;
- standing;
- leaning;
- reaching;
- waving;
- jumping;
- falling;
- recovering;
- interacting with an object;
- using a character-specific prop;
- reacting to the user's activity.

Unsupported movements must never be forced onto a character. The capability system should select an appropriate compatible fallback.

Characters may also use a separate graphical expression/effect layer for visual communication, including contextual symbols, particles, small effects, stickers, icons, or other lightweight expressive elements.

These effects must remain visually consistent with the active character and application design system.

The Desktop Pet must not display arbitrary task-status text such as "Done" above its head unless that behavior is explicitly defined by the active product feature.

The approved character roster consists of:

- Niki — retained existing character;
- Dog — retained existing character;
- six newly approved character identities represented by the current character mockups.

The previous legacy roster contains three additional characters that are to be deprecated/removed from the active roster. The exact asset removal must be verified against the project's actual character registry and asset inventory rather than inferred only from documentation.

The complete character identity, layer structure, capabilities, animation vocabulary, personality rules, and future expansion mechanism are authoritative in `11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md`.

AI-driven spontaneous and contextual behavior is authoritative in `12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md`.

In Phase 15, the Character Intelligence & AI Behavior Director is fully implemented 100% offline via LocalBehaviorEngine, with bounded priority scheduling, capability validation, temporary visual physics transforms strictly separated from window coordinates, native symbol expressions without text banners, and Personality Adaptation strictly OFF by default (zero metric collection, zero calculation, and zero influence).

## C. AI interaction

1. Text chat.

2. Voice input (Push-to-Talk and click-to-toggle with speech-to-text and Character Runtime state synchronization: Listening, Thinking, Speaking).

3. Optional voice output (speech synthesis response playback with audio enable/disable toggle and graceful text fallback).

4. Compact command bubble.

5. Full chat workspace.

6. Conversation history.

7. Structured task creation from natural language.

8. Follow-up questions when ambiguity is material.

9. Progress updates for long-running tasks.

10. Result summaries.

11. Result artifacts and source links where applicable.

AI interaction may also provide contextual signals to the Desktop Pet Behavior Director, when permitted by the user's settings and the applicable privacy/security controls.

The AI must not directly manipulate the WPF visual tree, operating system, files, or arbitrary UI state to create character behavior. AI-generated behavior must be translated into approved character behavior plans and validated through the character capability/animation layer.

## D. Real-time task execution

The user can give Niki a task and continue working elsewhere.

Example workflow:

- user assigns a task;
- agent marks task as Running;
- background worker executes it;
- user continues working;
- agent updates progress;
- when complete, notification popup appears;
- clicking the popup opens a result view;
- completed task remains in history.

Task lifecycle:

- Draft
- Pending
- Running
- Waiting
- Needs Approval
- Completed
- Failed
- Cancelled

Each task should have:

- title
- natural-language request
- structured goal
- created time
- start time
- optional due time
- priority
- current state
- progress where measurable
- actions taken
- result
- error information
- approval events
- links/artifacts

## E. Planning and tools

The tool registry should support categories rather than a single giant “computer control” function.

### Windows tools

- open application
- focus application
- close application
- enumerate running apps
- read clipboard
- write clipboard
- inspect UI
- interact with accessible UI elements
- keyboard input
- mouse input
- screenshot on demand
- read/write approved files
- create directories
- launch approved scripts
- safe process actions

### Browser tools

- open page
- search web
- inspect page content
- click
- type
- select
- navigate
- download
- upload
- extract structured data

**Browser support:** Niki AI is browser-agnostic. It can use any browser for which a compatible automation integration/adapter is available. Chrome, Edge, Brave, etc. are runtime examples, not a whitelist or product boundary. Dynamic capability detection via `BrowserAdapterRegistry` determines adapter selection.

**Agent orchestration:** `AgentOperator` coordinates conversational execution using provider-neutral native tool calling. Casual conversations remain transient in-memory; durably tracked tasks adhere to the canonical lifecycle (`Draft` -> `Pending` -> `Running` -> `Waiting` -> `NeedsApproval` -> `Completed` / `Failed` / `Cancelled`).

### Productivity tools

- reminders
- scheduled tasks
- focus timer
- notes
- task list
- workflows
- reusable macros

### AI tools

- summarization
- classification
- structured extraction
- brainstorming
- drafting
- comparison
- research synthesis

## F. Web research workflow

A long-running research task should support:

1. task creation;
2. browser session management;
3. source collection;
4. duplicate filtering;
5. source metadata;
6. extraction;
7. synthesis;
8. citations/links;
9. result artifacts;
10. user notification.

When the browser task fails, the agent must report the failure instead of pretending it completed.

## G. App control

Users should be able to say:

- “Open VS Code.”

- “Open my project folder.”

- “Close Spotify.”

- “Start my work setup.”

For workflows, use named actions rather than raw coordinates wherever possible.

## H. Memory

Memory is separated into:

- short-term conversational memory;
- long-term user preferences;
- project memory;
- task history;
- explicit user-saved facts.

Memory controls:

- view
- edit
- delete
- clear category
- disable memory
- export memory

The agent must never silently convert sensitive information into long-term memory.

### Character personality adaptation

The Desktop Pet may optionally adapt its behavior over time based on the user's permitted interaction patterns, preferences, and non-sensitive usage signals.

Examples may include:

- frequently used workflows;
- preferred working patterns;
- interaction style;
- recurring interests;
- preference for humor or expressive behavior;
- preference for quieter or more active companion behavior;
- recurring creative or productivity contexts.

This adaptation is **user-controlled** and must provide an explicit setting to enable or disable it.

Personality adaptation must not be treated as a hidden user-profile system.

It must:

- remain disabled when the user turns it off;
- respect applicable memory/privacy controls;
- avoid inferring or storing sensitive personal attributes;
- avoid making high-impact decisions based on inferred personality;
- avoid changing the character's core identity;
- adapt behavior and preferences rather than rewriting the character itself;
- remain explainable at a high level when relevant.

Character identity and personality remain character-specific. User adaptation modifies the character's behavioral tendencies within approved boundaries; it does not replace the character's established personality.

The detailed behavior adaptation model is defined in `12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md`.

## I. Scheduler and reminders

Support:

- one-time reminders;
- recurring reminders;
- date/time reminders;
- relative timers;
- task deadlines;
- “remind me when X finishes”;
- workday schedules;
- snooze;
- dismiss;
- notification history.

## J. Proactive assistance

Optional, user-controlled:

- break reminders;
- upcoming meeting reminders;
- incomplete task reminders;
- finished download notification;
- failed task notification;
- workflow suggestions.

Proactive behavior must have rate limits and a Do Not Disturb mode.

Desktop Pet spontaneous behavior must additionally respect its character identity, personality, capability system, behavior priority, cooldowns, and resource budget.

The AI Behavior Director may select contextual or spontaneous character behavior, but must not interrupt active user work unnecessarily.

## K. Notification and result popup

When a task completes, the agent should show a compact Windows notification plus an optional in-app result popup.

Popup content:

- character/avatar;
- title;
- completion state;
- one-sentence summary;
- key outputs;
- View Results;
- Open Artifact;
- Dismiss.

For example:

> Task Completed — Found 12 design references.

The result screen can include:

- summary;
- key findings;
- sources;
- files;
- action history;
- retry / rerun;
- export.

The result popup is a separate utility surface and must not become part of the free-floating Desktop Pet body or character rendering surface.

## L. Workflow system

Users can save commands as reusable workflows.

Examples:

- “Start work.”

- “Prepare a research session.”

- “Open my design tools.”

- “End workday.”

Workflow definition:

- trigger (Manual, Scheduled via ISchedulerService, Hotkey via GlobalHotkeyManager, or Event via ITaskRepository lifecycle);
- inputs (safe template substitution with `{{inputs.key}}`);
- ordered actions (ToolCall, Notification, Delay, PromptAgent);
- conditions (simple value matches and input checks);
- approvals (Workflow-level checkpoint evaluated via IPermissionEngine / IApprovalPromptHandler);
- retry policy (Safe Phase 13 default: no automatic tool retry);
- timeout (per-action and workflow-level timeouts with linked CancellationTokenSource);
- completion behavior (Silent, Notification, Popup).

Implementation notes (Phase 13):
- Persistence: SQLite `workflows` and `workflow_runs` tables (Migration V5).
- Privacy boundary: `workflow_runs` stores operational metadata only; never sensitive arguments, tool results, inputs/outputs, prompts, or clipboard contents.
- Seeded workflows: Three real workflows ("Start Work", "Prepare a Research Session", "End Workday") seeded in database, verified inactive on application startup.
- Desktop Pet integration: Purely visual/behavioral reactions via non-sensitive `TaskLifecycleSignal` (no task titles or Done text above pet).

## M. Widget system

Core V1 widgets: **12 total**

1. Clock

2. Calendar

3. Weather

4. Quick Note

5. Tasks

6. Reminders

7. System Monitor

8. Music Control

9. Clipboard

10. Focus Timer

11. AI Task Progress

12. Research / Web Results

Widget categories:

- System
- Productivity
- AI
- Information
- Media

Widget access:

- system tray;
- desktop widget launcher;
- hotkey;
- widget shelf;
- drag-and-drop positioning.

Widget rules:

- compact by default;
- consistent grid;
- one design language;
- no random accent colors;
- optional glass, solid, or minimal surface treatment using the same design tokens.

## N. Customization

### Character

- character selection;
- name;
- skin;
- outfit;
- accessories;
- expression pack;
- animation speed;
- optional character behavior/personality adaptation toggle.

Character-specific capabilities, personality, animation vocabulary, movable body parts, and signature reactions must not be treated as generic customization options. They are defined by the character system.

The character system must support future character additions without requiring the existing character engine to be replaced.

### Desktop

- position;
- size;
- opacity;
- always-on-top;
- click-through;
- theme.

### AI

- provider;
- model;
- API key;
- temperature/behavior where supported;
- voice.

### Behavior

- personality;
- proactive behavior level;
- notification intensity;
- approval strictness;
- wake/idle behavior;
- character personality adaptation on/off.

When personality adaptation is disabled, the character should continue using its established native personality and approved behavior model without user-adaptation signals.

## O. Multiple AI providers

Use a provider abstraction.

Examples:

- OpenAI-compatible provider

- Anthropic-compatible provider

- Google/Gemini-compatible provider

- OpenRouter-compatible provider

- local model provider

The UI should not assume a single vendor.

API keys should be stored securely, not as plaintext in application logs or source code.

## P. Local AI

Optional future capability:

- local model endpoint;
- routing of low-risk/simple tasks;
- cloud routing for complex research or planning;
- user visibility into which provider handled a task.

## Q. Screen awareness

On-demand only by default.

Flow:

request

→ screenshot permission

→ capture

→ vision analysis

→ answer/tool selection

Do not run continuous screenshot streaming unless explicitly enabled.

Character behavior may use permitted contextual application/task signals where appropriate, but continuous screen capture must not be introduced merely to animate the Desktop Pet.

Implementation notes (Phase 14):
- On-Demand Capture: `IScreenCaptureService` and `ScreenCaptureService` capture active window, primary monitor, or custom region using Phase 9 geometry (`Rectangle`, window handles) with strictly volatile in-memory image buffers.
- Zero Background Polling: Zero continuous screen capture loops or background capture threads. Monotonically increasing `CaptureCount` asserts zero idle capture operations.
- Permission Pipeline: `CaptureScreenTool` (`capture_screen`) and `AnalyzeScreenTool` (`analyze_screen`) declare `ToolRiskLevel.Sensitive` (Risk Level 2), enforced exclusively by the existing `ToolExecutor` / `PermissionEngine` pipeline.
- Privacy Gate: `ScreenAwarenessEnabled` boolean property failing closed immediately when disabled.
- Untrusted Content Boundary: Screen text and vision observations quarantined inside `=== UNTRUSTED SCREEN CONTENT START ===` and `=== UNTRUSTED SCREEN CONTENT END ===` blocks to prevent prompt injection.
- Zero Persistence: Raw image bytes and `VisionAnalysisResult` remain strictly ephemeral in volatile memory; never persisted into Timeline, `WorkflowRun`, Memory, `PetContext`, logs, or database.
- Multimodal Vision Providers: Generic `IVisionProvider` contract implemented via `CustomHttpVisionProvider`, `OpenAiVisionProvider`, and offline `MockVisionProvider`.
- Generic AI/Natural Voice: Generic `ITtsProvider` contract implemented via `CustomHttpTtsProvider`, `OpenAiTtsProvider`, and `WindowsSapiTtsProvider`, orchestrated with `PluggableTextToSpeechService`, native `WindowsAudioPlayer`, and automatic resilient fallback to local Windows SAPI.
- Provider-Scoped Credentials: Secure DPAPI storage under `vision_credential_{providerId}` and `tts_credential_{providerId}` via `ISecureSettingsStore`.

## R. Activity log

Every tool execution records:

- task ID

- timestamp

- tool name

- sanitized arguments

- result state

- approval state

- duration

- error if any

Sensitive data should be redacted.

Character behavior events may be logged only at an appropriate level for debugging/analytics and must not expose sensitive user content unnecessarily.

## S. Undo

Where an action is reversible, expose an Undo action.

Examples:

- move/rename file;

- change a setting;

- create a reminder;

- add a tag.

Do not present fake Undo for irreversible operations.

## T. Performance targets

Engineering targets, to be measured rather than assumed:

- low idle CPU usage;

- bounded idle RAM;

- no continuous polling loop unless unavoidable;

- animation suspended when hidden;

- event-driven notifications;

- minimal dependencies;

- no heavy 3D engine;

- no continuous GPU-heavy effects.

The articulated character system must remain resource-bounded even when the AI Behavior Director composes dynamic movement.

AI-driven character behavior must use:

- event-driven triggers where possible;
- cooldowns;
- priority handling;
- bounded animation frequency;
- idle behavior budgets;
- fallback local behavior when AI/API access is unavailable.

“Free-form” character behavior means dynamic composition from approved capabilities and primitives. It does not mean unrestricted execution, unrestricted system access, or unlimited animation generation.

## U. Accessibility

Support:

- keyboard navigation;

- visible focus states;

- readable contrast;

- scalable text;

- reduced motion;

- screen-reader-friendly labels for controls;

- tooltip alternatives;

- clear error states.

Reduced-motion settings should also reduce or disable non-essential Desktop Pet movement and graphical effects.

## V. First-release scope

The first implementation should prioritize:

1. companion window;

2. chat;

3. reminders;

4. task queue;

5. notifications;

6. app launch/control;

7. browser search using Edge/Brave;

8. permission system;

9. activity log;

10. basic memory;

11. widgets;

12. character selection.

The character system must be architected from the beginning so that approved character identities, articulated movement, character-specific reactions, and future AI-driven behavior can be added without replacing the core companion architecture.

Do not attempt every advanced capability in one pass. Build in stable vertical slices.

Detailed character implementation and AI-driven behavior composition should follow:

- `11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md`
- `12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md`

These are supplemental specifications for the Desktop Pet layer, not replacements for the core product scope, architecture, security, or guardrails.

## W. Release Candidate Status (Phase 17)

Phase 17 completes the planned roadmap, hardening and validating the entire feature set across 12 release checkpoints:
- Security fail-closed integrity, user-scoped DPAPI protection, and untrusted boundary quarantine.
- Multi-browser discovery supporting Chrome (when available) with resilient fallback to Edge/Brave.
- Articulated 2D Desktop Pet presentation (150x100), NearestNeighbor scaling, and deterministic timer throttling.
- Resilient local offline fallbacks for speech, behavior, and screen awareness.
- Deterministic clean normal shutdown with zero orphan processes.