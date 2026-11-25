# DuplicateFinder portable Windows test candidate

Extract the entire ZIP into a folder you own and run `DuplicateFinder.Desktop.exe`. Keep all extracted files together. This self-contained x64 build includes its .NET runtime; it does not need the .NET SDK. Windows 11 x64 is the initial validation target. A minimum supported Windows build and broader compatibility are not yet certified.

The current distribution policy is **private testing only**. Public distribution, an application license and signing are deferred. This is an unsigned test candidate, not an installer or an approved public release. It does not register an installation, add shortcuts, update itself or change file associations. Do not disable Windows security controls to run it. Clean-machine acceptance remains open.

## Test safely

Start with a separate folder of copied/generated files outside the extracted application folder. Add folders, choose file-type/size/exclusion settings, then scan. Results are exact byte matches. Similar-looking images are not similarity results. Review each group and use **File to keep** to protect one copy. Selecting a row selects a preview, not a removal target.

To compare drives, add each drive root (for example `D:\` and `E:\`) using Browse or the path field and Add, then choose **Scan folders**. Selected locations are shown below the path field; **Folders** lists them all. The input clears after adding a location so another can be entered. A combined scan finds matches within and across selected drives; overlapping locations are scanned once. Filters apply to every location, and hidden/system child exclusions still apply when a whole drive is selected. There is no automatic select-all-drives action. Progress shows stages and actual work counters; it does not currently predict a finish time.

Folder entry, file type and scan actions share a row on wide windows and use two rows on smaller windows. Preset extensions are described in the file-type tooltip; selecting **Custom extensions** reveals the editable extensions field and preserves your previous custom entry when switching categories.

Text preview supports bounded UTF-8 files. For PNG/JPEG images, click the underlined image name in either the group navigator or the file list to view the kept image beside an extra copy. You can also focus an image link and press Enter or Space. Clicking other parts of a group or file row only selects it. Previous/Next changes only the preview, preserving your keep choice; closing the comparison returns focus to its image link. Image previews are limited to 16 MiB, 12 megapixels and 8192 pixels per side; other formats and oversized/damaged files retain metadata or an explanatory fallback. Previews show current file contents and do not reverify scan matches. Non-image rows retain the separate **Preview file** action.

Review warnings for skipped, changing, inaccessible or hard-linked files. A completed operation does not guarantee that every path was scanned. Cancel scans with the Cancel button or Escape. Large storage calls may delay cancellation.

## Quarantine and restore

Quarantine is the default cleanup action and does not free disk space. Review the keep file, removal paths and destination before confirming. Keep all batch contents and the protected manifest. Restore requires the original Windows user profile. Use **Restore batch** to recover files; existing destination files are never overwritten. Resolve conflicts and review interrupted batches before retrying. Do not automatically purge batches.

Cross-drive groups can be reviewed but may be unavailable for quarantine. Permanent deletion is a separate, irreversible choice with its own confirmation and validation. No files are removed automatically.

## Diagnostics

Diagnostics starts off each launch. Opt in only when needed, choose a new local file and select a 16/64 KiB maximum. Records contain fixed events, UTC times, broad errors and counts, excluding paths, filenames, contents, exception messages and stack traces. Nothing is uploaded automatically. Stop before moving/sharing a saved log and review it first. Saved files remain until you delete them; limits stop recording rather than rotating files.

## Replace or remove a portable copy

Close the app before replacing its application folder. Extract a newer candidate into a separate folder and verify it first. Removing an application folder is not a recovery procedure: retain quarantine batches/manifests and any logs you need separately. The portable package has no uninstaller or automatic update process.

## Report a test issue

The package includes `MANUAL-ACCEPTANCE.md` for the remaining hands-on checks. Copy it outside the extracted application folder before recording Pass/Fail/Not tested results. Keep the packaged original unchanged for integrity checks. Narrator, contrast themes, display scaling and unavailable test environments must remain Not tested until actually exercised.

Record the source commit from `package-info.json`, Windows version, display scaling, chosen action and a reproduction using generated files. Do not include personal paths, documents or diagnostic files without reviewing them. A previous candidate had one undiagnosed first-scan IOException; retry and subsequent fresh-process tests passed. Report any recurrence rather than treating it as fixed.

Narrator, high contrast, 125–200% DPI, physical SSD/HDD, removable/filesystem coverage, interrupted-storage testing and a clean machine without the SDK remain acceptance gates. Dependency reports and available notices accompany the package; they are not a legal-compliance or security certification. The application license/distribution choice and native/font attribution review remain open before public release.
