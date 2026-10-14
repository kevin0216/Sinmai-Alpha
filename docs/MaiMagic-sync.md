# MaiMagic compatibility and performance sync

Reviewed 2026-10-15. Target: this checkout at `cd15585` (local branch `main`, version 0.1.6). The reference is Claude's `0.1.4+maimagic.3` source under `D:\MaiMagic\Sinmai-Alpha\Mods\Sources\Sinmai-Alpha`, with its interop notes and saved harnesses. The faithful 0.1.4 baseline was used to distinguish deliberate patches from decompiler changes. No remote branch was fetched or switched.

The changes were merged into the current source by member. Current chart behavior, shaders and assets remain the basis of the build. In particular, the merge preserves off-ring spawning, late WiFi autoplay, separate PV rotation, modern/legacy kaleidoscope angles, styled noise zones, three-track result identities, and owner-scoped pool routing with exception finalizers.

## Compatibility

- Optional display bindings prefer `MaiMagic.*`, then `AquaMai.*`. They rebind after host assembly loads and read live values through typed delegates: single-player mode, submonitor hiding, practice speed, render camera and display images.
- Duplicate-patch guards recognize both hosts. A replacement is guarded only after Alpha successfully applies its corresponding patch group.
- Optional volume synchronization accepts the old `float[]` stores and leaves incompatible stores, including newer dictionaries, alone. Missing members are cached until another host assembly loads.
- Mine audio removes a pending batch before playback and contains playback failures, so a throwing player cannot replay stale queued hits.
- Existing public interop names (`IsMineNoteOwner`, `ReadMode`, `ModeChart`) and the identity hooks used when ExtraDifficulty is disabled remain available.

## Which older performance fixes still apply?

| Area | Finding in the current source | Merged result |
| --- | --- | --- |
| Input and note hooks | Hot hooks still box `object[]` arguments and use repeated reflection. | Typed Harmony arguments, cached field delegates, direct protected-field access, and a note-ID lookup table. |
| Noise input | The input timeline still scans events repeatedly. | Binary-searchable blocked intervals and a live-event index, with scan fallback for exceptional data. |
| Visual commands | Resolving each note's color, size and opacity still walks command lists and allocates. | Indexed lookup plus reusable result objects; preserved mine/break/each precedence and blockarea isolation. |
| Tint pixels | The current branch already has a guarded rectangle fast path. Per-pixel HSV work and source readbacks still repeat. | Kept the newer geometry guard; added pixel memoization, source-readback caching, value keys and removal of an unnecessary upload. |
| Tint warmup | The branch already warms shaders and each-guide colors, but bakes all guide colors synchronously. | Kept shader warmup; replaced the synchronous bake with loading-frame work capped at 32 new keys across both monitors. |
| Practice seeks | Seek cleanup still discards reusable tint resources. | Restore renderer bindings on seeks; release chart-owned tint resources at chart release. |
| Subtitles | Missing fallback glyphs and repeated visibility toggles still cause rebuild work. | Batch glyph preload and retain labels that stay visible. |
| Motion | Scene-wide collision scans and duplicate hierarchy walks still run at effect onsets. | Ported load-time reservation/priming, reused walks within a render callback, pooled isolation targets and JIT warmup. |
| Screen effects | Copies, trail history shifts and idle evaluation still have avoidable work. | Rotate trail targets, gate unread history conservatively, use the supported aligned-copy path, and defer uniforms until a draw. Current effect-angle behavior remains intact. |
| Pools | All mirror pools are created even when a chart does not use them. | Scan chart needs, create required pools, and defer others with their original capacities; retain the current scoped routing and practice collection. |
| UI and reserves | Theme lookups, sprite-name reads, hierarchy arrays and dead reserve entries still repeat. | Ported the flag index/watch, memoized lookups, reusable lists, pruned identity/lease dictionaries, and reserve pruning. Summary cards participate in pruning too. |
| Chart lifetime | Note-keyed weak tables still retain keys under the game's Boehm Mono behavior. | Replace chart-scoped tables at lifecycle boundaries. |
| Diagnostics | This branch already has `GameplayDiagnostics`, frame statistics and a bounded writer. | Retained these instead of adding the older, overlapping `FrameDropLog` implementation. Use `"GameplayDiagnostics": true` for profiling. |

## Additional findings addressed in this branch

1. **Styled noise multiplies scanning and parsing costs.** `RenderAt` rescanned every event once per sensor, while warning-distance calculations repeatedly split SV/HS strings. Noise selection now takes one event pass; decoded speed tracks use binary search and parse multipliers once. Signed scroll accumulation, equal-time precedence, NULL resets and tolerance boundaries are preserved.
2. **The old noise raster cache cannot be copied directly.** Current zones have independent color, size, alpha and growth/shrink behavior. The new cache includes these inputs, owner/geometry, field and camera matrices, and viewport. Animated intervals redraw, and the first stable frame replaces the last animated raster. Warning-mesh caching uses full float style values. Shader animation continues every frame.
3. **Reader timelines survive completed charts.** Media, judge-line, presentation, noise, visual and reserve tables now reset at release; expansion timelines reset with their leases. They are not cleared at game start because readers can already be loaded. Practice seeks retain their chart data.
4. **Pool routing allocates sets per custom note.** Pool-field sets are now reused per native note type. Callers only read or copy these sets.
5. **The custom-level font readback uploads a texture that is immediately read on the CPU.** Removed that upload and unnecessary mipmap recalculation. The larger atlas readback itself remains.

The source checkout also referenced two absent helpers, `OwnerCallScope` and `SssJudgePathGeometry`. Added them and narrowed `.gitignore` from `Libs/` to `/Libs/`, so source helpers under `src/Notes/Libs` are tracked. The scope handles nested calls, stock-note masking, two owners, exceptions and resets. Geometry matches the native touch rotation and judge-region centers.

## Validation performed

| Check | Result |
| --- | --- |
| Release rebuild with 1.66 game references | Passed, zero warnings/errors. |
| Release rebuild with 1.70 game references | Passed, zero warnings/errors. |
| Cross-version method comparison | 3,013 method instruction streams match. The DLL hashes/module IDs differ, so the builds are not described as byte-identical. |
| `tests/SyncRegression` | 3,412,293 checks passed: original scan oracles versus indexed visuals/noise, styled-zone selection, SV/HS, seeks, routing scopes and geometry. |
| `tests/SyncRenderingState` | 26 checks passed against the built DLL's managed noise-cache decisions. No native rendering is invoked. |
| Existing performance diagnostics tests | Passed: statistics, overflow, bounded concurrent writer and shutdown drain. |
| Claude compatibility harness | 35/35 passed, including late/concurrent assembly loads and live accessors. Nine million getter reads produced no Gen0 collections. |
| Claude field-access/SV differential harness | 775,559 checks, zero mismatches. |
| Claude mine-audio flush harness | 6/6 passed for each game-version fixture. |
| Claude volume-sync harness | All seven host/storage scenarios passed, including absent, legacy, incompatible and throwing hosts. |

The new pure regression suite measured zero allocated bytes for 20,000 warmed speed/visual lookup pairs. A desktop .NET CPU sample of 2,000 speed lookups over 200 controls took about 35 ms with the scan oracle and 0.12 ms with the compiled track. This is an isolated lookup measurement, not a game FPS estimate. Claude's historical performance measurements were not used as results for this build.

Run the repeatable repository checks after populating `Libs` with `build.ps1`:

```powershell
./build.ps1 -GamePackage 'D:\SDEZ1.70\Package'
dotnet build Sinmai-Alpha.csproj -c Release --no-restore -t:Rebuild
dotnet run --project tests/SyncRegression/Tests.csproj -c Release
dotnet run --project tests/SyncRenderingState/Tests.csproj -c Release
dotnet run --project tests/PerformanceDiagnostics/Tests.csproj -c Release
```

The regression suite needs .NET 10; the mod and managed rendering-state harness target .NET Framework 4.7.2. The legacy compatibility harnesses are local external test assets; their captured output is in ignored `artifacts/` alongside the build and regression logs. The installable package is generated under `dist/Package` and archived as `dist/Sinmai-Alpha-0.1.6-MaiMagic-sync.zip`. It contains the build using 1.70 references; the 1.66 build is also retained in `artifacts/build166`.

## Remaining performance work and runtime validation

- Full-screen presentation copies, motion-driven canvas rebuilds, and expensive effects such as shatter/glow remain GPU or Unity-native costs. They were preserved because changing their rendering path can alter output.
- New subtitle glyphs still require a font upload and kerning pass. Custom-level text can still read an entire atlas after dynamic glyph updates. Batching reduces repetition but does not eliminate first-use work.
- Tint colors beyond the 32-key loading budget, borrowed carriers and attract-mode colors remain lazy. Session source-pixel caches also trade memory for fewer readbacks.
- The remaining session-level `SpriteModelSources` and `LevelLabels` weak tables can retain wrappers on Boehm Mono. Their long-session memory behavior still needs player measurement; this sync fixes chart-reader/note retention without redesigning those persistent caches.
- Removing in-play layer collision scans retains Claude's assumption that other mods do not write the reserved unnamed layers during render callbacks. A different mod set requires rechecking that assumption.
- Indexed visual entries and decoded controls are immutable during playback. An editor must invalidate/redecode after replacing entries in place; changed list counts are handled by fallback/rebuild paths.

No game process or cabinet was launched, and the game installations were not modified. Remaining integration checks are host load order under the game's Mono, both monitor layouts, practice rewind/reload, overlapping styled noise (including short pulses and animated edges), motion plus trail, modern/legacy kaleidoscope, subtitle fallback glyphs, and repeated tracks with memory/VRAM observation. The existing `tests/*/GpuProbe.csproj` probes require a configured Unity/game environment; their old stored results are not evidence for this merge.
