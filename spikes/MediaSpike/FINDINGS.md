# MediaElement effects + speech spike — findings (2026-11-12)

Throwaway app proving out the riskiest parts of the MediaElement filters / audio analysis / transcription plan.
Not in the solution; isolated by `spikes/Directory.*`. Test clip: `Resources/Raw/speech.mp4` (10.86s, 720p
H.264 + AAC, `say`-generated speech over `testsrc2`, so the transcript is known exactly).

Run: `Native/build.sh` (Swift bridge xcframework + CRT metallibs), then build `-f net10.0-ios -r iossimulator-arm64`
or `-f net10.0-android`. Autorun modes: `SIMCTL_CHILD_SPIKE_MODE=<mode>` (iOS) / `am start ... --es mode <mode>`
(Android): `effects:<name>`, `effects:cycle-stable`, `effects:cycle-reassign`, `effects:cycle-preset` (Android),
`pip:<name>`, `file`, `live`, `tap`. macOS speech harness: `Native/harness/main.swift`.

## Verdict

| Question | Result |
|---|---|
| Apple: CRT via CIKernel in an `AVVideoComposition` | ✅ Precompiled Metal CIKernel loads from a bundled metallib; 30 fps at 720p in the simulator |
| Apple: does PiP keep the effect? | ✅ Yes — the composition belongs to the item, so the PiP window shows the CRT look (iPad sim) |
| Android: GLSL CRT via Media3 `setVideoEffects` | ✅ Custom `GlEffect` + `BaseGlShaderProgram`; real time at 720p (frame 301 at pts 10.00) |
| Apple: `MTAudioProcessingTap` on the player | ✅ 44.1k mono float32 non-interleaved, ~3.7k-frame buffers; coexists with the composition |
| Android: `TeeAudioProcessor` audio tap | ✅ PCM16 interleaved, 1024-frame buffers, via a `DefaultRenderersFactory.BuildAudioSink` override |
| SpeechTranscriber from .NET | ✅ via a small Swift xcframework with a C ABI (`@_cdecl`) + `[DllImport]`/function pointers |
| Ahead-of-time transcription quality/speed | ✅ Word-perfect on the test clip, per-word timings, **~0.16s for 10.86s (~65× real time)** (macOS 27) |
| Live transcription fed from a tap | ✅ All sentences, finals ~1.3–2.5s after each sentence ends (macOS 27, real-time push) |
| Transcription on the iOS simulator | ❌ No on-device model at all (SpeechTranscriber unavailable, DictationTranscriber has no format) — device only |

## Findings that change the design

1. **Effects pipelines must exist before playback, on both platforms.**
   - Apple: assigning a `VideoComposition` to a *playing* item for the first time (null → composition) breaks:
     `CurrentTime` jumps to 0 and the layer keeps showing **unfiltered** frames. Composition → composition swaps
     are fine. Fix (verified): attach one composition when the item is created and have its handler read the
     current effect chain per frame (passthrough when empty). Switching is then free — no reassign, no stall.
   - Android: `setVideoEffects` on a player prepared *without* effects returns instantly, doesn't disturb the
     playhead, but **never shows** — the frame-processing pipeline is only built at prepare. Fix (verified):
     install a passthrough effect (`Contrast(0)`) before `Prepare()` and keep at least one effect in the list;
     then None/Mono/CRT switch live with no playhead disruption.
   - ⇒ MediaElement needs an "effects enabled" decision **at open**: an `EnableEffects` property (or "Effects is
     non-empty at open"). Turning it on later = reopen at the current position. Cost of the always-on
     passthrough (Apple CI per frame, Android GL pass) should be measured on device before making it default.
2. **Custom shaders are required for a real CRT.** The built-in-CIFilter CRT (bump distortion + stripes + bloom +
   vignette) reads as a bulge, not a tube. The Metal kernel and the GLSL shader are the same maths, so one
   shader spec per effect, written twice (MSL for CI, GLSL ES for Media3) plus a WebGL copy for Blazor.
3. **Metallibs are per SDK.** `metal -fcikernel` / `metallib -cikernel` must be run for `iphoneos` and
   `iphonesimulator` (and maccatalyst/macos) and the right one bundled per RID — the package needs a
   buildTransitive target for that. Compiling the kernels needs the Metal toolchain on the build machine.
4. **SpeechAnalyzer is Swift-only** — not in the .NET bindings. A Swift bridge works fine (C ABI, function-pointer
   callbacks, `GCHandle` context), so Shiny.Speech will ship an xcframework. Two things to fix for real:
   - Swift 6 strict concurrency rejects capturing the C callbacks; the spike builds in Swift 5 mode. Wrap them.
   - Build it per slice (ios, ios-sim, maccatalyst, macos) with `@available` guards; it returns -1 below 26.
5. **Always convert audio to `SpeechAnalyzer.bestAvailableAudioFormat` yourself.** `analyzeSequence(from: file)`
   happens to accept 44.1k float with SpeechTranscriber but DictationTranscriber rejects it ("Audio format is not
   supported"). The bridge now decodes with `AVAudioFile` (which reads the audio track straight out of an .mp4 —
   no separate extraction step on Apple) → `AVAudioConverter` → the same input stream as live mode. One code path.
   - The preferred format was 16 kHz mono Int16.
   - File mode currently yields into an unbounded `AsyncStream`; for long videos it needs back-pressure.
6. **Closing the input stream does not finish a live session.** `analyzer.start(inputSequence:)` returns at once;
   the last utterance only arrives after `finalizeAndFinishThroughEndOfInput()`. Easy to get wrong — fixed in the bridge.
7. **Tap timestamps aren't usable as-is.** The Apple tap's `GetSourceAudio` time range came back NaN; the Android
   tee sink has no timestamps at all. Position must be derived: frames counted since the last flush/seek,
   anchored to the player position at that seek. `IAudioAnalyzer.OnSeek` is required, not optional.
8. **Default-method rule hit immediately.** The CRT `GlEffect` needed `isNoOp` *and* `Effect.getDurationAfterEffectApplied`
   (inherited default) — the generated wrapper check in CLAUDE.md caught the second one. `TeeAudioProcessor.AudioBufferSink`
   has no defaults.
9. **Fallback chain on Apple:** SpeechTranscriber (`isAvailable`) → DictationTranscriber → (cloud / Whisper provider in
   Shiny.Speech). Treat "no model on this device" as a normal, reportable state, not a hang (the bridge now errors).

## Not covered (next)

- Physical iPhone: transcription on device, CI passthrough cost, PiP on iPhone (simulator iPhone reports PiP unsupported).
- Android speech (`SpeechRecognizer` + `EXTRA_AUDIO_SOURCE`, API 33+) and Android PiP with effects.
- Windows frame-server effects, Blazor WebGL/AudioWorklet, Mac Catalyst / macOS AppKit backends.
- Frame analysis tap (`AVPlayerItemVideoOutput` / Media3 frame access) for `IFrameAnalyzer` on video.

## Environment notes

- iOS 27 terminates apps that haven't adopted UIScene before managed code runs — the spike needed a `SceneDelegate`
  (the Sample already has one).
- With SDK 11.0.100 + Xcode 27.2, `net10.0-ios` resolves the 26.0 SDK pack, which demands Xcode 26.0; the spike sets
  `ValidateXcodeVersion=false`. (`net10.0-ios27.2` is not a valid TPV in this workload.)
- Android: added SDK platform `android-36` and AVD `spike_api37`.
