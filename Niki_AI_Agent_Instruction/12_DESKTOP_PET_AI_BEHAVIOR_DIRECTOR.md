# 12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md

## Purpose

This document defines the AI-driven behavior layer for Niki AI's Desktop
Pet.

It makes the character feel alive and context-aware without turning the
AI into a direct UI/OS controller.

### Core principle

**AI decides what the character wants to express. The Character System
decides how that character can naturally express it.**

The Behavior Director must consume the authoritative character
identities, capabilities, personalities and animation primitives from
`11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md`.

------------------------------------------------------------------------

# 1. High-Level Architecture

``` text
Niki AI / Context
       ↓
Low-Risk Context & Preference Signals
       ↓
AI Behavior Director
       ↓
Behavior Intent / Behavior Plan
       ↓
Character Identity + Capability Validation
       ↓
Motion Composer + Expression Composer
       ↓
Animation Engine / Expression Renderer
       ↓
Desktop Pet
```

The AI must never directly manipulate WPF controls, windows, processes,
files, browser automation, OS input or other privileged resources.

------------------------------------------------------------------------

# 2. Behavior Plan

The AI should produce a structured behavior plan rather than arbitrary
executable UI commands.

Conceptual fields:

-   characterId
-   intent
-   mood
-   targetContext
-   motionSequence
-   expressionIntent
-   intensity
-   duration
-   priority
-   cooldown
-   interruptibility

Example:

``` text
intent: curious_observation
mood: calm
motionSequence:
  look_left
  pause
  head_tilt
  blink
  return_idle
expressionIntent:
  subtle_sparkle
intensity: low
```

The plan is validated before execution.

------------------------------------------------------------------------

# 3. Open-Ended Behavior Composition

The behavior system must NOT be limited to a fixed list of monolithic
animations.

Instead, the AI may compose approved primitives into novel sequences.

Example:

``` text
look_around
→ pause
→ lean_forward
→ blink
→ gesture
→ step_back
→ stretch
→ return_idle
```

Another character may receive the same intent but a completely different
sequence because its identity and capabilities differ.

This creates open-ended behavior while preserving safety and
consistency.

------------------------------------------------------------------------

# 4. Character Identity Constraint

Before a behavior plan is executed:

1.  Load character identity.
2.  Load personality profile.
3.  Load body capabilities.
4.  Validate requested primitives.
5.  Apply personality weighting.
6.  Replace unsupported actions with compatible fallbacks.
7.  Apply cooldown/priority constraints.
8.  Execute only the validated plan.

The AI cannot override the character's identity.

------------------------------------------------------------------------

# 5. Personality-Aware Behavior Selection

Each character has behavioral tendencies.

Example:

### Quiet / reserved character

Prefer: - subtle gaze - slow movement - small gestures - quiet idle -
understated reactions

Avoid frequent: - large jumps - exaggerated celebration - constant
waving

### Energetic character

Prefer: - bounce - quick gestures - expressive movement - short runs -
stronger celebration

### Stoic knight

Prefer: - guard stance - gear check - controlled nod - disciplined
movement

### Curious astronaut cat

Prefer: - head tilt - gaze exploration - antenna inspection - tail
movement - quiet curiosity

These are behavioral tendencies, not hard-coded single animations.

------------------------------------------------------------------------

# 6. Context-Aware Behavior

The director may use relevant low-risk context such as:

-   application activity
-   task state
-   notification state
-   user active/idle state
-   work-session duration
-   time-of-day where appropriate
-   character's current mood
-   current animation
-   recent character behavior
-   recent user interaction with the pet

Examples:

### User starts working

Character settles into an appropriate idle/work posture.

### Long work session

Character may stretch, yawn, shift posture or perform another
character-specific low-interruption behavior.

### Task starts

Character may become attentive.

### Task succeeds

Character uses its own success reaction.

### Task fails

Character expresses disappointment according to its personality.

### User returns

Character uses its own greeting/attention behavior.

------------------------------------------------------------------------

# 7. Personality Adaptation

Personality Adaptation is an explicit user-controlled feature.

### Setting

`Settings → Desktop Pet → Personality Adaptation`

Values:

-   `OFF`
-   `ON`

### OFF

The Behavior Director must ignore adaptive user-preference signals for
personality selection.

The character follows its base identity.

### ON

The director may use low-risk preference signals to personalize
behavior.

Examples:

-   writing frequency
-   research frequency
-   creative-tool usage
-   interaction style
-   humor-related interaction patterns
-   work-session duration
-   break patterns

The system should learn behavioral preferences, not secretly diagnose or
label the user.

------------------------------------------------------------------------

# 8. Character Growth / Familiarity

When Personality Adaptation is enabled, the character may gradually
develop a familiarity profile.

Conceptually:

``` text
Base Personality
        +
Observed Preference Signals
        +
Interaction Feedback
        ↓
Familiarity Profile
        ↓
Behavior Weight Adjustment
```

Example:

If a user frequently writes for long periods, the character may
increasingly select: - quiet writing-themed idle behavior - stretch
after long sessions - focused posture - small "thinking" mannerisms

If a user frequently interacts with humorous content, the character may
use more playful/comedic reactions where appropriate.

The character must still remain itself.

------------------------------------------------------------------------

# 9. Feedback Loop

The system may learn from safe interaction signals:

-   user clicks/pets character
-   user dismisses a behavior
-   user interacts with an expression
-   user ignores repeated behavior
-   user changes Personality Adaptation setting
-   user manually triggers a reaction

These signals can adjust behavior selection.

Do not use covert sensitive profiling.

------------------------------------------------------------------------

# 10. Mood Model

Mood should be lightweight and temporary.

Possible internal states include:

-   calm
-   curious
-   happy
-   excited
-   tired
-   focused
-   surprised
-   confused
-   disappointed
-   sleepy
-   playful

Mood is not a permanent personality replacement.

Mood transitions should be influenced by: - recent events - current
context - character identity - user interaction - time since last
behavior

A quiet character may become happy without suddenly becoming
hyperactive.

------------------------------------------------------------------------

# 11. Expression Composer

The Behavior Director can request contextual graphical expression
effects.

Example:

``` text
intent: playful_success
characterTheme: dark
effect:
  subtle_symbol
  small_spark
duration: short
intensity: low
```

The Expression Composer translates intent into character-native
graphics.

The AI does not directly draw arbitrary UI.

------------------------------------------------------------------------

# 12. Character-Native Expression Language

Expression generation must use the character's own visual language.

Examples:

-   astronaut: stars, tiny space particles, orbital/sci-fi motifs
-   knight: heraldic marks, impact lines, controlled victory effects
-   dark character: restrained symbols, subtle particles, minimal visual
    noise
-   energetic cartoon: stars, bursts, motion lines
-   cat: hearts, sparkles, paw-like motifs when appropriate

The effect should look as if it belongs to that character.

------------------------------------------------------------------------

# 13. Spontaneous Behavior

The Desktop Pet may perform spontaneous behavior when the system has a
reason and the behavior budget permits it.

Spontaneous behavior should consider:

-   idle duration
-   recent behavior
-   mood
-   personality
-   current user activity
-   cooldown
-   priority
-   environmental context

Examples:

-   looking around
-   stretching
-   adjusting clothing/equipment
-   yawning
-   sitting
-   sleeping
-   small playful action
-   observing the cursor
-   character-specific mannerism

Spontaneous behavior must never become constant background computation
or intrusive animation spam.

------------------------------------------------------------------------

# 14. Behavior Budget and Performance

The AI should NOT continuously think about the pet.

Use event-driven triggers, cooldowns and bounded decision frequency.

Required concepts:

-   minimum interval between spontaneous decisions
-   animation cooldown
-   expression cooldown
-   priority
-   interruption policy
-   idle budget
-   resource budget
-   fallback local behavior

If the AI/API is unavailable, the character should continue using local
safe behaviors.

------------------------------------------------------------------------

# 15. Priority Model

Suggested priority order:

1.  User interaction
2.  Explicit user-triggered character action
3.  Important system/task attention
4.  Notification attention
5.  Contextual reaction
6.  Spontaneous behavior
7.  Idle behavior

Lower-priority behavior must not interrupt higher-priority behavior.

------------------------------------------------------------------------

# 16. Interruptibility

Every behavior should declare whether it can be interrupted.

Examples:

-   idle: fully interruptible
-   blink: short/non-interruptible
-   greeting: partially interruptible
-   user drag reaction: high priority
-   celebration: interruptible after minimum presentation time
-   sleep: interruptible by user interaction
-   important notification reaction: higher priority

------------------------------------------------------------------------

# 17. Physical and Capability Validation

Before execution:

``` text
AI Behavior Plan
      ↓
Character Capability Check
      ↓
Motion Constraint Check
      ↓
Personality Consistency Check
      ↓
Priority / Cooldown Check
      ↓
Approved Behavior
```

If a requested action is unsupported:

-   substitute a compatible action;
-   reduce the sequence;
-   or safely fall back to idle.

Never fabricate unsupported anatomy.

------------------------------------------------------------------------

# 18. Interaction With User

The character can respond to:

-   click
-   drag
-   pet/touch interaction
-   release
-   hover
-   return-to-desktop presence
-   explicit character command where supported

Each character should respond differently according to its personality.

Example: - cheerful character may bounce; - quiet character may glance
at the user; - knight may straighten posture; - cat may move ears/tail.

------------------------------------------------------------------------

# 19. Safety and Architecture Boundaries

The Behavior Director:

-   does not bypass permissions;
-   does not invoke privileged tools directly;
-   does not control the OS directly;
-   does not manipulate arbitrary windows;
-   does not read sensitive data merely to create personality;
-   does not override `05_SECURITY_AND_PERMISSIONS.md`;
-   does not override `06_AVOID_AND_GUARDRAILS.md`;
-   does not create a second Scheduler/Notification engine;
-   does not turn ResultPopup into part of the character window;
-   operates 100% offline via `LocalBehaviorEngine` with zero cloud AI dependency in Phase 15;
-   physics simulation (`MotionPhysicsSimulator`) strictly drives temporary visual `RenderTransform` offsets and never modifies `CompanionWindow.Left/Top`, `PetSurface`, or navigation coordinates;
-   physics simulation returns exact identity transform under `ReducedMotion`;
-   stops animation and casual idle timers completely (`SetThrottled(true)`) when companion window is hidden or minimized, with zero duplicate timers created across repeated throttle/unthrottle cycles;
-   composes pure symbolic visual expressions via `ExpressionComposer` and `ExpressionOverlayControl` with bounded durations [0.5s, 2.5s] and zero text badges/banners;
-   enforces strict zero metric collection, calculation, and behavioral weighting when Personality Adaptation is disabled (default);
-   release candidate hardening verified deterministic throttling, priority bounding, offline safety with `LocalBehaviorEngine`, and clean lifecycle disposal without orphaned background processes.

The Desktop Pet remains a presentation/interaction layer.

------------------------------------------------------------------------

# 20. No Unsolicited Task Text

The Behavior Director must not display: - "Done" - task titles - result
summaries - arbitrary status text

above the character unless an explicit product feature later defines
that behavior.

Task results belong to the Notification/Result Popup system.

The character may visually react to task state without becoming a text
notification surface.

------------------------------------------------------------------------

# 21. Future Extensibility

The system must support:

-   additional characters
-   future Anime/Waifu characters
-   new species
-   new body capabilities
-   new motion primitives
-   new expression themes
-   new personality profiles
-   new contextual triggers

New characters should be added by creating a new CharacterId/profile
rather than modifying the behavior engine itself.

------------------------------------------------------------------------

# 22. Relationship to Character System

`11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md` is authoritative for:

-   character identity
-   character roster
-   visual identity
-   body decomposition
-   capabilities
-   personality baseline
-   emotional range
-   signature animations
-   signature reactions
-   character-native expression language

This document is authoritative for:

-   AI behavior selection
-   contextual behavior
-   spontaneous behavior
-   personality adaptation
-   familiarity/growth
-   behavior composition
-   mood transitions
-   expression intent
-   behavior priorities and budgets

Neither document overrides the project's core security, architecture,
guardrails, or roadmap authority.
