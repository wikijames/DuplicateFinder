# Image and Media Similarity Feasibility

**Roadmap item:** DF-401  
**Status:** Initial evaluation; no algorithm or image-decoding dependency selected.

## Scope and boundary

Visual similarity is a separate, opt-in result category. `DuplicateDetector` continues to mean byte-identical files; a perceptual match must never be reported as an exact duplicate or passed directly to cleanup. Similarity candidates remain review-only.

DF-401 will compare image-hash approaches and image-decoding constraints. Video similarity is a separate problem involving frame sampling and temporal comparisons; it is out of scope for the first image prototype and must not be inferred from image results.

## Candidate image methods

| Method | Evaluation role | Main trade-off |
| --- | --- | --- |
| Average hash (aHash) | Fast baseline after grayscale normalization and downsampling | Very low cost, but coarse and sensitive to content/layout changes |
| Difference hash (dHash) | Fast gradient-based baseline | Low cost; may miss broader visual relationships and can be affected by crop or rotation |
| Perceptual hash (pHash) | Primary candidate for resized or recompressed variants | More processing than simple hashes; thresholds still require corpus-specific calibration |

These hashes can be compared using Hamming distance. A distance threshold is a candidate-generation rule, not proof of equivalence; there is no threshold selected in this phase. OpenCV documents these and other image-hash algorithms, which supports the comparison set but does not determine the implementation dependency.

## Preliminary generated-fixture run

A temporary .NET 10 harness compared the three algorithms from `PerceptualHash.NET` 0.4.1 using generated 128x128 images. The table reports Hamming distance from one patterned baseline and average time/managed allocation for 50 warm hashes on this development machine.

| Algorithm | Resize + JPEG 80 | Brightness 1.08 | Crop + resize | Rotate 90 | Unrelated pattern | ms/hash | KiB allocated/hash |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| aHash | 0 | 0 | 13 | 58 | 29 | 1.838 | 41.2 |
| dHash | 0 | 0 | 16 | 56 | 32 | 1.700 | 41.4 |
| pHash | 2 | 0 | 14 | 38 | 23 | 1.797 | 46.0 |

This tiny synthetic set only checks basic behavior; it does not establish a safe threshold or represent photographic libraries. Crop and rotation distances were large, and the unrelated pHash distance was only 23 bits, reinforcing that hash distance must remain a review candidate rather than a duplicate decision. Allocation is reported, not peak memory.

## Candidate package screening

| Candidate | Evidence | Decision |
| --- | --- | --- |
| `CoenM.ImageSharp.ImageHash` 1.3.6 | MIT; aHash, dHash, pHash; about 1.1M NuGet downloads. Last published in 2022 and pins ImageSharp 2.1.3, which is within published vulnerable ranges. | Do not adopt without a maintained release and a patched decoder dependency. |
| `PerceptualHash.NET` 0.4.1 | MIT; targets modern .NET, has cross-platform golden tests and a configurable pixel budget. Its README documents a single ImageSharp runtime dependency, currently pinned to 3.1.12; NuGet reports low adoption (590 downloads at review time). | The fixture run worked, but restore reported six advisories in the pinned decoder. Current ImageSharp advisories set 3.2.0 as the v3 patch floor. The prior 3.2.0 spike required a Six Labors license key at build time; the package author documents staying on 3.1.12 for compatibility with its consumers. Do not adopt until a patched, reproducible restore/build and license review succeed. |
| `SixLabors.ImageSharp` 4.1.3 | Active, fully managed decoder. The September 2026 advisories reviewed here are patched in 4.1.2 and later. Its Split License grants Apache 2.0 for open-source/source-available use, transitive dependency use, qualifying small for-profit direct use, and nonprofit/charity use; other direct use requires a commercial license. | Security status is improved, but the project's eligibility for Apache terms as a direct dependency is not established. The prior 4.1.3 spike also encountered a license-key build requirement. Do not adopt directly until license eligibility and a clean, reproducible build are confirmed. |
| `SkiaSharp` 4.156.0 | MIT; actively maintained and cross-platform, with platform-specific native assets. NuGet reports the Win32 native-assets package at 84.73 MB compressed. | Not a similarity library and adds native deployment complexity. Could be screened as a decoder with independently implemented hashes, but package footprint, published Windows output size, and formats still need measurement. |
| OpenCvSharp 5 | Active .NET wrapper; its full native profile includes OpenCV contrib hashing, while the slim profile excludes contrib. | Not tested: native runtime footprint and deployment cost are a poor fit for this constrained first prototype without separate approval. |

The fixture metrics above used ImageSharp 3.1.12 and generated, non-TIFF inputs. They are algorithm observations only, not a dependency approval. No package was added to the solution.

## Constraints and safety

- Keep image analysis outside the UI-agnostic exact-match path until the API and decoder are selected.
- Bound decoded dimensions, per-image memory, and concurrency. Check decoder metadata where possible before allocating full pixel buffers.
- Normalize EXIF orientation and define consistent handling for color profiles, alpha channels, and animated images before comparing hashes.
- Treat corrupt, unsupported, or oversized images as per-file analysis failures; preserve scan progress and cancellation rather than aborting an entire scan.
- Keep similarity groups separate from exact duplicate groups and require the existing human review flow for any later action.
- Evaluate decoder licenses, maintenance, supported formats, native assets, deployment size, and Windows-first/cross-platform behavior before adding a package.

## Evaluation plan

Use small, generated fixtures rather than personal photos or downloaded image collections. Include byte-identical images, resized and recompressed variants, modest brightness changes, visually related but distinct images, unrelated images, orientation metadata, alpha transparency, malformed files, and large-dimension inputs.

For each candidate, record false positives and false negatives as well as per-image time and peak memory. Tune thresholds against the fixture set, then keep the threshold configurable and results review-only. Cropping and rotation should be measured explicitly rather than assumed to work.

## Next decision

The security gate has a potentially patched managed decoder path, but no complete candidate clears maintenance, vulnerability, license, and deployment checks together. ImageSharp 4.1.3 is beyond the reviewed patched-version floors, but its direct-use license eligibility and package build requirement remain unresolved. `PerceptualHash.NET` is maintained and MIT-licensed but pins the earlier vulnerable ImageSharp version. SkiaSharp is MIT-licensed and active, but is a native decoder/rendering stack rather than a hash implementation and its deployment footprint has not been measured. Continue DF-401 by testing whether a patched managed path can build under the project's actual licensing conditions, or separately approve a native decoder evaluation. Keep the fixture harness disposable until that dependency gate is resolved; do not alter `DuplicateDetector` yet.

## Sources

- [OpenCV image-hash module documentation](https://docs.opencv.org/4.x/d4/d93/group__img__hash.html) lists supported hash families and describes image-hash comparison.
- [CoenM.ImageSharp.ImageHash 1.3.6](https://www.nuget.org/packages/CoenM.ImageSharp.ImageHash/1.3.6) and its [source repository](https://github.com/coenm/ImageHash) provide the release, framework, and algorithm details used above.
- [PerceptualHash.NET 0.4.1](https://www.nuget.org/packages/PerceptualHash.NET/0.4.1) documents its algorithms, pixel budget, and fixture methodology.
- [ImageSharp advisories](https://github.com/SixLabors/ImageSharp/security/advisories) list affected and patched versions; the reviewed September 2026 advisories set 3.2.0 and 4.1.2 as their v3 and v4 patch floors.
- [ImageSharp 4.1.3 license text](https://www.nuget.org/packages/SixLabors.ImageSharp/4.1.3/license) defines when Apache 2.0 applies and when a commercial license is required.
- [SkiaSharp 4.156.0](https://www.nuget.org/packages/SkiaSharp/4.156.0) and its [deployment guide](https://github.com/mono/SkiaSharp/blob/main/documentation/dev/packages.md) document its current version, MIT license, and native-assets model.
- [OpenCvSharp](https://github.com/shimat/opencvsharp) documents its native runtime profiles and contrib module coverage.