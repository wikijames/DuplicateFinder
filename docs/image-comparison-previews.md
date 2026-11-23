# Exact duplicate image comparison

Select an image file row in a duplicate group, then choose **Compare images**. The comparison shows the protected keep file on the left and an extra copy on the right. If the selected row is the kept file, the first extra copy is shown. Previous/Next copy changes only the preview; the existing **File to keep** selector remains the sole way to change the protected file. The dialog has no cleanup or keep-selection actions.

Both panes show the full path, current image dimensions, current encoded file size and preview status. The extra-copy heading reflects the selected quarantine/permanent-delete action. These are review labels, not execution: cleanup still requires the existing separate confirmation and validation. Close or Escape returns focus to the preview button. At narrow sizes the panes stack vertically inside a scrollable body; Close remains outside the scroll area.

## Format and resource limits

- PNG and JPEG (`.png`, `.jpg`, `.jpeg`) only. Other formats retain existing text/metadata behavior. The Images scan preset still finds exact duplicates of all its listed formats; preview support is a smaller set.
- Read-only Windows local fixed/removable paths, at most 16 MiB encoded, 12,000,000 source pixels and 8192 pixels per side. Linked files and reparse-point parents are rejected using locked native handles. No write/delete access is requested, and no preview file is written.
- Source dimensions are checked through the existing Skia codec before allocating the source bitmap. Decode is sequential on a worker: one kept image, then one extra copy. The source bitmap is disposed after generating a thumbnail with a longest side of at most 768 pixels. Only two thumbnail bitmaps are retained by the dialog. No list thumbnails or group-wide prefetch/cache are created.
- JPEG EXIF orientation is applied to the displayed thumbnail. The preview is a static raster; animation, zoom, PDF rendering and visual-similarity matching are outside this phase.
- Cancellation is checked between bounded reads and around decode. An in-progress native decode cannot be interrupted; closing hides the dialog immediately and releases remaining resources when that bounded worker operation completes. Navigation is disabled during loading so requests cannot accumulate.
- Corrupt, incomplete, oversized, missing, locked and unsupported images show a readable fallback. Preview messages do not include exception text or identifying paths; the selected paths are intentionally visible in the local dialog.

Previews read **current** file contents. They do not certify that files still match scan results. Scanning, hashing, byte verification, hard-link exclusions, cleanup validation, quarantine and restoration services are unchanged. No additional packages or public service contracts are introduced; the existing bundled Skia decoder is reused.

## Validation

The automated suite adds generated PNG/JPEG cases for portrait/landscape downscaling, unchanged source bytes, source dimension/pixel/file limits, unsupported and disguised formats, corruption/truncation, locked/missing/hard-linked files, linked parents, cancellation and all eight JPEG orientation values. The existing Windows review smoke script also exercises exact image scanning, a nondefault keep choice, extra-copy navigation, Light/Dark/System themes, compact dialog sizing, focus return and unchanged source hashes. It never confirms cleanup.

Screen-reader output, rendered contrast, high contrast and the wider DPI/display/storage matrix still require the release acceptance checks. Native codec security and attribution remain part of the existing release review; this feature does not close those gates.
