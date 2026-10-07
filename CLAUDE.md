# CLAUDE.md — PWE PC MONITOR

## Writing rules · STE-lite v1

Applies to: procedures, release and rollback steps, handoffs (HANDOFF, "等 Lee"), warnings, and notes for other sessions.

1. One action per step. Start with the verb. Put a condition first: "If …, do …".
2. Sentence length: at most 20 words in English, 40 characters in Chinese. Code, paths and commands do not count.
3. Give each step a success check: the expected output, status code or version. `healthy` or exit code 0 is not evidence unless the step says what it proves.
4. Write warnings as `> ⚠️`. First sentence: what to do or not do. Second: what happens if you don't. Then the reason. One hazard per warning.
5. Use active voice. Name the owner of each to-do.
6. One term, one meaning: use only the words in this repo's glossary. If a new word has two meanings, split it into two words first, then add them.
7. Date every value that can change: "measured 2026-10-03". Mark anything unchecked as "not verified" and say why. Never write a plan as done.
8. Keep steps, reasons and incident stories apart. An incident story never interrupts the steps.

Does not apply to:
- Explanations and reasons: no length limit, but lead with the conclusion.
- History logs (*-LOG.md, dated running entries): leave them as written.
- Product and brand copy in any language, App Store copy, AI prompts that visitors see.
- The format of "等 Lee" entries in handoff files: the global convention stands.
- TTS narration and voice-over scripts: never split sentences (qwen-tts measured 10 of 10 failures after splitting).
- Scripture, classical and cultural content.

Do not rewrite old docs in bulk. Tidy a section by these rules when you change it.

## Scope in this repo
- Applies to: README sections "Run a packaged build" and "Verification boundary"; `docs/release-*.md`.
- Does not apply to: Chinese internal plans (for example `docs/review-v0.7.2-optimization-plan.md`); user-interface copy.

## Glossary
| Use | Meaning (one only) | Not |
|---|---|---|
| floating widget | The optional always-on-top window, toggled by **Show Floating Widget**. | always-on widget |
| detail panel | The expanded part of the floating widget, opened by hover or F2. | hover details, details |
| All Sensors | The full sensor list section, shown by **Show All Sensors**. | full sensor list |
| Sensor diagnostics | The menu that lists channels the hardware or driver did not expose, and the selected GPU source. | Settings diagnostics |
| estimated working-set reduction | The figure that **Optimize memory** reports after a trim. It is an estimate. | free RAM, free memory |
| Release zip | `pwe-pc-monitor-win-x64-<version>.zip` on a GitHub Release. The public download. | published artifact, artifact |
| Actions artifact | The CI upload kept for 30 days for diagnostics. Not a public download. | artifact (alone) |
