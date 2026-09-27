# Niki AI — Desktop Pet System Specification

## Status

Supplemental product specification for the Niki AI Desktop Pet system.

This file defines the overall Desktop Pet product behavior and system boundaries. It does not replace the existing project specification files.

Implementation must follow the existing build plan and the phase appropriate to each feature.

Existing functionality, architecture, security rules, design system, completed phases, and verified behavior must remain intact.

The Desktop Pet system builds on the existing Companion Shell and Character Runtime rather than creating parallel implementations.

---

## Purpose

Niki AI is not only an AI operator with an avatar.

The companion should also behave like a lightweight **desktop pet / desktop creature** that lives naturally on the Windows desktop while remaining useful as the visible surface of the assistant.

The supplied reference video demonstrates the target interaction concept: a small 2D/pixel creature lives directly on the desktop, stands on the taskbar or other usable desktop surfaces, moves around, reacts to the desktop environment, can follow/approach active windows, and exposes a compact interaction menu when requested.

The target is therefore:

**Niki = AI assistant + persistent lightweight desktop pet + interactive character.**

The Desktop Pet is a presentation and interaction layer around the established Niki AI systems.

It must reuse existing:

- Companion Shell;
- Character Runtime;
- Character Registry;
- Animation Runtime;
- task system;
- settings system;
- scheduler/notification system where applicable;
- tool architecture;
- permission engine;
- logging;
- test infrastructure.

It must not create duplicate versions of these systems.

---

## 1. Non-Negotiable Product Rules

1. The companion remains a **transparent, borderless, free-floating character**.

2. No persistent card, container, panel, toolbar, header, status box, speech bubble, or button row may surround the pet.

3. The visible pet itself is the primary persistent companion surface.

4. The pet must remain lightweight. This is a 2D/pixel or lightweight layered character system, not a 3D engine.

5. Existing verified behavior must remain intact unless a later approved specification explicitly changes it.

6. Pet behavior must never interfere with normal Windows interaction.

7. The user must be able to move, pause, hide, disable, or otherwise control the pet through the existing application/settings architecture:
   - Hiding the Desktop Pet surface hides the visible companion window while keeping the Niki AI background runtime, scheduler, and assistant services active. Showing it restores the pet surface without restarting the application.
   - Closing the application window respects user-controlled close behavior (Minimize to Tray vs Full Exit).
   - Full Exit cleanly terminates the pet surface and all background services without orphaned processes.

8. Pet behavior must be deterministic enough to test while still feeling organic during normal use.

9. Pet animation must remain compatible with reduced-motion preferences.

10. The pet must never fake an AI action, task completion, tool result, or system state through animation alone.

11. Actual task/application state comes from the underlying system.

12. The Desktop Pet must never bypass:

- tool validation;
- permission checks;
- security controls;
- task lifecycle rules;
- memory controls;
- existing automation boundaries.

13. The Desktop Pet must not become an unrestricted OS controller.

14. Autonomous character behavior must remain bounded, cancellable, and resource-aware.

15. Motion physics simulation (`MotionPhysicsSimulator`) must affect temporary visual render transform offsets only (`_characterTranslateTransform` and `_characterScaleTransform` on `CharacterDisplay.RenderTransform`) and must NEVER modify `CompanionWindow.Left`, `CompanionWindow.Top`, `PetSurface`, or logical navigation coordinates. Offsets reset strictly to identity upon motion completion.

16. Zero continuous physics loops or always-running autonomous behavior loops during idle. Physics is evaluated strictly on-demand during active motion steps.

17. Personality Adaptation must remain strictly OFF by default, performing zero metric collection, calculation, storage, or behavioral weighting when disabled.

18. Character expressions use character-native visual symbol overlays (`ExpressionOverlayControl`) only — zero persistent text banners, zero task-title overlays, and zero unsolicited voice audio playback from spontaneous pet behavior.

19. Legacy speech bubble/status text badge is completely removed from `CharacterView`; the pet communicates visually and expressively without attached text frames.

20. When the companion window is hidden or minimized, frame pacing and casual idle timers stop completely (`SetThrottled(true)`) to eliminate background CPU/GPU usage.

21. `ReducedMotion` accessibility preference is decoupled from high contrast and persisted via `ISecureSettingsStore` (`Companion.ReducedMotion`), suppressing visual physics and simplifying expressions.

---

## 2. Relationship to the Existing Character Runtime

The Desktop Pet System builds on the existing Character Runtime.

It does not replace:

- Character Registry;
- Character State Machine;
- Animation Controller;
- character capability system;
- rendering system.

The Desktop Pet layer adds:

- desktop/environment context;
- movement;
- surface awareness;
- spatial behavior;
- user interaction;
- desktop-specific reactions.

Character identity, anatomy, animation primitives, character-specific capabilities, and detailed animation behavior are defined by the dedicated character system.

AI-driven spontaneous and contextual behavior is handled by the dedicated behavior-director layer.

This file should not duplicate those systems.

---

## 3. Character State and Desktop Behavior

Existing character states may include:

- Idle;
- Walk;
- Run;
- Jump;
- Listening;
- Thinking;
- Working;
- Talking;
- Happy;
- Notification;
- Sleep;
- Error;
- WaitingForApproval;
- TaskComplete;
- Busy.

These states describe meaningful character/application states.

The Desktop Pet layer adds environmental behavior around those states.

### Example relationship

| Environment / Application condition | Character behavior |
|---|---|
| No event for a while | Idle + contextual fidget |
| Moving across a valid surface | Walk / Run |
| Short transition between compatible surfaces | Jump / transition behavior when supported |
| Falling from an invalid surface | Fall / recovery behavior when supported |
| Approaching a relevant application window | Movement toward target |
| Arriving at target | Idle / Listening / Thinking according to actual application state |
| AI is processing | Thinking / Working |
| AI is speaking/responding | Talking |
| User interaction succeeds | Brief positive reaction |
| User requests sleep/pause | Sleep / paused state |
| Serious task/tool failure | Error |
| Approval required | WaitingForApproval |
| Task completed | Appropriate completion reaction |

The application/task state remains authoritative.

Desktop movement must not overwrite a meaningful application state simply because the character is moving.

For example:

- a character may walk while the underlying state is Working;
- a character may approach a window while remaining in Thinking;
- a character may return to its resting position without changing the actual task state.

---

## 4. Desktop Pet Movement Model

The pet should support lightweight 2D movement.

### 4.1 Free movement

The character may move within the desktop working area when autonomous movement is enabled.

Movement must:

- remain inside safe screen bounds;
- avoid invalid system regions unless explicitly supported;
- use smooth position interpolation during ordinary movement;
- support appropriate facing/orientation;
- support walking and faster movement where the character capability allows it;
- stop naturally;
- preserve subtle idle motion when stationary.

Movement capabilities must be determined by the actual character.

Do not assume every character supports identical movement.

### 4.2 Screen boundaries

Use actual Windows virtual-desktop/work-area information rather than hard-coded screen dimensions.

Support:

- single-monitor setups;
- multiple-monitor setups;
- mixed monitor resolutions;
- different DPI/scaling values;
- monitors positioned left/right/up/down relative to one another;
- monitor changes while the application is running.

The pet must never remain stranded outside all valid work areas after a display topology change.

### 4.3 Taskbar interaction

The taskbar is a meaningful visual surface in the target behavior.

Where reliably supported, the pet may:

- stand near/on the taskbar region;
- walk along an eligible taskbar/desktop-bottom surface;
- sit or idle there;
- react to changes in usable geometry.

Do not hard-code the taskbar to the bottom edge.

Windows taskbar configuration may vary.

If reliable taskbar-surface detection is unavailable, use the Windows work-area boundary as the safe fallback.

Do not pretend full taskbar physics exist when the required geometry is unavailable.

---

## 5. Window and Surface Awareness

A major part of the desired pet experience is environmental awareness.

### 5.1 Surface abstraction

Use a domain abstraction such as `PetSurface` rather than scattering raw coordinate logic throughout the application.

A surface should describe, at minimum:

- screen/monitor identity;
- X/Y position;
- width/height;
- surface type;
- whether the surface is currently usable;
- whether the pet may stand/walk on it;
- relevant window relationship where applicable.

Possible surface types:

- DesktopWorkArea;
- TaskbarArea;
- ApplicationWindowTopEdge;
- ApplicationWindowBottomEdge;
- ApplicationWindowLeftEdge;
- ApplicationWindowRightEdge;
- Custom/Fallback.

The actual implementation may use equivalent names when an existing abstraction already provides the same responsibility.

### 5.2 Application windows

The pet may detect visible top-level application windows and use their geometry as environmental information.

Prefer:

- Windows-native APIs;
- window enumeration/geometry APIs;
- accessibility/UI Automation metadata where appropriate.

The pet may:

- approach a visible window;
- stand near a window edge;
- walk along an eligible edge;
- reposition when the target moves/resizes;
- return to a stable desktop/taskbar position when the target disappears.

The pet must not enter, manipulate, or control application windows merely because it can detect them.

Window awareness is a visual/environment feature.

Actual application control continues through the existing tool, automation, validation, and permission architecture.

### 5.3 Active-window following

The character may support configurable behavior similar to the reference concept where it moves toward the current or selected desktop context.

Possible modes:

- Follow Active Window;
- Stay on Current Surface;
- Roam Desktop;
- Stay in One Place;
- Sleep / Pause.

The initial implementation should use conservative deterministic behavior.

Autonomous roaming and follow behavior can be expanded incrementally through the established behavior system.

Never move the pet unexpectedly while the user is actively dragging or directly interacting with it.

---

## 6. Autonomous Pet Behavior

The pet should feel alive without consuming meaningful CPU time.

Autonomous behavior must use the existing character/behavior architecture rather than introducing a second autonomous state machine.

Possible low-level behaviors include:

- stand idle;
- blink/fidget;
- look left/right;
- turn around;
- walk a short distance;
- run a short distance;
- pause and observe;
- sit/rest;
- jump when supported;
- return to a preferred resting surface;
- approach an active/selected window when that mode is enabled.

These are behavior capabilities, not a requirement that every character support every action.

### 6.1 Behavior selection

Behavior selection may be:

- deterministic;
- context-driven;
- AI-directed;
- locally selected from safe behavior primitives.

The final behavior must always respect:

- character capabilities;
- user settings;
- reduced-motion settings;
- performance limits;
- application state;
- security boundaries.

AI-generated behavior must never directly manipulate WPF or Windows APIs.

### 6.2 Anti-annoyance rules

The pet must not:

- constantly cross the screen;
- jump continuously;
- repeatedly steal focus;
- permanently cover important UI;
- move during direct manipulation;
- generate unnecessary notifications;
- consume excessive CPU to appear alive;
- repeatedly interrupt focused work.

Autonomous behavior must be pausable globally.

### 6.3 Context-sensitive activity

The pet may react to real Niki AI events:

- task started → appropriate working reaction;
- task waiting for approval → attention/waiting reaction;
- task completed → brief contextual completion reaction;
- notification → notification reaction;
- AI listening → listening reaction;
- AI thinking → thinking reaction;
- error → error reaction;
- long idle period → calm casual activity.

These reactions must be based on actual application state.

The pet must not claim success merely by playing a success animation.

---

## 7. Interaction With the User

### 7.1 Pointer hover

Existing hover reactions must remain.

Additional behavior may include:

- looking toward the pointer;
- small attention animation;
- brief reaction before returning to the previous behavior.

Do not create continuous high-frequency animation merely because the pointer remains over the character.

### 7.2 Mouse press

Existing press/squash interaction should remain where supported by the character.

A click must not accidentally become a drag unless the pointer actually moves beyond the configured drag threshold.

### 7.3 Dragging

The user must be able to drag the pet freely.

During dragging:

- autonomous movement pauses;
- the character uses the appropriate drag posture/reaction;
- the transparent companion window follows the pointer;
- final position is clamped to a valid monitor/work-area region;
- previous behavior resumes after a short settling period.

### 7.4 Click / double-click

The companion remains the visual entry point to Niki AI.

A single click may open a small transient interaction surface or intended quick interaction surface.

A double-click may open the dedicated task/chat/application window already established by the project.

Do not create a persistent UI shell around the pet.

### 7.5 Context menu

A compact context menu may expose controls such as:

- Chat / Open Niki;
- Current task / task status;
- Follow Active Window;
- Roam / Stay;
- Sleep / Wake;
- Change character;
- Pet size;
- Opacity;
- Always on Top;
- Hide to Tray;
- Exit.

Only expose actions that are actually implemented.

Menu actions must call existing application services.

Do not embed business logic directly inside the pet view.

---

## 8. Follow / Window Behavior

The target behavior should feel like the pet is aware of what is happening on the desktop.

A robust implementation can use the following conceptual flow:

1. Observe the current desktop/window environment at a low frequency.

2. Identify safe candidate surfaces.

3. Select a target according to the current behavior mode.

4. Plan a short movement path.

5. Move the character using the existing movement/animation runtime.

6. Re-evaluate the environment periodically.

7. If the target disappears or becomes invalid, stop safely and select a valid fallback.

Do not continuously scan the desktop at high frequency.

The pet should not require screen capture for ordinary movement or window geometry awareness.

---

## 9. Z-Order and Interaction Safety

The companion window should remain visually above appropriate desktop content while avoiding disruptive focus behavior.

Requirements:

- no focus stealing during autonomous movement;
- clicking the pet may intentionally bring relevant Niki UI to the foreground;
- autonomous movement must not activate unrelated application windows;
- clicking through transparent regions should remain possible where supported;
- the visible character should remain the primary interactive hit area;
- Always On Top remains user-configurable under the existing settings architecture.

The pet must not intercept keyboard input globally except through already approved global hotkey mechanisms.

---

## 10. Animation Requirements

The existing Character Runtime and animation architecture remain authoritative.

The Desktop Pet layer may require the runtime to support movement/context animations such as:

- idle loop;
- walk left/right;
- run left/right;
- jump;
- fall/recovery;
- landing;
- sit/rest;
- sleep;
- attention/notice;
- edge traversal where supported;
- happy/celebration;
- task-working;
- approval-waiting;
- error;
- drag/being-carried;
- press reaction;
- hover reaction.

Not every character requires every animation immediately.

The runtime must support incremental asset expansion and character-specific capabilities.

### Animation principles

- preserve pixel-art readability at small sizes;
- avoid blurry scaling where nearest-neighbor rendering is appropriate;
- avoid heavy GPU effects;
- maintain consistent frame timing;
- allow per-animation frame duration;
- support transition animations where appropriate;
- support reduced-motion mode;
- avoid visual noise, excessive glow, or 3D effects.

Character identity and detailed animation behavior are defined by the character-specific specification.

---

## 11. Movement Physics

Use simple 2D kinematics rather than a game engine.

Suggested concepts:

- position;
- velocity;
- facing direction;
- grounded/airborne;
- target position;
- current surface;
- movement intent;
- movement mode;
- fall state;
- landing state.

The physics model should remain intentionally small.

### Basic rules

- gravity applies only while airborne/falling;
- grounded movement follows a detected compatible surface;
- horizontal velocity eases toward target speed;
- stopping has short deceleration rather than an instant snap;
- jumping uses a bounded impulse when supported;
- falling has a safe recovery path;
- movement remains within safe desktop/system boundaries.

Do not introduce a general-purpose game physics engine.

---

## 12. Surface Detection Strategy

Prefer the least expensive and most deterministic information source available.

Recommended order:

1. Windows work-area APIs;

2. native window enumeration/geometry APIs;

3. Windows UI Automation/accessibility metadata where useful;

4. limited hit-testing/geometry calculations;

5. screenshot/vision only as a future optional fallback when a specific feature genuinely requires it.

Do not add continuous screenshot streaming just to make the pet move.

Do not use browser automation to detect normal desktop surfaces.

---

## 13. Performance Requirements

The pet is intended to run for long periods.

Target behavior:

- low idle CPU usage;
- no continuous busy-loop on the UI thread;
- lightweight timer/render scheduling;
- throttled environment observation;
- no unnecessary allocations per frame;
- no constant window recreation;
- no repeated expensive desktop enumeration when nothing changed;
- animation pauses or lowers activity when hidden/sleeping where appropriate.

The pet should remain responsive while the main Niki application performs AI/tool tasks in the background.

Performance verification should include:

- long-idle runtime observation;
- animation resource usage;
- environment-observation overhead;
- autonomous behavior frequency;
- multi-monitor scenarios.

AI behavior generation must also remain bounded.

The pet must not continuously invoke an AI provider simply to appear alive.

---

## 14. Multi-Monitor Requirements

The system must understand the Windows virtual desktop.

Required behaviors:

- detect connected monitors/work areas;
- keep the pet on a valid monitor after display changes;
- preserve the nearest valid location when a monitor is removed;
- support independent monitor DPI/scaling;
- avoid hard-coded 1920×1080 assumptions;
- ensure animation and hit-testing remain correct under scaling.

---

## 15. Settings

Pet-specific settings should use the existing settings/configuration architecture.

Potential settings include:

- enabled/disabled;
- autonomous movement enabled/disabled;
- behavior mode;
- follow-active-window enabled/disabled;
- roam frequency/intensity;
- pet size;
- opacity;
- always on top;
- reduced motion;
- start with Windows;
- hide to tray;
- preferred resting location;
- selected character;
- adaptive personality enabled/disabled.

Defaults should be calm and non-disruptive.

Adaptive personality must not be enabled silently.

Do not introduce a second settings system for the pet.

---

## 16. Persistence

Persist only user-controlled preferences that belong in the existing settings/storage model.

Examples:

- selected character;
- preferred pet location;
- behavior mode;
- size/opacity;
- reduced-motion preference;
- follow/roam preference;
- adaptive personality preference.

Do not persist high-frequency movement telemetry or unnecessary behavioral logs.

Do not store a continuous history of desktop window titles/locations merely because the pet observed them.

---

## 17. Security and Privacy

Desktop awareness is not authorization.

The pet may observe minimal geometry needed for its own movement, but that does not grant permission to inspect or transmit application content.

Strict separation:

- **Window geometry** may be used for pet movement.

- **Window contents** are not automatically read.

- **Clipboard data** remains governed by the separate clipboard/data-egress policy.

- **Screen capture** remains off by default and requires an explicit feature path.

- **Application control** continues through the Tool Registry, automation layer, validation, and Permission Engine.

- **AI providers** must not receive desktop content simply because the pet is running.

The Desktop Pet cannot authorize an action.

Never use pet movement as an excuse to bypass permission controls.

### 17.1 Task and Workflow Lifecycle Signal Privacy & Visual-Only Integration

Task and workflow execution lifecycle events provide event-driven behavioral signals to the Desktop Pet via `ITaskLifecycleSignalHub`:

- Signals contain ONLY: `SignalType` (`TaskStarted`, `TaskWaiting`, `ApprovalRequired`, `TaskCompleted`, `TaskFailed`, `WorkflowNotification`), opaque GUID `SourceId`, and `Timestamp`.
- Signals NEVER contain task titles, user prompts, workflow payloads, tool arguments, clipboard contents, secrets, or sensitive notification text.
- Desktop Pet integration is strictly visual/behavioral on the existing `CharacterRuntime` (`Working`, `Thinking`, `WaitingForApproval`, `TaskComplete`/`Happy`, `Error`, `Notification`).
- No task titles, result summaries, or "Done" text may be displayed above or on the pet body.
- Result notifications remain on the separate Result Popup / toast surfaces.

---

## 18. Architecture Guidance

Add Desktop Pet behavior as focused services/components around the existing Character Runtime.

Possible interfaces, when equivalent abstractions do not already exist:

- `IPetMovementController`;
- `IPetBehaviorController`;
- `IPetSurfaceDetector`;
- `IPetSurfaceProvider`;
- `IPetEnvironmentObserver`;
- `IPetPathPlanner`;
- `IPetInteractionController`;
- `IPetSettingsService`.

Possible models:

- `PetPosition`;
- `PetVelocity`;
- `PetSurface`;
- `PetSurfaceType`;
- `PetMovementMode`;
- `PetBehaviorMode`;
- `PetEnvironmentSnapshot`;
- `PetTarget`.

Reuse existing:

- Character Registry;
- Character State Machine;
- Animation Controller;
- behavior/animation abstractions;
- transparent companion shell/window;
- design tokens;
- settings;
- tray/hotkey services;
- logging;
- cancellation/clock/random abstractions;
- test infrastructure.

Do not introduce:

- a second character engine;
- a second animation engine;
- a second task state machine;
- a second permission system;
- a second AI planner.

---

## 19. Relationship to AI-Directed Character Behavior

The Desktop Pet may eventually support open-ended, contextual, and spontaneous behavior.

That behavior must remain layered:

**AI / Context → Behavior Plan → Character Capability Validation → Animation / Movement Composition → Character Runtime → Rendered Pet**

The AI may choose or compose behavior, but it does not directly control:

- WPF;
- window handles;
- OS input;
- arbitrary processes;
- files;
- permissions;
- security settings.

The character capability system determines whether a proposed movement or reaction is supported.

Unsupported behavior must receive an appropriate fallback.

The detailed character identity, anatomy, animation primitives, and behavior composition rules belong to the dedicated character/animation specification.

The detailed AI behavior-director rules belong to the dedicated AI behavior specification.

This file only defines how those systems relate to the Desktop Pet.

---

## 20. Recommended Incremental Delivery

This specification is intentionally designed for staged implementation.

It does not create a new build phase by itself.

Implementation must follow the existing build plan.

### Stage A — Pet movement foundation

Implement when the project phase calls for the Desktop Pet movement foundation:

- movement controller;
- virtual desktop/work-area awareness;
- basic surface representation;
- walk/stop/facing;
- jump/fall/landing basics where supported;
- safe clamping;
- drag integration;
- deterministic tests.

### Stage B — Desktop/window awareness

Then add:

- native top-level window observation;
- target selection;
- active-window following mode;
- surface invalidation/recovery;
- taskbar/work-area interaction where reliable;
- multi-monitor handling.

### Stage C — Living-pet behavior

Then add:

- autonomous roaming;
- short fidgets and movement decisions;
- playful reactions;
- sit/rest/sleep;
- contextual behavior based on actual Niki states;
- anti-annoyance throttling.

### Stage D — Advanced AI-directed behavior

After the required Character Runtime, AI provider, task/context, and safety infrastructure are stable, add:

- richer contextual behavior;
- composed behavior sequences;
- spontaneous behavior;
- character-specific behavior;
- optional user-controlled personality adaptation;
- capability-aware fallbacks.

### Stage E — Polish and advanced interaction

Then add, only where justified:

- richer edge/climbing behaviors;
- additional special animations;
- richer context menu;
- stronger window-follow behavior;
- advanced surface transitions;
- additional character-specific behaviors.

The exact implementation phase must follow `09_BUILD_PLAN.md`.

This specification does not authorize pulling future-phase scope into an earlier phase.

---

## 21. Testing Requirements

Every implemented stage must include automated tests and runtime verification.

### Unit tests

Cover:

- surface detection normalization;
- screen/work-area boundaries;
- multi-monitor coordinate calculations;
- movement interpolation;
- facing direction;
- jump/fall/landing transitions where supported;
- invalid target recovery;
- behavior timing;
- anti-annoyance rules;
- reduced-motion behavior;
- settings persistence;
- drag interaction state;
- target window disappearance;
- desktop topology changes;
- deterministic random behavior using injected interfaces;
- character capability validation;
- unsupported behavior fallback.

### Integration tests

Cover:

- Character Runtime ↔ Pet Movement Controller;
- Companion Window ↔ movement updates;
- Settings ↔ pet behavior;
- task/AI state ↔ character reactions;
- window observer ↔ target selection;
- permission system remains independent from pet behavior;
- notification/result surfaces remain separate from the pet.

### Runtime verification

Verify the real Windows application for:

- idle animation;
- walk/run movement;
- drag and drop;
- taskbar/work-area positioning;
- moving/resizing application windows;
- active-window follow behavior;
- multi-monitor behavior;
- pause/sleep;
- context menu;
- reduced motion;
- no focus stealing;
- no persistent box/card UI;
- acceptable idle resource usage;
- correct behavior when AI generation is unavailable.

Never declare the Desktop Pet feature complete solely because unit tests pass.

---

## 22. Failure / Recovery Rules

The pet must fail softly.

Examples:

- Surface information unavailable → use safe work-area fallback.

- Window enumeration fails → remain on current safe surface.

- Monitor topology changes → clamp/reposition to a valid monitor.

- Animation asset missing → use a valid fallback animation rather than crashing.

- Movement target becomes invalid → cancel target and choose a safe idle state.

- Companion window reposition fails → stop autonomous motion and preserve the last valid state.

- AI behavior generation fails → use a local/default compatible behavior.

No failure in pet presentation or behavior should crash the AI agent or task system.

---

## 23. UX Personality

The pet should feel:

- alive;
- calm;
- playful;
- helpful;
- lightweight;
- unobtrusive;
- responsive.

It should not feel:

- hyperactive;
- noisy;
- intrusive;
- game-heavy;
- visually overloaded;
- like a conventional desktop widget panel.

Animation and behavior should communicate personality without distracting from the user's work.

A character may have a distinct personality, but contextual behavior should remain coherent with its identity rather than producing arbitrary contradictory reactions.

---

## 24. Design Constraints

Preserve the approved Niki AI visual identity:

- primary orange `#F97316` where brand UI is involved;
- dark charcoal foundation for application UI;
- restrained glassmorphism for actual UI surfaces;
- Inter font;
- consistent icon family;
- minimalist overall system;
- original Niki characters;
- lightweight 2D/pixel or layered companion;
- no heavy 3D rendering.

The **pet itself** may use its own character-specific palette required for readability and identity.

Do not force every character into a flat monochrome orange treatment if that conflicts with the approved character design.

Brand UI surrounding the pet must continue to follow the main design system.

---

## 25. Explicit Non-Goals

This feature specification does NOT authorize:

- a 3D desktop assistant;
- a Unity/Unreal/game-engine dependency;
- continuous screen recording;
- continuous screenshot streaming;
- automatic reading of application contents;
- unrestricted keyboard/mouse control;
- bypassing the Tool Registry;
- bypassing permissions;
- hidden telemetry about the user's desktop;
- replacing the existing companion shell;
- replacing the existing character state machine;
- implementing a second AI planner;
- implementing full autonomous PC control inside the pet layer;
- implementing a second settings system;
- implementing a second memory system.

---

## 26. Definition of Done

The Desktop Pet System is complete only when all of the following are true:

1. The pet remains a transparent, borderless, free-floating character with no persistent container UI.

2. It can move naturally in the Windows desktop environment.

3. It can use safe desktop/work-area surfaces and, where reliably supported, taskbar/window edges.

4. It reacts to window/environment changes without stealing focus.

5. Dragging, clicking, double-clicking, context interaction, pause/sleep, and settings work reliably.

6. Existing Niki AI task/application states correctly drive character reactions.

7. Autonomous behavior is calm, configurable, cancellable, and resource-efficient.

8. Multi-monitor and DPI/scaling scenarios are handled.

9. Privacy and security boundaries remain intact.

10. AI-directed behavior cannot bypass character capabilities, permissions, or security controls.

11. Missing AI/API availability does not break the pet runtime.

12. Automated tests and real Windows runtime verification both pass.

13. Existing verified functionality has no regression.

14. No arbitrary task-status text is placed over the pet unless explicitly required by an approved feature.

15. Implementation is delivered incrementally according to the existing build plan.

---

## 27. Implementation Instruction to the Coding Agent

When this file is present in the project context:

- Read it alongside the existing specification when a task touches the companion, character runtime, animation, desktop positioning, window awareness, or pet behavior.

- Treat this file as the parent Desktop Pet specification.

- Do not implement the entire specification simply because the file is present.

- Implement only the subset assigned to the current project phase.

- Before each pet-related implementation, inspect the existing code and reuse current Character Runtime and Companion Shell abstractions.

- Never break transparent free-floating companion behavior to add pet features.

- Never create duplicate character, animation, AI-planning, task, permission, settings, or memory systems.

- Keep Desktop Pet behavior separate from utility surfaces such as Result Popup, notifications, task panels, and chat windows.

- Update tests and documentation for every implemented subset.

- Do not claim a behavior is implemented merely because the specification describes it.

- Build, run, exercise, and verify the real Windows application before marking an implemented behavior complete.

---

## 28. Release Candidate Hardening & Stability Guarantees

As of Phase 17 completion, the Desktop Pet system guarantees:
- **Presentation Footprint**: Strictly maintains the 150x100 presentation size with nearest-neighbor pixel preservation (`BitmapScalingMode.NearestNeighbor`, `SnapsToDevicePixels = True`).
- **Authoritative Registry**: `CharacterRegistry` acts as the sole authoritative registry managing 8 runtime identity profiles. On-disk frame files are validated with `BitmapDecoder` (`niki` and `dog/biscuit` verified true; candidate identities verified false and unselectable; `mochi` collapsed). No fabricated sprite assets exist.
- **Visual-Only Physics**: `MotionPhysicsSimulator` alters RenderTransform visual offsets only, never mutating `CompanionWindow.Left/Top`, `PetSurface`, or navigation coordinates. Suppressed to exact identity under Reduced Motion.
- **Deterministic Timer Throttling**: Frame animation timers and casual idle timers stop completely when the companion window is hidden or minimized (`SetThrottled(true)`). No duplicate timers are created across repeat throttle/unthrottle cycles. Zero continuous physics loops run during idle.
- **Focus Safety**: The companion window applies `WS_EX_NOACTIVATE` and never steals focus from the user's active applications during movement, idle, or expression reactions.