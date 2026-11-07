# Changelog

## Unreleased

## TVBox PC 0.8.7 - 2026-11-07

- Consolidate danmaku into adjacent bottom toggle/source controls with live appearance and top-quarter area settings; center readable notices and retain load failures on a yellow warning indicator.
- Add local site enable/group/add/remove controls and work-season advertisement marks with clean/unknown-first automatic fallback and aggregate statistics.
- Preserve per-episode viewing state and progress, add Continue Watching removal and drag scrolling, poster image fallbacks, full title copy, and company/network catalog navigation.
- Add animated sun/moon theme switching, hot-title and ten-entry search history, dismissible suggestions, and return-to-top/previous-position controls.
- Increase VOD buffering and record stalls; distinguish missing titles from expired media URLs. Add bounded experimental Nuvio adapters, default off because live decoder acceptance is incomplete.
- Preserve original 32 site keys and all personal data. Full verification and unimplemented cloud integrations are listed in the independent report.

## TVBox PC 0.8.6 - 2026-11-07

- Add a public stable-release updater with streamed SHA-256 verification, clean extraction, normal exit, file backup and rollback; preserve independent user profiles.
- Share season/episode parsing across playback, resume, health cache and danmaku; use ten site workers including probes, avoid caching transient errors and expand failed positive caches.
- Recheck source preferences in the background without replacing the playing session; freeze position before explicit source selection. Normalize discontinuous VOD timestamps across injected-ad segments without overriding user seeks.
- Fall back from unavailable Douban keyword search to optional TMDB without substituting rating filters; support encrypted optional danmaku endpoints and official signed API requests.
- Correct Bilibili original release date versus platform publication date; real Shawshank comments obtained. Full evidence and remaining integrations are in the independent report.

## TVBox PC 0.8.5 - 2026-11-07

- Search Chinese season ordinals and bounded base-title variants while retaining strict title/year/season validation; preserve positive legacy source health and bypass obsolete negative spellings.
- Prioritize foreground artwork without increasing concurrency; retry an initial timed-out banner instead of leaving it blank indefinitely.
- Stop the loading overlay on terminal playback/discovery failures and use company names when production logos are absent.
- Complete unlocked native UI acceptance, a real movie smoke test, and a season-card-to-episode-2 real playback test. Pass 323 offline assertions; retain unverified limits in the independent report.

## TVBox PC 0.8.4 Preview - 2026-11-07

- Restore a compact horizontal discovery banner and finite countdown; prefetch the next slide with cancellation and a bounded preparation deadline.
- Persist preferred artwork decisions across launches, retain existing disk image paths, reject invalid image responses and refresh recently read cache entries.
- Replace detail tabs with a continuous backdrop, seasonal episode cards, production/network logos, collections and a centered sticky title; add lightweight title depth and pointer perspective.
- Prefer same media type and animation in paged recommendations, and released titles in default broad searches while preserving explicit user sorting.
- Show episode pictures and saved progress in Continue Watching, isolate season progress and display version/date above Settings.
- Validate 312 offline assertions and three live metadata assertions. Native visual/input and playback acceptance remain pending Windows unlock; release as a preview without changing Latest.

## TVBox PC 0.5.0 - 2026-11-06

- Keep one independent playback page alive across Home, catalog and Settings; add a persistent Now Playing navigation entry.
- Resume the last successful provider first; require actual decoded frames and the saved offset before committing progress, then fall back to the selected speed/quality order.
- Use accurate initial resume with a bounded HLS discontinuity budget, then restore normal read timeouts after verified playback; a jumped clock or stalled seek is not playback evidence.
- Replace duplicate top source selection with a refresh icon, changed-preference warning badge and framed favorite star; keep manual source selection below.
- Add official Douban/Netflix marks, light/dark discovery themes and a short system-animation-aware transition.
- Keep Home filters on the current channel; add year/date/score/name sorting in both directions and explanatory hover hints for scope and controls.
- Close active hover hints after page navigation so hidden-player tooltips do not remain over Home.
- Put independent Douban/TMDB/IMDb values in a poster-bottom strip; hydrate loaded poster metadata in the background with optional saved TMDB credentials.
- Avoid rebuilding active checkbox input controls inside their pointer callbacks, preserve poster objects during updates, and clean up title-bar input before native window teardown.
- Upgrade Windows App SDK to 2.6.5 and keep WinUI modules, PRI and MUI resources together in the portable libs directory.

## TVBox PC 0.4.0 - 2026-11-06

- Add movie, television, animation, documentary and variety catalogue navigation with shared filters, scoped/global keyword search, progressive paging and honest result scope.
- Persist catalogue results, filters, positions and presets in an additive SQLite database; retain authoritative JSON favorites/history and provider identities.
- Add separate metadata details, provider-specific ratings/votes/fetch times, validated external links, season-aware matching and manual TMDB association/unlinking.
- Accept an optional personal TMDB Key/Read Token in Settings, encrypted for the current Windows user; no-key startup and ordinary playback remain usable, with one-time dismissible hints.
- Keep original direct playback and all 32 source keys; add alias matching with year/type/season checks and in-player metadata access.
- Reserve an isolated HD adapter interface and display an explicit deferred message; Xiaoya/G-Box integration is moved to the next update.
- Synchronize product/assembly/file versions to 0.4.0, include SQLite licenses and TMDB attribution, and exclude metadata databases/credentials from packages.

## TVBox PC 0.3.0 - 2026-11-06

- Click the video to show/hide the entire playback UI; keep double-click fullscreen and automatic hiding.
- Keep the episode picker immediately beside Next, including ordinary window widths.
- Add persisted speed/quality source preference, bounded initial quality comparison, HLS rendition selection and measured media-probe duration with color thresholds. Missing titles show NA; transport/decoder failures remain errors.
- Add decoder-derived media summary and a details dialog; label stream-size estimates and unavailable metadata explicitly.
- Add per-poster resume actions with season/episode/time, and refresh progress when returning from playback.
- Associate unambiguous same-title/year/kind resource/search histories with catalogue resume without mutating legacy records or merging different known subject IDs.
- Split Douban/Netflix catalogue views into independent categories with process-lifetime waterfall and scroll state; downward scrolling loads another page without requiring the More button. Netflix uses the Douban Netflix tag catalogue and Douban ratings, explicitly labelled; its high-score list does not claim an official Top250.
- Parse Top250 collection posters and reject playlist recommendations as playable films. Explain current XML/JSON danmaku support and the absence of automatic website matching.

## TVBox PC 0.2.0 - 2026-11-05

- Replaced the provider catalogue home with independent Douban recent-hot television and movie feeds, real posters/ratings, pagination, refresh and last-good cache warnings.
- Moved provider-specific browsing to the Resources page and hid the live-TV entry while focusing on VOD.
- Expanded bundled HTTP/API providers from 3 to the reference project's 32-entry public list, preserving existing provider keys and subscription identity.
- Added progressive cross-provider discovery, strict title/year/season matching, exclusion of commentary/trailers, serialized media-prefix probing, immediate autoplay and background alternate-source discovery.
- Added player source status, manual cross-provider switching, retry, favorites and bounded automatic fallback after actual decoder/start failures; source switches preserve the same episode and progress.
- Persisted Douban subject identity separately from the last successful provider; preserved legacy provider favorites/history and prevented another episode from inheriting the previous episode position.
- Required fresh rendered-frame evidence per open generation so old cumulative renderer counters cannot validate a new source; unsuccessful opens cannot overwrite good progress.
- Added Douban poster Referer headers and regression checks for feed parsing, matching, media validation, concurrency, cancellation and resume behavior.

## TVBox PC 0.1.0 - 2026-11-05

- Added three bundled MacCMS sources with a stable local configuration identity; removed mandatory first-run source entry.
- Added latest-updates browsing, continue-watching entry, source connection status, retry and bounded startup source fallback.
- Limited search to three parallel visible sources and exposed per-source result/error status.
- Isolated preferences, favorites and viewing history from upstream in the TVBox PC profile; added test-profile isolation.
- Enabled normal TLS certificate validation for application HTTP requests.
- Fixed loaded-but-invisible external text subtitles by presenting Flyleaf's decoded cues in a dedicated WinUI overlay.
- Fixed the fast local-configuration startup race that could leave the home page uninitialized on restart.
- Added regression checks for bundled assets, startup selection, favorites, fresh-process progress restoration and damaged JSON recovery.
- Added a build entry point and retained upstream licenses and attribution. Cloud/AI/RTX-specific enhancements remain scheduled for later versions.

## 1.1 - 2026-09-22

### Added

- Added a persistent close behavior setting for exiting the app or minimizing it to the system tray.

### Changed

- Restored the search display mode labels to “站点切换” and “分组纵览”.
- Updated application and release metadata to version 1.1.
- MSI upgrades now allow lower or identical versions to overwrite the installed version.

## 1.0.10 - 2026-08-01

### Fixed

- Restore the existing custom installation directory during upgrades and reinstalls, including migration from previously released MSI packages.
- Combine desktop-shortcut and post-install launch choices on the final installation-options page, and reduce installer extraction overhead.

## 1.0.9 - 2026-08-01

### Fixed

- Close the saved video-source and live-source history flyouts as soon as a configuration load starts, preventing the popup from remaining over the destination page.

## 1.0.8 - 2026-08-01

### Fixed

- Recover the local CatPawOpen service when its Node process exits and prevent live-source probing from replacing the active video-source runtime.
- Preserve video detail state when returning from playback instead of reloading the page.
- Improve startup and buffering transfer-rate reporting, including the first downloaded bytes, and keep loading surfaces opaque.
- Let Flyleaf coalesce repeated seeks, pin video and time-shift progress to the newest target, and prevent stale callbacks from moving progress backward.
- Enable the frame statistics used by hardware-decoder recovery so normal playback is not falsely reopened after five seconds.

## 1.0.7 - 2026-08-01

### Added

- Added a shared loading indicator and real-time transfer speed display for video and live playback buffering.

### Fixed

- Restored the text-input cursor and client input behavior for the search box inside the custom title bar.
- Restored saved video and live sources from validated local snapshots before background network refresh, reducing startup wait time.
- Reused validated CatPaw scripts during startup and shortened Node runtime readiness polling without changing explicit reload behavior.

## 1.0.6 - 2026-08-01

### Fixed

- Replaced the fixed playback-window transition delay with native window and WinUI layout stability detection, reducing jitter and flicker when entering or leaving full-screen and picture-in-picture modes.
- Synchronized the final Flyleaf swap-chain size after every presentation change and ignored repeated mode commands while a transition is still settling.

## 1.0.5 - 2026-07-31

### Fixed

- Removed the experimental frozen-window transition that could duplicate the video, expose the desktop, or time out while entering picture-in-picture from a maximized window; playback presentation is back on the stable native presenter and placement path.
- Replaced recycled-card `ContextFlyout` instances on favorites and history pages with page-owned context menus, avoiding a repeatable access violation in `Microsoft.UI.Input.dll` when right-clicking a poster.

## 1.0.4 - 2026-07-31

### Added

- Added a live-player channel picker that reuses the video episode selector icon and right-side overlay interaction.

### Fixed

- Kept live channel selection and channel navigation available while a stream is resolving; choosing another channel now cancels the stale load and starts the latest request.
- Fixed video and live bottom-bar flyout controls freezing or crashing the app when opening expandable menus.
- Hid the video-player back button in picture-in-picture mode while preserving keyboard and bottom-bar exit controls.
- Applied the shared 8-pixel player corner radius in normal and picture-in-picture modes while retaining square, edge-to-edge full-screen playback.
- Added even spacing around the live player in its normal page layout without exposing the underlying surface during picture-in-picture resizing.
- Covered the uncropped Flyleaf swap-chain corner pixels with theme-matched inverse-corner masks in the normal video and live layouts.
- Stabilized the original maximized restore rectangle after leaving picture-in-picture so the system Restore command no longer reuses compact-window bounds.
- Prevented navigation rail glyphs from losing edge pixels after full-screen or picture-in-picture transitions by removing nested fixed-size clipping and stabilizing the restored shell layout.
- Replaced navigation rail font glyphs with inset vector paths and changed shell restoration from repeated 50 ms layout forcing to one debounced final layout pass.
- Isolated picture-in-picture presenter properties, preserved and guarded the shared native restore bounds, and applied one transition to video and live full-screen/picture-in-picture changes.
- Prevented video and live line switching from rebuilding an active WinUI flyout during its click event, eliminating the native input crash.
- Serialized media source transitions and rejected stale resolver, decoder, recovery, and playback callbacks from previous lines.
- Unified search, detail, and player back buttons with one shared icon, size, padding, and spacing specification.
- Removed the extra focus outline around episode cards on the video detail page.
- Removed the nested live-page surface and duplicate title, aligning all primary pages on the shared page padding and title styles.

### Documentation

- Added the GPL-3.0 project license and a README interface screenshot.
- Included the project license in future portable and MSI release layouts.

## 1.0.3 - 2026-07-29

### Fixed

- Made the persisted collapsed navigation mode authoritative at startup and after video or live full-screen transitions.
- Restored maximized windows directly without briefly showing their previous normal bounds.
- Fixed the apphost runtime lookup path that incorrectly resolved `runtime` below the managed entry directory and kept reporting a missing .NET Desktop Runtime even after users installed it.
- Fixed the silent WinUI startup crash by redirecting the embedded registration-free activation manifest to the structured runtime directories before `Application.Start`.
- Replaced the flat self-contained output with a framework-dependent application in `libs` plus a standard private dotnet-root in `runtime`, so the multi-file release starts without a system .NET installation.
- Reorganized Node and FFmpeg as root runtime components, grouped icons and JavaScript under `assets`, and added explicit `libs`, `locales`, and `runtime` classifications.
- Consolidated WinUI language resources under `locales\winui`, retained only Chinese, English, Japanese, and Korean resources, and removed duplicate MUI payload from `libs`.
- Rebuilt the original blue cube artwork on a polished Apple-style rounded-rectangle application icon without changing the cube itself.
- Ensured the portable and installed layouts contain exactly one root `TVBox.exe`; single-file publishing remains disabled.
- Added an installer page for the optional desktop shortcut while retaining the Start menu shortcut and selectable install directory.
- Synchronized executable, assembly, manifest, package, and local status-page versions.
- Hardened portable and MSI validation against preferences, sources, history, favorites, logs, dumps, debug symbols, credentials, and other machine-local files.
- Kept release artifacts stateless while preserving user configuration and history under `%LOCALAPPDATA%\TVBox for Windows` across upgrades.
- Made VOD/live sources empty and every user-facing switch off for a clean first run.
- Made settings, source records, history, and favorites crash-safe with serialized atomic writes and backup recovery.
- Automatically reloads the active CatPawOpen source after its external configuration center saves a stable configuration change.

## 1.0.2 - 2026-07-29

### Fixed

- Added a blocking first-run source setup with required VOD and optional live configuration.
- Reorganized portable and installed files around a visible root launcher and an `app` runtime directory.
- Upgraded to matching Flyleaf FFmpeg bindings and LGPL FFmpeg 8.1 libraries so bundled playback libraries load correctly.
- Added a stable taskbar identity, explicit shortcut icon, and a selectable MSI installation directory.

## 1.0.1 - 2026-07-29

### Fixed

- Moved runtime data to `%LOCALAPPDATA%\TVBox for Windows` and completed the internal namespace rename.
- Removed the MSI launch condition that incorrectly rejected supported Windows 11 systems.
- Published distinct `1.0.1` package names so cached `1.0.0` installers cannot be mistaken for the corrected build.

## 1.0.0 - 2026-07-29

### Added

- Native WinUI 3 desktop shell with retained state for each navigation section.
- TVBox/CatVod JSON, CatPawOpen Node subscriptions, Jint JavaScript spiders, and live playlists.
- Flyleaf/FFmpeg playback with subtitles, danmaku, playback speed, aspect modes, full screen, and resizable picture-in-picture.
- Cross-site search with site tabs or collapsible grouped results.
- Clean portable ZIP and WiX MSI release pipelines with user-data and secret scanning.

### Fixed

- Preserved maximized window geometry and sidebar state across full-screen and picture-in-picture transitions.
- Removed compact navigation overlay and full-screen frame flashes.
- Kept grouped search headers full width when collapsed.
- Made configuration reload transactional and Node runtime replacement non-destructive.
- Prevented stale live-load results and canceled WebView sniff sessions from overwriting or leaking resources.
- Paused hidden playback timers and refreshed favorites/history only when their data changes.
- Restricted the local server to loopback by default, bounded uploads, and confined file endpoints to app data.
- Replaced the GPL FFmpeg bundle with a verified LGPL-3.0-or-later shared build and bundled provenance files.
- Corrected Windows Installer version detection so Windows 10 and 11 are not rejected as unsupported.

### Packaging

- First supported architecture: Windows x64.
- Minimum operating system: Windows 10 version 1809 (build 17763).
- Release binaries are currently unsigned.
