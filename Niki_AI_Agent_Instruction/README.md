# Niki AI — Agent Build Instruction Pack

This repository-style pack is the source of truth for building **Niki AI**, a lightweight Windows desktop AI agent with a small 2D companion character, desktop widgets, real-time task execution, memory, scheduling, browser automation, application control, and a permission-controlled tool system.

## How the AI coding agent must use this pack

Before implementing any task, read these files in order:

1. `01_CORE_CONCEPT.md`
2. `02_FEATURES_AND_SCOPE.md`
3. `03_DESIGN_SYSTEM.md`
4. `04_ARCHITECTURE.md`
5. `05_SECURITY_AND_PERMISSIONS.md`
6. `06_AVOID_AND_GUARDRAILS.md`
7. `07_QA_TESTING.md`
8. `08_WORKFLOW_PROMPT.md`

Then execute the user's task while treating the documents as a single specification.

## Critical environment rule

**If Google Chrome is unavailable on the target PC, use another available browser such as Microsoft Edge or Brave for browser-based testing, documentation, preview, or web automation.**

## Primary approved visual reference

The image in `assets/niki-ai-selected-reference.png` is the currently approved high-level visual direction. It is a reference, not a license to copy every detail literally. Preserve the concepts the user approved: orange identity, dark/light glass surfaces, compact widgets, small pixel companions, structured utility panels, and a polished modern desktop experience.

## Recommended build philosophy

- Build the smallest reliable vertical slice first.
- Keep design tokens centralized.
- Keep AI providers behind an abstraction layer.
- Keep tools separate from the model.
- Put permissions between the planner and every sensitive tool.
- Prefer deterministic, testable operations over magical automation.
- Verify every feature instead of assuming that generated code works.
- Avoid unnecessary dependencies and background loops.
- Never silently bypass errors, permissions, or validation.

## Target

Windows-first, lightweight, professional, highly customizable, and maintainable. The desktop companion should feel present without becoming distracting, while the full application panel should feel like a serious productivity application.
