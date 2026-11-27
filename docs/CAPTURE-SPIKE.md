# Capture spike: one frame of the game window per key press (#4)

Stash and Aldur labels (#14, #15) and wealth snapshots (#17) will read the stash from the screen, because Path of Exile 2 has no stash API. GGG's policy allows that only "once per manual key press" and only while "the user is aware of what you are doing with that data" ([Sources](SOURCES.md#ggg-third-party-policy-checked-27-november-2026)). This spike checks that a backend can take exactly one frame of the game window per physical key-down, and records which one to use.

The spike itself changes nothing in the app. Its Windows.Graphics.Capture backend now runs in the app behind **Collect stash pictures** in Settings (off by default), which saves the pictures #3 needs: see [Bringing WGC into the app](#bringing-wgc-into-the-app) and [What #3 needs](#what-3-needs). [Capture and privacy](CAPTURE-AND-PRIVACY.md) describes everything the shipped app does.

**Result:** Windows.Graphics.Capture (WGC), one session per press. It took one frame per press on every test window, in 9 to 14 ms (p50) and never more than 17.2 ms. Both fallbacks fail the rule and stay disabled. Nothing has been checked in the real game yet; [What to check in the game](#what-to-check-in-the-game) lists the checks.

## What was built

| Piece | Where | What it does |
|---|---|---|
| `Frame`, `IFrameSource` | `src/StillSaneExile.Domain/Capture/Frame.cs` | One image of a window's client area, held in memory only. `Dispose` clears the pixels. `IFrameSource.CaptureOnce(hwnd)` takes exactly one frame or returns null. |
| `FrameReader` | `src/StillSaneExile.Domain/Capture/FrameReader.cs` | Sits behind `CaptureGate`. A `GateAction.Read` decision allows one `CaptureOnce`. The analysis runs off the hook thread, the frame is discarded right after, and the result comes back only if its generation is still current. A decision can be read once; a second read throws. |
| `FrameStats` | `src/StillSaneExile.Domain/Capture/FrameStats.cs` | The spike's stand-in for analysis: checksum, average colour, share of black pixels, HDR peak. |
| `CaptureSpike` | `tools/CaptureSpike` | A developer console tool, built by CI but not packed: the three backends, test windows, `info`, `bench`, `hold` and `live`. Targets `net10.0-windows10.0.22621.0` and runs on Windows 10 2004 (19041) or later. |
| Tests | `tests/StillSaneExile.Domain.Tests/FrameReaderTests.cs`, `tools/CaptureSpike.Tests` | 12 domain tests with a fake source and the real gate (the in-app collector added 6 more, for `HoldMode.Shot` and `PictureCopy`). 5 real-capture tests that skip when there is no GPU, no desktop or no WGC (CI runners). Skipping is not failing. |

The desktop project still targets `net10.0-windows`: the app calls WGC through raw COM ([below](#bringing-wgc-into-the-app)), so the installer gains no WinRT projection.

Run the tool with `dotnet run --project tools/CaptureSpike -c Release -- <command>`:

- `info`: OS build, GPU, each monitor's HDR state and SDR white level, keyboard repeat rate.
- `bench [--runs 50] [--windows d3d,blt,wpf,borderless]`: every backend against windows this tool owns. `borderless` covers the primary monitor for about a minute.
- `hold [--seconds 10]`: a key held with this machine's auto-repeat, through the real gate, `FrameReader` and WGC.
- `live [--key RightAlt] [--dump <folder>]`: for the game. One frame of Path of Exile 2 per physical press. `--selftest-injected` sends 31 synthetic presses of an unused key code and expects no capture; it refuses to run with the game in front.

Nothing is written to disk unless `--dump` names a folder.

## Test machine

Windows 11 build 26200, NVIDIA GeForce RTX 5070, one AW2521HF monitor: 1920×1080 at 239 Hz, SDR only (no HDR support), 100% scaling. Keyboard repeat: 250 ms delay, then 30 repeats a second.

No game ran here. The test windows stand in for it:

| Window | Stands in for |
|---|---|
| D3D11 window, 960×540, flip-model swap chain | A windowed DX11/DX12 game |
| D3D11 window, 960×540, blt-model swap chain | An older windowed DX11 game |
| WPF window, 960×540 | SSE itself (Direct3D 9 into a DWM redirection surface) |
| D3D11 borderless popup, 1920×1080, flip-model, covering the monitor | PoE2's "Windowed Fullscreen" |

Every test window draws a 64×64 marker at (8, 8) whose colour encodes the frame number. Reading it back proves the crop is exactly the client area, and it gives the render time of the content. `Present(1)` did not wait for vsync on this machine (it ran at about 7,000 frames a second), so the D3D windows pace themselves to about 240 Hz.

## Backends and latency

Each scenario is 50 captures, 100 ms apart, after one warm-up call. "Create→frame" runs from asking for the capture to holding the frame. "Whole call" adds the copy from the GPU into memory and the teardown. "Content age" is how long before the press the captured image was drawn; a negative value means it was drawn after the press.

### Windows.Graphics.Capture (chosen)

The per-press sequence: `CreateForWindow`, a free-threaded frame pool with **one buffer**, `StartCapture`, and the first `FrameArrived`. The handler takes at most one frame (an interlocked flag) with `TryGetNextFrame`. As soon as the GPU copy of the client-area crop is queued, the frame, session and pool are disposed. The cursor is off (`IsCursorCaptureEnabled = false`). The D3D device is kept for the source's lifetime; creating it captures nothing.

| Window | Pool format | Frames OK | Create→frame p50 / p95 / max (ms) | Whole call p50 / p95 (ms) | Session open, p50 (ms) | Content age (ms), median (min..max) |
|---|---|---|---|---|---|---|
| D3D11 flip, windowed | B8G8R8A8UIntNormalized | 50/50 | 9.6 / 11.9 / 12.6 | 11.1 / 13.0 | 4.5 | −7.1 (−10.6..−3.0) |
| D3D11 flip, windowed | R16G16B16A16Float | 50/50 | 12.3 / 14.4 / 14.9 | 13.8 / 16.0 | 4.9 | −8.7 (−13.5..−3.5) |
| D3D11 blt, windowed | B8G8R8A8UIntNormalized | 50/50 | 11.2 / 14.8 / 15.0 | 12.4 / 15.8 | 4.9 | −4.2 (−12.7..1.4) |
| D3D11 blt, windowed | R16G16B16A16Float | 50/50 | 11.8 / 14.1 / 15.3 | 13.3 / 15.7 | 4.3 | −3.1 (−5.8..−0.8) |
| WPF | B8G8R8A8UIntNormalized | 50/50 | 9.3 / 13.1 / 15.5 | 11.1 / 13.9 | 4.2 | 1.0 (−6.4..5.3) |
| WPF | R16G16B16A16Float | 50/50 | 13.8 / 14.7 / 15.6 | 15.6 / 16.8 | 5.7 | −1.3 (−6.5..3.3) |
| D3D11 flip, borderless 1920×1080 | B8G8R8A8UIntNormalized | 50/50 | 12.1 / 14.8 / 17.2 | 13.2 / 16.8 | 4.4 | −9.2 (−13.5..−3.0) |
| D3D11 flip, borderless 1920×1080 | R16G16B16A16Float | 50/50 | 10.7 / 13.7 / 14.2 | 12.9 / 15.9 | 4.3 | −8.1 (−12.3..−3.0) |

What the numbers show:

- **Every frame was usable.** 400 of 400 had the right size, were not black, and had the marker exactly at (8, 8). For a window with a title bar, the crop (client origin minus DWM's extended frame bounds) removed the title bar and borders.
- **The first frame is current.** Its content was drawn at most 5.3 ms before the press, and usually after it: WGC composes a fresh frame for a new session rather than handing over a cached one.
- **It doesn't need the game's cooperation.** With the window's thread blocked for a second (like a loading screen), WGC still returned in 9 to 16 ms, with the last image drawn before the block. The only messages the captured window handled during WGC runs were mouse moves from someone using the machine, never anything per capture.
- **The first call is slow.** The first call of each new source took 27 to 72 ms: it creates the D3D device, and the first in a process also activates WinRT. In the 10-second hold, the cold press took 79 ms in all, 55 ms of it create→frame. The app should create the device and activation factory ahead of time; neither captures anything.
- **The yellow border stays on.** Turning it off (`IsBorderRequired = false`) needs `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`, which returned "Allowed" for this unpackaged exe. But it made every capture slower: create→frame p50 17.7 to 18.0 ms and p95 20.2 to 20.3 ms, against 10.2 and 12.4 ms with the border, run back to back. The border also tells the player that something is reading the window. The session is open for about 5 ms, so whether the border is even visible needs a look in the game.
- **16-bit frames work on SDR.** They read back the same as 8-bit ones once reduced to sRGB (markers 50/50). On the windowed D3D window they cost about 2.7 ms more at p50, and twice the memory: 16.6 MB against 8.3 MB at 1080p.

### Fallback 1: PrintWindow (`PW_CLIENTONLY | PW_RENDERFULLCONTENT`), rejected

On this machine it returned correct frames of all four windows, flip-model and borderless included. Create→frame p50 was 4.7 to 10.5 ms and p95 was 5.7 to 12.7 ms. It is rejected because it works through the game's own window thread:

- On every call, the captured window's thread handled `WM_PAINT`, `WM_ERASEBKGND` and `WM_NCPAINT` (0x000F, 0x0014, 0x0085: 51 of each for 51 calls). No `WM_PRINT` reached the window procedure. The documentation agrees: "The application that owns the window referenced by hWnd processes the PrintWindow call."
- With the window's thread blocked for a second, PrintWindow waited 952 to 956 ms, until the thread ran again.

Making the game repaint and waiting on its message loop is an interaction with the game process that the policy table in [Sources](SOURCES.md) keeps out. It would also stall the HUD during loading screens.

### Fallback 2: DXGI Desktop Duplication, cropped to the client area, rejected

Each call did `DuplicateOutput1`, one `AcquireNextFrame`, `ReleaseFrame`, then released the duplication.

- **One acquisition gives nothing.** On every window, 400 of 400 frames were black (8 scenarios). The first frame of a new duplication has `AccumulatedFrames 0` and `LastPresentTime 0`, which means no desktop image (create→frame 0.3 to 0.4 ms).
- **It takes two acquisitions to get pixels.** A measurement-only variant called `AcquireNextFrame` again until a frame carried an image. That took 2 acquisitions per press and gave correct frames in 3.1 to 4.4 ms (p50) and 5.3 to 6.7 ms (p95). Two acquisitions per press break the rule.
- **It copies the whole monitor.** Anything on top of the game lands in the frame: SSE's own HUD, notifications, other windows.

In two of the WPF runs the window log shows that someone moved the window during the run (`WM_MOVING`), with 5 to 6 black frames and up to 5 acquisitions. Those runs say nothing about the backend.

## One press, one frame: the evidence

1. **Design.** A `GateAction.Read` allows one `FrameReader.ReadAsync`, which calls `CaptureOnce` once; reading the same decision again throws. The WGC pool has one buffer, and while the frame is held, nothing else can arrive. The handler takes at most one frame, and the session and pool are closed about 5 ms after they open.
2. **Unit test, fake source, real gate** (`FrameReaderTests.Ten_second_hold_with_autorepeat_takes_exactly_one_frame`). One press plus 300 auto-repeat key-downs, arriving while the frame is still being taken, give 1 `CaptureOnce`, 300 ignored repeats, the frame discarded, and 0 frames left in memory. The other tests cover:
   - release, focus loss and a newer press while capturing: the result is dropped as late, and the frame is still discarded;
   - synthetic input and another window in front: no capture;
   - a failing analysis: the frame is still discarded.
3. **The real path for 10 seconds** (`CaptureSpike hold`, real gate, `FrameReader`, WGC). 295 key-downs over 10.3 s (1 press and 294 repeats at 30 a second), then 1 release, gave:
   - Gate: reads 1, auto-repeat ignored 294.
   - Reader: captures 1, frames 1, discarded 1, in memory 0.
   - WGC: OS acquisitions 1, sessions opened 1 and closed 1, `FrameArrived` 1, extra 0, after close 0.
4. **Real-capture test** (`RealCaptureTests.Held_key_with_autorepeat_takes_one_wgc_frame_through_the_real_gate`). The same check with 300 repeats, run by `dotnet test` wherever a GPU is present.
5. **The bench.** In every WGC scenario, 52 calls gave 52 OS acquisitions, 52 sessions opened and closed, 52 `FrameArrived`, 0 extra and 0 after close: 416 calls in all.
6. **The real keyboard hook ignores synthetic input** (`CaptureSpike live --selftest-injected`). 31 `SendInput` key-downs and a release arrived flagged `LLKHF_INJECTED`. Result: synthetic ignored 31, reads 0, captures 0.

## HDR

**What could be tested:** only HDR off. This monitor has no HDR: the display configuration reports HDR unsupported, and DXGI reports an 8-bit sRGB (G22, BT.709) output.

- Both WGC pool formats work with HDR off. A `R16G16B16A16Float` frame holds linear scRGB with SDR white at 1.0. Reduced to 8-bit sRGB, it matched the `B8G8R8A8UIntNormalized` frame of the same window on every marker.
- `FrameStats` and the PNG dump treat both formats the same way: divide by the frame's SDR white, clamp, and encode as sRGB. A domain test (`Stats_give_the_same_colour_for_8_bit_and_hdr_frames`) checks this with SDR white at 2.5 (200 nits).

**What the code does:**

- `CaptureFormat.Auto` reads the HDR state of the game window's monitor. It uses `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2` (Windows 11 24H2 and later), whose active colour mode separates HDR from wide colour. Older Windows falls back to `GET_ADVANCED_COLOR_INFO`.
- With HDR on, it asks WGC for `R16G16B16A16Float`; with HDR off, for `B8G8R8A8UIntNormalized`.
- The monitor's SDR white level (`DISPLAYCONFIG_SDR_WHITE_LEVEL`) travels with the frame as `Frame.SdrWhite`. `FrameStats.PeakScRgb` shows highlights above it.
- The older "advanced colour" bit is not used for this choice: it reported "supported" for this SDR monitor because of colour management.

**What Microsoft's documentation says:**

- "On systems with Windows HD color enabled, the content pixel format might not necessarily be DXGI_FORMAT_B8G8R8A8_UNORM."
- "To avoid pixel overclipping (i.e. the captured content looks washed out) when capturing HDR content, consider using DXGI_FORMAT_R16G16B16A16_FLOAT".

So an 8-bit pool on an HDR desktop clips highlights, which is why the code switches to 16-bit.

**What could not be tested:**

- HDR on, at all.
- Whether Path of Exile 2's own HDR output draws its UI at Windows' SDR white or at an in-game brightness of its own. If it is the game's own brightness, dividing by Windows' SDR white gives the wrong level, and the recognizers will need a per-capture reference (a known UI colour), or a calibration per HDR state.
- Whether an SDR game on an HDR desktop behaves like the SDR case.

## What to check in the game

None of this has run against Path of Exile 2. Run `CaptureSpike live` (add `--dump <folder>` to keep the PNGs) and check the list below. The app's stash picture collector ([What #3 needs](#what-3-needs)) answers items 1 to 3 and 5 as a side effect: each picture's notes record its size, the window mode, HDR and the latency, and a black or missing frame is reported over the game.

1. **Windowed Fullscreen (borderless) at 1920×1080 and at 2560×1440.** Every press prints one `Accepted` line, the frame size equals the game's resolution, it is not `BLACK`, and create→frame stays near the numbers above while the game is rendering at full load.
2. **Exclusive "Fullscreen" mode.** Does WGC get a frame at all, or does it time out or come back black? If it fails, SSE should ask for Windowed Fullscreen and abstain.
3. **Windowed mode with a title bar.** The frame is the client area only.
4. **Each renderer** (DX11, DX12, Vulkan) in Windowed Fullscreen.
5. **HDR**, on an HDR monitor:
   - Windows HDR on and off, and the game's own HDR setting on and off.
   - `--format auto` picks 16-bit with HDR on.
   - The same stash tab looks the same in the SDR and HDR PNGs, neither washed out nor dark.
   - Note the printed peak scRGB.
6. **The cursor.** WGC leaves the Windows cursor out. A cursor the game draws itself would still be in the frame.
7. **The yellow capture border.** Is the flash visible on each press, and is it acceptable?
8. **Holding the key for 10 seconds**, then releasing:
   - exactly one `press N: Accepted` line and one `released` line;
   - the ignored auto-repeat count grows;
   - Alt+Tab while holding prints `invalidated`.

   If Right Alt changes what the game shows, use `--key F8`.
9. **Overlays.** Steam, Discord and GPU-vendor overlays that draw inside the game's own frame will be in the capture. Separate windows, such as SSE's HUD, are not.
10. **Display setups:** the game on a second monitor, mixed DPI (100, 125 and 150%), and G-Sync or FreeSync.
11. **A loading screen.** A press returns quickly with the last image. The HUD should show nothing then anyway.

## Bringing WGC into the app

The app takes pictures with WGC behind **Collect stash pictures** in Settings, off by default (`WgcFrameSource`, `StashCollector` and `StashPictureStore` in `src/StillSaneExile.Desktop`). It collects the pictures for #3 now, with the same key press stash reading will use.

- **Raw COM, no Windows 10 target.** The app keeps `net10.0-windows` and calls WGC through COM vtables (`Infrastructure/CaptureInterop.cs`): `RoGetActivationFactory` for the capture item and frame pool factories, `IGraphicsCaptureItemInterop.CreateForWindow`, and Direct3D 11 for the copy, as the spike did for D3D. A `net10.0-windows10.0.19041.0` target would have put the WinRT projection, `Microsoft.Windows.SDK.NET.dll` (24.9 MB, and WPF can't be trimmed), in the package for about ten calls. The slot numbers were checked against the projection's own ABI code, and the real-capture tests run them.
- **One acquisition per press, and none after giving up.** The spike took its frame in a `FrameArrived` handler and waited up to 500 ms for it. On a timeout, the handler could still take a frame after the wait had given up: that frame was never disposed, and the handler set an event that was already disposed. The app has no handler. The capturing thread alone calls `TryGetNextFrame`, every 0.5 ms, until one frame is there or 500 ms have passed, then closes frame, session and pool before reading the copy back. A frame is taken by that thread before the deadline, or not at all.
- **Its own gate.** `StashCollector` listens to the same key and foreground events as the HUD but has its own `CaptureGate`, in the new `HoldMode.Shot`, so the quick price path is untouched. Auto-repeat, synthetic input and another window in front take nothing. A frame that comes back after focus loss, lock, a display change or a newer press is dropped unsaved (`FrameReader` now also disposes a late result, which clears the picture's copy). Letting go doesn't drop it, so a quick tap keeps its picture.
- **The device ahead of time.** The D3D device and both factories are created on a worker thread when collecting is switched on (or at start, if it is on), and released when it is switched off. Creating them captures nothing.
- **As in the spike:** the yellow border stays on, the Windows cursor is left out, and an HDR monitor gets a 16-bit frame, reduced to 8-bit sRGB for the PNG.
- **Abstains** on no frame within 500 ms and on a black frame, and says so in the note over the game. The notes keep both the picture's size and the game window's client size, so a mismatch shows.
- **Tests.** `RealCaptureTests` (desktop tests) checks the crop to the pixel on a window with a title bar, and holds the stash key through `StashCollector` for 300 auto-repeats: 1 acquisition, 1 session opened and closed, 1 PNG. Both skip without a GPU or a desktop, as on CI.

## What #3 needs

The screenshots for the M2 recognizers. Take them with SSE itself, so each one is exactly the frame SSE will get: client area only, no Windows cursor, the same colour handling.

In SSE, open Settings → **Stash pictures** and tick **Collect stash pictures**. In the game, press Right Alt once per screenshot (under Keys, change the stash key to F8 if Alt changes what the game shows). Each press saves one PNG and its notes in the data folder, under `captures\<date>\`, and a note over the game says "Saved stash picture N". Afterwards, label them on the same Settings page, under **Label your pictures**.

1. **Tabs.** Take each of these at every resolution and UI scale in item 4:
   - **Dedicated currency tab:** one capture of every subtab, with real stacks in it. Include small (1 to 9) and large (3 to 4 digit) stack counts.
   - **Ordinary stash tab:** items of different sizes, including some stackables.
   - **Quad stash tab:** the same.
2. **Aldur Remnant panel:** the selectable rewards and the common bonus, all visible. At least two captures in which the reward order differs.
3. **Hard cases:**
   - an item tooltip lying over a stack's quantity;
   - a row that is only partly visible.
4. **Game settings:** Windowed Fullscreen (borderless) at 1920×1080 and at 2560×1440, each at two in-game UI scales (the default and one other). That is four combinations for every screen above. Windowed Fullscreen uses the desktop resolution, so 1440p needs Windows set to 2560×1440.
5. **Metadata for every capture.** The JSON file beside each PNG already holds the capture time, SSE's version, the picture's and the game window's size, the window mode, the monitor and its resolution, Windows display scaling, HDR on or off, the pixel format and the latency. On My wealth, add the tab type (and the currency subtab) and the in-game UI scale; put anything else in the note: game patch and language, renderer (DX11, DX12 or Vulkan), a hard case.
6. **HDR, if you have an HDR monitor:** the same currency subtab with Windows HDR off and with it on.
7. **A held-out set.** Tick **Held out** on about a third of the captures, taken in a different play session. They stay apart from the set used to tune thresholds; don't tune against them.
8. **Before sharing or committing anything:** crop or blur character names, chat and account names.

Later, scoring needs the truth for each capture: which item, and how many, sits in each visible slot. Notes taken while capturing make that easier.
