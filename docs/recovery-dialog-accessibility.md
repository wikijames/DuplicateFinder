# Recovery batch selector accessibility

This is a focused UI fix for **Select recovery batch**, using the existing review-dialog layout. It does not change batch inspection, quarantine, restore, matching or cleanup contracts.

## Reproduced issue

On merged baseline `8107f4b`, the selector used an unconstrained window with a single stack of instructions, input, validation and actions. A local UI Automation resize request of 480 × 220 placed the Review restore action partly outside the reported window bounds. `IsOffscreen` alone did not detect this clipping. Cancel also did not receive initial keyboard focus.

## Changes

- Reuse the existing review-dialog helper: scrollable body, actions outside the scroll area, and minimum logical size 480 × 360. The selector opens at 640 × 360.
- Put initial focus on Cancel, preserving its default/escape behavior and the existing return to Restore batch.
- Add a visible field label and bound the wrapping path input to 80 logical pixels of height. Soft wrapping preserves the full path value; no new validation or path normalization is introduced.
- Add stable automation IDs for Cancel, Review restore and selection status, and mark status updates as polite live announcements.
- Give folder Browse buttons contextual accessible names from their existing picker titles. Their visible caption and picker behavior stay the same.

## Validation boundary

The extended Windows review smoke check exercises this selector in Light, Dark and System appearance. It checks initial Cancel focus, complete synthetic long-path values, action bounds at compact size and after an undersize request, Tab/Shift+Tab between input/Browse/Cancel/Review, Escape cancellation, default Enter cancellation and focus return. It never reviews a real batch or confirms recovery in this check.

The separate synthetic recovery runner remains available to verify actual quarantine/restore after the layout change. Original matching, quarantine and restoration services and existing tests remain unchanged.

UI Automation and local keyboard checks do not replace Narrator listening tests, high contrast, rendered contrast, other display configurations or the full 125–200% DPI matrix. No global Windows appearance/accessibility setting is changed by these checks. Private testing remains the approved distribution scope.

## Local results — 23 November 2026

The baseline and fixed Release builds passed all 240 existing tests. The final build had zero warnings/errors. The extended running-app smoke suite passed, including every new selector check in all three appearance modes, existing text/image previews, keep-choice safety, cleanup cancellation and 301 generated groups. The baseline clipping/focus issue was reproduced before the fix; these results are local UI evidence, not general accessibility certification.
