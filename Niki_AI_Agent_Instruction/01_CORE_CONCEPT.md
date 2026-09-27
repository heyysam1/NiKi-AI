# 01 — Core Concept

## Product identity

**Name:** Niki AI

**Positioning:** A personal AI operator that lives on the Windows desktop.

Niki AI is not merely a chatbot and not merely a desktop pet. The character is the visible companion layer; the actual product is an agent runtime that can understand requests, plan work, use tools, remember context, schedule future actions, report results, and ask for approval when risk or ambiguity requires it.

## Core promise

> Small companion. Real work.

Niki AI should feel like an always-available digital operator that the user can speak to naturally while continuing their normal workflow.

Example:

The user is designing in Figma and says through voice mode:

> “Find recent references for this landing-page style and summarize the useful ones.”

Niki AI should:
1. understand the goal;
2. create an internal task;
3. search using an allowed browser;
4. collect useful results;
5. summarize the findings;
6. keep working while the user continues in Figma;
7. notify the user when the work is ready;
8. open a result popup containing the findings, sources, and generated output.

The user should not have to supervise every harmless step.

## Product pillars

### 1. Presence
A small 2D character remains on the desktop without occupying meaningful workspace.

### 2. Agency
The agent can execute tasks using controlled tools rather than only answer questions.

### 3. Memory
The agent can retain useful preferences, project context, and task history.

### 4. Time awareness
The agent can schedule reminders, recurring tasks, deadlines, and notifications.

### 5. Transparency
The user can inspect what the agent is doing, what it has done, and why an action needs approval.

### 6. Safety
Sensitive actions pass through an explicit permission layer.

### 7. Personalization
The user can choose characters, skins, behavior, voice, theme, size, position, and personality.

### 8. Lightweight execution
The character is 2D/pixel-oriented, idle animation is suspended when appropriate, and background work is event-driven.

## Interaction philosophy

The default desktop presence is intentionally tiny.

Target standard visible character canvas:

**150 × 100 px**

Recommended scaling presets:
- Compact: 100 × 70
- Standard: 150 × 100
- Large: 200 × 135
- Custom: user-selectable

The character should never become a full-screen animated distraction unless the user explicitly opens a larger experience.

## Character concept

The user has approved a hybrid roster:

- one or more anime/waifu-style AI companions;
- cute animal companions;
- pixel-art styling;
- expressive but professional silhouettes;
- small visual footprint;
- no 3D real-time rendering requirement.

Characters should look designed, not like generic emoji or stock chibi art. Use a controlled sprite language, limited palette, consistent outlines, and a recognizable silhouette.

## Core character roster

### Niki
Human anime companion / primary AI identity.

Personality defaults:
- calm
- helpful
- focused
- slightly playful
- concise unless the user asks for detail

### Mochi
Cat companion.

Traits:
- quiet
- observant
- sleepy when idle
- playful during success

### Biscuit
Dog companion.

Traits:
- energetic
- encouraging
- positive reactions
- stronger movement animation

### Lumi
Rabbit companion.

Traits:
- gentle
- organized
- subtle ear/tail animation

### Kiki
Parrot companion.

Traits:
- curious
- expressive
- short vocal reaction animations

### Momo
Monkey companion.

Traits:
- playful
- fast idle cycles
- expressive reactions

### Roku
Fox companion.

Traits:
- clever
- calm
- alert notifications

More characters can be added later without changing the core runtime.

## The agent loop

Conceptually:

User input
→ intent understanding
→ task decomposition
→ tool selection
→ permission evaluation
→ execution
→ observation
→ correction/retry when safe
→ result
→ user-facing notification
→ task history

The model must not directly control the operating system. It should produce structured intents/tool calls that the agent runtime validates and executes.

## Modes

### Assistant Mode
The safest and most transparent default. The agent asks before important actions and exposes task progress.

### Autonomous Mode
Trusted low-risk operations may execute automatically. High-risk operations still require approval.

### Do Not Disturb
The agent can continue background work but suppresses non-critical visual interruptions. Critical failures and user-requested reminders can still surface.

### Paused
No new autonomous actions are started. Existing safe jobs may be allowed to finish depending on task policy.

## Success definition

A successful build is not “the UI looks close.”

A successful build means:
- the application launches reliably;
- the desktop companion behaves predictably;
- tasks can be created and tracked;
- the agent can use controlled tools;
- notifications work;
- permissions work;
- browser automation uses Edge or Brave only;
- the app can recover from failures;
- the UI follows one coherent design system;
- the character stays lightweight;
- important workflows are covered by repeatable tests.
