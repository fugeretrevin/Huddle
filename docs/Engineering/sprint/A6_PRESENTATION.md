# A6 - Presentation system (type scale, text fit, plan review layout)

Status: pure logic only, uncompiled, not applied to any UI. Code: `Assets/MuseDesk/Scripts/Spatial/Core/Ui/` (`TypeScale.cs`, `TextFit.cs`, `PlanReviewLayout.cs`), tests: `Assets/MuseDesk/Tests/EditMode/Ui/PresentationTests.cs`.

## Type scale (mm, TMP at scale 0.01)

| Token | mm | Weight | Use |
|---|---|---|---|
| Title | 7.0 | Bold | plan / paper title |
| Heading | 6.0 | Bold | section heads ("Work items") |
| Body | 4.8 | Medium (Bold for item titles) | request, objective, item titles |
| Caption | 4.4 | Medium | item detail, expected outputs, risks, chips |
| Provenance | 4.2 (floor) | Medium, InkSoft | provenance footer; nothing in the project goes below this |

## Legibility rule

x-height must subtend at least 0.30 deg (hard floor), 0.40 deg is comfortable.
x-height mm = 2 * d * tan(theta/2); font mm = x-height / 0.546 (Inter x-height ratio). `MinMmAt(d) = max(4.2, that)`.

| Distance | Min mm (0.30 deg) | Comfort mm (0.40 deg) |
|---|---|---|
| 0.30 m | 4.2 (floor) | 4.2 |
| 0.44 m | 4.2 | 4.2 |
| 0.50 m | 4.8 | 6.4 |
| 0.60 m | 5.8 | 7.7 |
| 0.80 m | 7.7 | 10.2 |
| 1.00 m | 9.6 | 12.8 |

Consequence worth knowing: the 4.2 mm provenance size is only at the hard floor out to about 0.44 m. The Wall near-user plan card is read at roughly 0.5 m, where the floor is 4.8 mm, so today's Caption (4.4) and Provenance (4.2) sizes sit slightly below it. `TextFit` raises them to the surface minimum (so at 0.5 m Caption collapses onto Body 4.8) rather than shrinking. The distances are surface properties the caller must supply; the true on-head distance for each surface needs a headset check.

## TextFit rules

- Candidates are ladder sizes in [MinMmAt(distance), preferred]. The largest that fits wins.
- If none fits, use the smallest legible size, truncate with an ellipsis, and set `ShowMore`. Never shrink below the minimum.
- If the minimum is above every ladder size (far surfaces), the minimum itself is used (`ClampedToMinimum`).
- Width model: average em width Regular 0.53, Medium 0.55, Bold 0.58; 92% wrap efficiency; line height 1.25 em. These are estimates, not TMP measurements. A runtime adapter should confirm with `TMP_Text.GetPreferredValues` and fall back to the estimate only when TMP is unavailable.

## PlanReviewLayout rules

Input is a `PlanModel` mapped from the backend plan (`backend/app/phase3/manager.py`: title, objective, items[client_item_id, title, kind, proposed_agent, depends_on, acceptance_criteria], assumptions, risks, status, provenance). Output is ordered rows (kind, text, size, tone, lines, height, ShowMore) plus provenance text.

- State chip: Proposed = Amber ("PROPOSED - nothing has started"), Committed = InfoBlue. Teal is reserved for running; neither state uses it.
- Provenance footer from the source field only: `SCRIPTED_DEMO` -> `[SCRIPTED DEMO]`, `LIVE_MODEL` -> `Model-written`, `TEMPLATE_PLAN` -> `Template plan (not model-written)`, missing -> `Source not stated`, unknown -> echoed. Never defaults to live.
- Missing fields produce no row. Dependencies show the item number when resolvable, else the raw id. "Expected outputs" are the backend `acceptance_criteria`.
- With a height budget, whole blocks are dropped from the end and a "+N more ... (tap More)" row is added. The footer is always kept.
- At far distances every row clamps to the same minimum size, so hierarchy flattens; use weight and colour then.

## Adapter plan (not applied)

1. `Scripts/Wall/WallUi.cs:230-233` (WallCard title 6.5, body 4.6, foot 4.2): replace literals with `TypeScale.TitleMm/BodyMm` and `Mathf.Max(x, TypeScale.MinMmAt(cardDistance))`; footer uses `TypeScale.ProvenanceFloorMm` raised to the minimum.
2. `Scripts/Wall/WallUi.cs:164-173` (`MakeText`): optionally add an overload taking a `TextFitResult`; set `t.fontSize = fit.SizeMm` and `t.text = TextFit.Apply(...)`.
3. `Scripts/Wall/WallUi.cs:264` (`WallCard.SetText`): keep the signature; inside, run `TextFit.Fit` for title (maxLines 2) and body, and show a "More" key when `ShowMore`.
4. `Scripts/Wall/WallHabitatController.Manager.cs:147-158` (AwaitingApproval): build a `PlanModel` from `_manager` (Title, Objective, Items with `proposedAgent`, `dependsOn`; Source = `_manager.Provenance`) and render `PlanReviewLayout.Layout` rows instead of the string builder; this replaces the hard `i < 4` cap with the budget-driven "+N more". The `ProvenanceWords` call (line 158) maps to `PlanReviewLayout.ProvenanceLabel`.
5. `Scripts/Documents/DecisionPanel.cs:170-184` (badge, needsLabel, fromLabel, title, summary text assignments): size title/summary through `TextFit.Fit` with the paper's viewing distance (hand-held, about 0.4 m) and route provenance through `ProvenanceLabel`. Colour tones map to `OfficeTokens` via a small switch in the adapter (`PlanPalette` already holds the same hex values).
6. Add a small EditMode parity test in the Workspace assembly asserting `PlanPalette.Of(tone)` equals the matching `OfficeTokens` value, since Core cannot reference it.
