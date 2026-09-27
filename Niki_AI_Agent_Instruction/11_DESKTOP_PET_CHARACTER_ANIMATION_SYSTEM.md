# 11_DESKTOP_PET_CHARACTER_ANIMATION_SYSTEM.md

## Purpose

This document is the authoritative supplemental specification for Niki
AI's Desktop Pet character identities, body-part decomposition, movement
capabilities, personality consistency, expressions, animation
primitives, and character-specific reactions.

It extends `10_DESKTOP_PET_SYSTEM.md`. It does not replace the core
product, architecture, security, permissions, or guardrail
specifications in files 1--10.

### Core principle

**Characters are identities, not animation presets.**

Each character must preserve a coherent visual identity, personality,
emotional range, body mechanics, and behavioral language. Animation must
be generated from the character's capabilities and identity rather than
applying one generic animation set to every character.

Reference images that are only behavior/style references must never be
imported as character identities.

------------------------------------------------------------------------

## 1. Approved Character Roster

### Retained legacy characters

-   `niki`
-   `dog`

These two remain part of the roster.

### Removed legacy characters

Three legacy character slots are to be removed from the active roster,
as directed for this phase. Their exact asset/CharacterId mapping must
be confirmed from the project asset inventory before destructive
deletion. They must not remain selectable after the roster migration.

### New mockup characters

The six uploaded mockups become the six new character candidates for the
roster.

Stable IDs should be assigned once the project assets are named.
Suggested temporary IDs:

1.  `character-03-astronaut-cat`
2.  `character-04-knight`
3.  `character-05-orange-astronaut-cat`
4.  `character-06-goth-girl`
5.  `character-07-retro-boy`
6.  `character-08-red-cap-adventurer`

Do not treat these temporary IDs as final asset filenames until the
asset inventory is inspected.

### Phase 15 Roster Reconciliation Result

In Phase 15, the roster is authoritatively reconciled within the sole runtime `CharacterRegistry`:
- Retained `niki` (retained primary character, active).
- Retained `dog` (retained character, active; mapped non-destructively to existing `biscuit` sprite sheet assets).
- Six approved mockup candidate identities registered with full capability profiles (`character-03-astronaut-cat`, `character-04-knight`, `character-05-orange-astronaut-cat`, `character-06-goth-girl`, `character-07-retro-boy`, `character-08-red-cap-adventurer`).
- Legacy character `mochi` marked deprecated and hidden from active selectable roster without destructive asset deletion.

### Phase 16 Polish & Asset Ingestion Integrity

In Phase 16, ingestion validation and character system polish are finalized:
- Sole Authoritative Registry: `CharacterRegistry` is the single source of truth for runtime character profiles.
- Dynamic Manifest Discovery: `ReloadCharacters()` dynamically scans and reloads animation manifests on disk while strictly preserving all 8 authoritative identity profiles.
- Real On-Disk Frame Validation: `IsAssetBacked(id)` performs thorough validation of manifests and referenced image frame files on disk (existence and non-zero byte size).
- Active Selectable Characters: `niki` and `dog` (mapped to `biscuit`) are confirmed `IsAssetBacked = true` and remain the only active selectable characters.
- Non-Selectable Candidates: The six approved candidate identities (`character-03` to `character-08`) remain registered with capability bitmasks and themes, but return `IsAssetBacked = false` and throw `InvalidOperationException` if attempted to be activated prior to real sprite asset delivery.
- Legacy Compatibility: `mochi` remains accessible via `GetIdentityProfile("mochi")` but hidden from the active context menu.
- Pure Visual Expressions: Legacy speech bubble text badge removed from `CharacterView`; expressions are purely graphical/symbolic overlays via `ExpressionComposer` and `ExpressionOverlayControl` with bounded durations [0.5s, 2.5s].

### Phase 17 Hardening & Final Roster Verification

In Phase 17 Release Candidate verification:
- Authoritative Roster: `CharacterRegistry` manages 8 authoritative identity profiles (`niki`, `dog`, and `character-03` through `character-08`).
- Strict Frame Decodability: `IsAssetBacked(id)` was hardened and verified to validate not only on-disk path existence and non-zero byte size, but actual `BitmapDecoder` frame decodability. Truncated or corrupt frame files safely return `false`.
- Active Usable Characters: Only `niki` and `dog` (mapped to `biscuit`) decode real frames and are marked `IsAssetBacked = true`.
- Zero Fabricated Assets: No synthetic or placeholder sprite sheets were created for candidate identities (`character-03` to `character-08`). They remain registered with full capability metadata and themes, but return `IsAssetBacked = false` and cannot be activated via `SetActiveCharacter`.
- Legacy Profile: `mochi` remains preserved in registry metadata but hidden from the active selection UI.
- Scaling & Rendering: `CharacterView` strictly enforces `BitmapScalingMode.NearestNeighbor` and `SnapsToDevicePixels = True` inside the 150x100 presentation container.

------------------------------------------------------------------------


# 2. Mockup-Derived Character Identity Profiles

The following interpretations are derived from the six supplied mockups.
They are the design basis for behavior and animation, while exact
personality values can be refined after final assets are inspected.

## Character 03 --- Astronaut Cat

### Visual identity

Compact dark cat in a rounded space helmet/suit, green eyes, cat ears,
curled tail, small antenna/light on top, small decorative sparkles.

### Personality direction

Calm, curious, observant, quietly playful, slightly spacey.

This character should not behave as permanently hyperactive. Its energy
should usually be subtle and controlled.

### Natural emotional range

-   Calm
-   Curious
-   Content
-   Mild surprise
-   Sleepy
-   Quiet excitement
-   Focused
-   Gentle amusement

### Body-part capabilities

-   Head/neck
-   Eyes/blink/gaze
-   Ears
-   Mouth
-   Forepaws
-   Hind legs
-   Tail
-   Torso
-   Helmet/head assembly
-   Antenna/light
-   Small suit details where separately available

### Signature behaviors

-   Slow curious head tilt
-   Tail curl/un-curl
-   Helmet/antenna check
-   Looking upward at the antenna light
-   Small feline stretch
-   Quiet paw movement

### Signature reaction

A curious head tilt followed by a slow blink and tail movement.

------------------------------------------------------------------------

## Character 04 --- Knight

### Visual identity

Compact armored knight with a closed visor, red plume, sword, heavy
armor plates, belt and medieval detailing.

### Personality direction

Stoic, disciplined, brave, duty-oriented, dignified. Humor should be dry
or physical rather than overly cute.

### Natural emotional range

-   Alert
-   Focused
-   Proud
-   Determined
-   Tired
-   Mildly confused
-   Victorious
-   Concerned

### Body-part capabilities

-   Helmet/head
-   Visor
-   Plume
-   Neck
-   Torso/armor
-   Shoulders
-   Upper/lower arms
-   Hands
-   Legs
-   Boots
-   Sword
-   Belt/armor accessories

### Signature behaviors

-   Sword/gear check
-   Plume movement
-   Guard stance
-   Controlled armor stretch
-   Short disciplined march
-   Brief victory stance

### Signature reaction

A controlled weapon/armor check followed by a firm nod or guard stance.

------------------------------------------------------------------------

## Character 05 --- Orange Astronaut Cat

### Visual identity

Orange cat in a white space suit/helmet, expressive dark eyes, visible
tail, compact robotic/space suit details.

### Personality direction

More openly cheerful and energetic than Character 03. Friendly, curious,
playful, optimistic.

### Natural emotional range

-   Happy
-   Excited
-   Curious
-   Surprised
-   Playful
-   Sleepy
-   Proud
-   Mild disappointment

### Body-part capabilities

-   Head/helmet
-   Eyes
-   Ears
-   Mouth
-   Arms
-   Hands/paws
-   Legs
-   Feet
-   Tail
-   Torso
-   Suit panels
-   Helmet details

### Signature behaviors

-   Little bounce
-   Tail wag
-   Excited paw gesture
-   Looking around through helmet
-   Quick playful turn
-   Tiny celebratory hop

### Signature reaction

A quick bounce + tail movement + cheerful paw gesture.

------------------------------------------------------------------------

## Character 06 --- Dark/Goth Girl

### Visual identity

Human female character with long black hair, dark fitted top, wide dark
trousers, dark shoes, muted palette and reserved expression.

### Personality direction

Quiet, reserved, introspective, composed, slightly mysterious. She must
not be forced into constant cheerful behavior.

### Natural emotional range

-   Calm
-   Thoughtful
-   Tired
-   Subtle amusement
-   Mild annoyance
-   Shy/awkward
-   Sad/reflective
-   Focused
-   Quiet happiness

### Body-part capabilities

-   Head/neck
-   Eyes
-   Eyelids
-   Mouth
-   Hair groups
-   Shoulders
-   Upper/lower arms
-   Hands
-   Torso
-   Hips
-   Upper/lower legs
-   Feet
-   Clothing/accessory groups

### Signature behaviors

-   Hair adjustment
-   Looking away
-   Small sigh
-   Slow blink
-   Weight shift
-   Hands-in-pocket posture if asset permits
-   Quiet sitting/leaning behavior

### Signature reaction

A subtle look-away or slow blink rather than a large exaggerated
reaction.

------------------------------------------------------------------------

## Character 07 --- Retro Boy

### Visual identity

Young male character with brown textured hair, brown varsity-style
jacket, cream sleeves, dark shirt, wide trousers, brown shoes and
visible cross necklace/detail.

### Personality direction

Relaxed, casual, grounded, slightly aloof but approachable. Can be
playful without becoming hyperactive.

### Natural emotional range

-   Relaxed
-   Curious
-   Confident
-   Amused
-   Bored
-   Sleepy
-   Focused
-   Mild surprise
-   Casual excitement

### Body-part capabilities

-   Head/neck
-   Eyes
-   Mouth
-   Hair
-   Shoulders
-   Arms
-   Hands
-   Jacket/sleeves
-   Torso
-   Hips
-   Legs
-   Shoes
-   Necklace/accessory where separable

### Signature behaviors

-   Jacket/sleeve adjustment
-   Casual weight shift
-   Hair adjustment
-   Hands-in-pocket idle
-   Small head nod
-   Relaxed stretch

### Signature reaction

A casual head nod + slight grin/gesture rather than a dramatic
celebration.

------------------------------------------------------------------------

## Character 08 --- Red-Cap Adventurer

### Visual identity

Highly pixelated small adventurer/platformer-inspired figure with large
red cap, blue overalls, red shirt, gloves, brown boots and compact
cartoon proportions.

### Personality direction

Energetic, adventurous, expressive, optimistic and physical.

The design naturally supports stronger cartoon reactions than the
quieter characters.

### Natural emotional range

-   Excited
-   Happy
-   Curious
-   Surprised
-   Determined
-   Confused
-   Victorious
-   Frustrated
-   Playful

### Body-part capabilities

-   Head
-   Cap
-   Face/eyes/mouth where asset supports it
-   Arms
-   Hands
-   Torso
-   Overalls
-   Legs
-   Feet/boots
-   Cap and clothing groups

### Signature behaviors

-   Cap adjustment
-   Ready stance
-   Small jump
-   Quick run-in-place
-   Fist pump
-   Comedic stumble/recovery
-   Energetic wave

### Signature reaction

Quick cap adjustment followed by a determined pose or celebratory fist
pump.

------------------------------------------------------------------------

# 3. Full-Body Motion System

Niki AI must NOT rely on whole-image bobbing, edge deformation, or
simple pixel displacement as the primary character animation system.

The character renderer should support independently animated body
components wherever the artwork permits it.

### Required movement model

`Character` → `Body Parts` → `Joints / Anchors` → `Motion Constraints` →
`Motion Primitives` → `Motion Composer` → `Animation Renderer`

Examples:

-   walking uses leg cycles, arm counter-swing, torso motion, head
    stabilization and foot placement;
-   sitting changes hips, knees, torso and head posture;
-   looking around changes gaze, head and sometimes torso;
-   jumping changes legs, arms, torso, anticipation, flight and landing;
-   stretching uses multiple connected body parts;
-   emotional reactions alter posture rather than only moving the outer
    silhouette.

### Body-part decomposition

Asset preparation must identify all independently movable parts that are
visually and anatomically meaningful.

Do not artificially split every pixel. Split by meaningful articulated
regions.

### Character-specific anatomy

The capability system must determine what each character can physically
perform.

Examples: - cat: ears, tail, paws, feline stretch; - knight: armor,
plume, sword and controlled heavy movement; - human: arms, hands, legs,
hair, clothing; - animal: species-specific limbs/tail/ears; - stylized
character: use the motion model appropriate to its proportions.

Unsupported movements must receive a compatible fallback rather than
forcing impossible anatomy.

------------------------------------------------------------------------

# 4. Motion Primitive System

Animations are not fixed monolithic clips.

The engine should expose reusable primitives such as:

-   idle
-   blink
-   gaze-left/right/up/down
-   head-tilt
-   look-around
-   breathe
-   stretch
-   lean
-   sit
-   stand
-   walk
-   run
-   jump
-   fall
-   land
-   wave
-   nod
-   shake-head
-   gesture
-   inspect-accessory
-   yawn
-   sleep
-   wake
-   celebrate
-   recoil
-   stumble
-   recover

The AI Behavior Director may compose these primitives into new
sequences.

Example:

`look_left → pause → head_tilt → blink → tail_motion → return_idle`

No single predefined animation clip is required for every sequence.

------------------------------------------------------------------------

# 5. Natural Motion / Physics Layer

Where appropriate, movement should support:

-   acceleration/deceleration
-   anticipation
-   follow-through
-   inertia
-   weight shift
-   secondary motion
-   squash/stretch
-   balance correction
-   landing compression
-   hair/accessory/tail follow-through

These effects must respect the character's visual style and not destroy
pixel-art readability.

------------------------------------------------------------------------

# 6. Personality Integrity

Every animation, emotion and reaction must remain compatible with the
established character identity.

A character must not randomly switch personality simply because an AI
generated a novel behavior.

Exception: explicitly designed comedic or exceptional reactions are
allowed when they remain believable for that character.

The system should prefer: **character-consistent creativity over generic
randomness.**

------------------------------------------------------------------------

# 7. Mannerisms, Idle Personality and Signature Behaviors

Every character should have:

-   2--5 recurring mannerisms
-   personality-specific idle behavior
-   1--3 signature animations
-   1--3 signature reactions
-   character-specific success behavior
-   character-specific failure/disappointment behavior
-   character-specific notification attention behavior
-   character-specific greeting/return behavior

The same event must therefore be expressible differently by different
characters.

------------------------------------------------------------------------

# 8. Expression Layer

Body animation is only one layer.

A separate expression/effect layer may add:

-   hearts
-   sparkles
-   stars
-   sweat drops
-   tears
-   question marks
-   exclamation marks
-   motion lines
-   music notes
-   celebration particles
-   subtle aura/glow
-   comic-style symbols
-   contextual themed effects

Expressions must inherit the character's visual language.

Examples: - cute character: soft hearts/sparkles; - dark character:
restrained, minimal effects; - energetic character: stronger
bursts/motion lines; - knight: heraldic/impact-style effects; -
astronaut: space/sci-fi themed effects.

Do not apply one universal sticker style to every character.

------------------------------------------------------------------------

# 9. AI-Generated Expression Intent

The AI may request an expression through structured intent rather than
directly manipulating the UI.

Example:

`Intent: playful_surprise` `Intensity: low` `Theme: character_native`
`Duration: short`

The expression system then renders a safe character-themed effect.

AI must never directly execute arbitrary WPF/OS/UI operations.

------------------------------------------------------------------------

# 10. User Personalization / Character Growth

Character personalization is an optional feature and MUST be
user-controlled.

### Settings

Provide a clear Desktop Pet setting:

**Personality Adaptation** - ON - OFF

When OFF: - no adaptive personality learning should influence character
behavior; - the character follows its base identity/profile.

When ON: - low-risk behavioral preference signals may gradually
influence behavior; - personalization should remain bounded and
explainable; - the character does not become a psychological clone of
the user.

### Allowed signal examples

-   frequently writes
-   frequently researches
-   frequently uses creative tools
-   frequently interacts with humor/meme-related content
-   long work sessions
-   frequent breaks
-   preferred interaction patterns

Avoid inferring sensitive personal traits or creating hidden
psychological profiles.

### Growth model

`Base Character Identity` + `Observed Low-Risk Preference Signals` →
`Character Adaptation Profile` →
`Behavior Probability / Context Selection`

The character retains its original identity while becoming more familiar
with the user's working style.

------------------------------------------------------------------------

# 11. Contextual Environment Behavior

The character may react to non-sensitive, relevant desktop context:

-   user becomes active
-   user starts a work session
-   long inactivity
-   long continuous session
-   task starts
-   task completes
-   task fails
-   notification arrives
-   user returns after absence

Behavior must remain non-intrusive and must never block or interfere
with user work.

------------------------------------------------------------------------

# 12. Hard Constraints

-   No unsolicited task/result text floating above the character.
-   No generic personality overriding character identity.
-   No impossible anatomy unless deliberately supported as a special
    effect.
-   No whole-image edge-bobbing as the only animation mechanism.
-   No direct AI control of WPF/OS.
-   No bypass of security/permission rules.
-   No hidden sensitive personality profiling.
-   No permanent personalization when the user has disabled Personality
    Adaptation.
-   No importing reference-only characters as identities.
-   Result Popup remains separate from the Desktop Pet.
-   Scheduler/Notification core must not depend on the character
    renderer.

------------------------------------------------------------------------

# 13. Asset Discovery and Layer Preparation

When final assets are added to the project:

1.  Inspect the asset inventory.
2.  Match each asset to its stable CharacterId.
3.  Identify body-part layers and articulated regions.
4.  Preserve the original artwork.
5.  Create animation-ready layer definitions without unnecessarily
    redrawing the character.
6.  Record unsupported capabilities explicitly.
7.  Validate that each character can render independently.
8.  Keep future character slots extensible.

The six mockups are design references for this system. Final production
animation should use the project's actual approved character assets once
supplied.

------------------------------------------------------------------------

# 14. Relationship to Other Specs

-   `01_CORE_CONCEPT.md` remains authoritative for product concept.
-   `02_FEATURES_AND_SCOPE.md` remains authoritative for overall scope;
    character specifics are delegated here.
-   `03_DESIGN_SYSTEM.md` remains authoritative for global visual
    language; character-specific visual language is defined here.
-   `04_ARCHITECTURE.md` remains authoritative for application
    architecture; this document must not create a second unrelated
    character engine.
-   `05_SECURITY_AND_PERMISSIONS.md` remains authoritative and cannot be
    overridden.
-   `06_AVOID_AND_GUARDRAILS.md` remains authoritative for safety,
    privacy and performance constraints.
-   `07_QA_TESTING.md` remains authoritative for test strategy.
-   `09_BUILD_PLAN.md` remains authoritative for roadmap/phase
    sequencing.
-   `10_DESKTOP_PET_SYSTEM.md` is the parent Desktop Pet specification.
-   `12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md` defines AI-driven behavior
    composition and must consume this document's character
    identities/capabilities.
