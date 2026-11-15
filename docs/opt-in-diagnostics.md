# Opt-in local session diagnostics

Diagnostics starts off on every launch. Open **Diagnostics: off**, choose a 16 KiB or 64 KiB maximum, then select **Choose new file and start**. Select a new filename in an existing local folder. Cancelling the picker leaves recording off. Existing files are never overwritten, appended to or reused.

The recorder captures only events after opt-in; there is no retrospective capture. It records UTC timestamps, schema version, fixed operation/outcome names, broad failure categories and optional counts. For completed scans, `Files` means files in duplicate groups, not all discovered files; `Groups`, `SkippedFiles` and `SkippedFolders` describe that scan. For cleanup/restore, `Files` means completed affected files. Interrupted recovery counts are progress at interruption, not a guarantee that every entry has finished.

It accepts no arbitrary event text. File paths, names, extensions, file content, preview text, exception messages/types, stack traces, machine names and user names are absent from the schema. Failure categories are None, Missing, AccessDenied, InputOutput, InvalidInput and Other. The recorder does not attach to Avalonia/general trace output. Existing sanitized trace reporting remains separate. There is no upload, telemetry service, export-to-server action or crash-dump capture.

## Retention and limits

- Recording is session-only. The choice is not persisted or automatically enabled next launch.
- Each selected file is limited to 16 KiB or 64 KiB. Complete records are reserved against that cap before enqueueing. At the cap, recording stops; it does not rotate or open another file.
- A background writer accepts at most 64 queued records. If the queue fills, new recording stops while accepted records drain. A write failure disables recording without retrying the operation or changing scan/cleanup results.
- **Stop and close file** stops acceptance, drains accepted records and releases file handles. Normal app close also drains the queue. The header indicates on/off, limit or failure; open the dialog for state and controls. Dialog state is refreshed when reopened.
- Saved files remain until the user deletes them. There is no automatic deletion, expiry, rotation, global log-directory sweep or total-size limit across files the user separately chooses. Stop recording, review saved files and retain only what is needed.
- Records are flushed through the stream after writes; this is not guaranteed durable storage against power loss. A crash, forced termination or disk failure can leave missing records or a partial final JSON line. Readers should ignore an incomplete final line. No automatic repair occurs.

Use local storage. Recording creates a new file through Windows local-path validation, locked ancestor directories and a create-new handle. Existing paths and linked files cannot be overwritten; reparse ancestors are rejected. Directory handles remain held until the recording is closed, so stop before moving its containing folder. Native Windows identity/path guards are shared with the existing handle utility; no delete access is requested. Files use normal inherited destination permissions and are not encrypted. Choose an appropriate destination and review timestamps/counts before sharing. The product does not promise operating-system isolation or redact a file after the user modifies it externally.

## Validation

Release build: zero warnings/errors. Full suite: 215 passing tests. Eight new tests cover closed-schema privacy, error categorization, drain/idempotent stop, size bounds, queue pressure, write failure, invalid fields/limits and native create-new refusal to overwrite existing content.

Running Windows UI checks used synthetic fixtures and a generated destination only. They confirmed off-by-default state, explicit native save selection, scan start/completion records with correct duplicate counts, absence of fixture paths/names/content, the selected 16 KiB bound, explicit stop and app close afterward. No personal directory was scanned and no real user log was deleted.

This does not complete physical-storage failure testing or release readiness. Blocked storage can delay the background writer and normal shutdown drain; scan operations do not wait for individual writes, and the bounded queue stops accepting records under pressure. High contrast, display scaling, the broader filesystem matrix, package/license review, packaging and clean-machine acceptance remain in the [Windows release checklist](windows-release-checklist.md).
