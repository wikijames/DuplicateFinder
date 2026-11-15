# Opt-in local session diagnostics

Diagnostics starts off on every launch. Open **Diagnostics Off**, choose a 16 KiB or 64 KiB maximum, then select **Choose File & Start Recording**. Select a new filename in an existing local folder. Cancelling the picker preserves the previous inactive state. Existing files are never overwritten, appended to or reused.

The recorder captures only events after opt-in; there is no retrospective capture. It records UTC timestamps, schema version, fixed operation/outcome names, broad failure categories and optional counts. For completed scans, `Files` means files in duplicate groups, not all discovered files; `Groups`, `SkippedFiles` and `SkippedFolders` describe that scan. For cleanup/restore, `Files` means completed affected files. Interrupted recovery counts are progress at interruption, not a guarantee that every entry has finished.

It accepts no arbitrary event text. File paths, names, extensions, file content, preview text, exception messages/types, stack traces, machine names and user names are absent from the schema. Failure categories are None, Missing, AccessDenied, InputOutput, InvalidInput and Other. The recorder does not attach to Avalonia/general trace output. Existing sanitized trace reporting remains separate. There is no upload, telemetry service, export-to-server action or crash-dump capture.

## Retention and limits

- Recording is session-only. The choice is not persisted or automatically enabled next launch.
- Each selected file is limited to 16 KiB or 64 KiB. Complete records are reserved against that cap before enqueueing. At the cap, recording stops; it does not rotate or open another file.
- A background writer accepts at most 64 queued records. If the queue fills, new recording stops while accepted records drain. A write failure disables recording without retrying the operation or changing scan/cleanup results.
- **Stop Recording** stops acceptance, drains accepted records and releases file handles. Normal app close also drains the queue. The header distinguishes off, recording, stopped, limit and failure. The open dialog shares the existing 500 ms UI refresh while recording; there is no additional timer or background monitor.
- Saved files remain until the user deletes them. There is no automatic deletion, expiry, rotation, global log-directory sweep or total-size limit across files the user separately chooses. Stop recording, review saved files and retain only what is needed.
- Records are flushed through the stream after writes; this is not guaranteed durable storage against power loss. A crash, forced termination or disk failure can leave missing records or a partial final JSON line. Readers should ignore an incomplete final line. No automatic repair occurs.

Use local storage. Recording creates a new file through Windows local-path validation, locked ancestor directories and a create-new handle. Existing paths and linked files cannot be overwritten; reparse ancestors are rejected. Directory handles remain held until the recording is closed, so stop before moving its containing folder. Native Windows identity/path guards are shared with the existing handle utility; no delete access is requested. Files use normal inherited destination permissions and are not encrypted. Choose an appropriate destination and review timestamps/counts before sharing. The product does not promise operating-system isolation or redact a file after the user modifies it externally.

## Validation

Release build: zero warnings/errors. Full suite: 215 passing tests. Eight new tests cover closed-schema privacy, error categorization, drain/idempotent stop, size bounds, queue pressure, write failure, invalid fields/limits and native create-new refusal to overwrite existing content.

Running Windows UI checks used synthetic fixtures and a generated destination only. They confirmed off-by-default state, explicit native save selection, scan start/completion records with correct duplicate counts, absence of fixture paths/names/content, the selected 16 KiB bound, explicit stop and app close afterward. No personal directory was scanned and no real user log was deleted.

This does not complete physical-storage failure testing or release readiness. Blocked storage can delay the background writer and normal shutdown drain; scan operations do not wait for individual writes, and the bounded queue stops accepting records under pressure. High contrast, display scaling, the broader filesystem matrix, package/license review, packaging and clean-machine acceptance remain in the [Windows release checklist](windows-release-checklist.md).

## Diagnostics UI refinement

The dialog has four sections: a text-and-color recording status card, a compact local-privacy notice with **What information is recorded?**, **Recording Settings**, and **Privacy and retention** with expandable limits/retention details. Existing theme colors and focus styles are reused. The primary action changes between Start and Stop according to the same existing eligibility rules. Close stays outside the vertically scrolling content. Keyboard focus moves to the available recording action after starting or stopping.

The maximum-size selector applies to the next recording only; it does not resize an active recording. The UI does not retain/expose the chosen destination, elapsed time, written bytes or other live metrics, so those fields are not invented. Native file-picker paths may be shown in the interface; the privacy wording describes the unchanged record schema separately.

Off means no recorder exists. Stopped is the existing `Stopped` state. File-size and queue limits have distinct reasons in the dialog, while the header shows a compact limit label. An unsuccessful attempt to choose/create a file shows an inline error and preserves the previous recorder state; it is not presented as a `WriteFailed` recording state. The recorder, JSON schema, limits, file creation, filtering and shutdown lifecycle are unchanged.

The dialog uses a 420×280 minimum with vertical scrolling and sizes its initial window against the current monitor's work area and scaling. This layout accommodates smaller work areas; it does not constitute tested certification at every DPI setting.

### Refinement validation

- Release build: zero warnings/errors; all 215 existing tests pass. No existing tests were removed or weakened.
- The extended interactive UI smoke script checks accessible expander names and Space-key expand/collapse, the exact 16/64 KiB choices, off-by-default state and visible actions at the compact size under Light, Dark and System selections. Existing review checks and the 301-group synthetic scan still pass.
- Native-picker checks with generated fixtures verified start, dialog reopen while recording, scan counts, stop/drain, a distinct stopped header and picker cancellation without creating a recording.
- A 64 KiB recording continued to 17,511 bytes. A separate default 16 KiB recording stopped at 16,270 bytes, preserved valid JSON lines and updated the header/dialog with the file-size-limit reason through the existing timer.
- Selecting an existing synthetic log, including confirming the native overwrite prompt, was refused by the unchanged create-new guard. Its SHA-256 hash stayed unchanged, and the previous limit state remained alongside an inline start error.
- Synthetic paths, filenames and file contents remained absent from recorded data. No personal directories were scanned and no user logs were deleted.
- Actual display scale tested: 100% (96 DPI). Windows 125%, 150% and 200%, cross-monitor DPI moves, Narrator speech, high contrast and rendered hover/disabled-state contrast have not been manually validated in this task. Queue-limit/write-failure service tests remain passing; those two failure states were not forced through the interactive UI.

### Reference-inspired themes and control sizing

The subsequent visual direction uses the supplied screenshots as color/spacing references while retaining DuplicateFinder's layout and assets. Light uses an off-white `#F7F6FB` canvas, white panels and a violet `#6650CE` primary accent. Dark uses a near-black `#0D0D0F` canvas, charcoal `#1B1B1E` panels, blue `#2D8CF0` primary actions and orange `#FFB366` warnings. Destructive actions retain their own red treatment. Fluent's native accent palettes match these choices, including dropdown/focus interactions. System appearance continues to follow the OS Light/Dark selection.

Body and input/button text use 14 logical units; supporting main-window text uses 13, with small utility labels at 12. Buttons, textboxes and dropdowns have a shared 38-unit minimum height, consistent padding and 6-unit corner radii. Single-line input text is centered; multiline previews remain top-aligned. Inputs have explicit theme-aware surfaces and stronger borders than decorative panel outlines.

Twenty resource foreground/background pairs were checked: text/focus pairs are at least 5.17:1, and input-border contrast is 3.29:1 in Dark and 3.35:1 in Light. These are static resource checks, not a certification of every rendered state. The Release build remains clean, all 215 tests pass, and the extended UI smoke test still passes under the three appearance selections with compact windows. No scan/recording behavior or dependencies changed. The outstanding display-scaling, screen-reader and rendered-state checks above remain open.
