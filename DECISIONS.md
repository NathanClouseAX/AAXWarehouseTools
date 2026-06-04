# DECISIONS.md — recorded Decision Rule applications (plan C-10)

| Date | DR | Decision |
|---|---|---|
| 2026-06-04 | WP-0.1 | Model Id = **896000929** (adjacent to AAXIntegrationOperations 896000928; verified unique across all `*\Descriptor\*.xml`; 895972610 rejected — owned by Microsoft HRPlatform). |
| 2026-06-04 | C-4/F36 | Label prefix `@AAXWarehouseTools:` (LabelFileId = AAXWarehouseTools); content file `AAXWarehouseTools.en-US.label.txt` (dot convention per sibling donor). |
| 2026-06-04 | REQ-R-1 | Renderer dimension policy clarified: `dpmm <= 0` always throws `ZplDimensionsMissingException` (density must come from caller/X++ fallback); `^PW`/`^LL` parsing only supplies width/height. |
| 2026-06-04 | III.7 | C# API adds X++-friendly `ZplRenderResult` (Count / GetPng(i)) because X++ cannot declare closed generic CLR types like `IList<byte[]>`. `RenderToPngList` retained for C# tests. |
