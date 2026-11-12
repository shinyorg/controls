// SPIKE: a C ABI over Apple's Swift-only SpeechAnalyzer / SpeechTranscriber (iOS 26+), so .NET can
// reach it with [DllImport]. Two entry points: transcribe a file ahead of time, and a live session that
// PCM is pushed into (from an MTAudioProcessingTap on the player).

import AVFoundation
import Foundation
import Speech

public typealias SsbSegmentCallback = @convention(c) (
    UnsafeMutableRawPointer?,   // ctx
    Double,                     // start seconds
    Double,                     // end seconds
    Int32,                      // isFinal
    UnsafePointer<CChar>,       // text (utf8)
    UnsafePointer<CChar>        // words json (utf8) [{"w":"..","s":0.1,"e":0.3}]
) -> Void

public typealias SsbDoneCallback = @convention(c) (
    UnsafeMutableRawPointer?,   // ctx
    UnsafePointer<CChar>?       // error, or null on success
) -> Void

struct Ctx: @unchecked Sendable { let raw: UnsafeMutableRawPointer? }

@_cdecl("ssb_is_available")
public func ssb_is_available() -> Int32 {
    if #available(iOS 26.0, macOS 26.0, *) {
        if SpeechTranscriber.isAvailable { return 1 }
        return 2 // DictationTranscriber is the broad-device fallback; locale support decided at start
    }
    return -1
}

@available(iOS 26.0, macOS 26.0, *)
func emit(_ text: AttributedString, _ range: CMTimeRange, _ isFinal: Bool, _ ctx: Ctx, _ onSegment: SsbSegmentCallback) {
    var words: [String] = []
    for run in text.runs {
        guard let r = run.audioTimeRange else { continue }
        let w = String(text[run.range].characters)
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "\"", with: "\\\"")
        words.append("{\"w\":\"\(w)\",\"s\":\(r.start.seconds),\"e\":\(r.end.seconds)}")
    }
    let plain = String(text.characters)
    let json = "[" + words.joined(separator: ",") + "]"
    plain.withCString { t in json.withCString { j in
        onSegment(ctx.raw, range.start.seconds, range.end.seconds, isFinal ? 1 : 0, t, j)
    } }
}

/// Picks SpeechTranscriber (long-form, new model) when the device supports it, else DictationTranscriber.
/// Returns the module plus a task draining its results into the callback.
@available(iOS 26.0, macOS 26.0, *)
func makeModule(_ localeId: String, _ ctx: Ctx, _ onSegment: SsbSegmentCallback) async throws -> (any SpeechModule, Task<Void, Error>, String) {
    let requested = Locale(identifier: localeId)
    if SpeechTranscriber.isAvailable, let locale = await SpeechTranscriber.supportedLocale(equivalentTo: requested) {
        let t = SpeechTranscriber(locale: locale, transcriptionOptions: [], reportingOptions: [.volatileResults], attributeOptions: [.audioTimeRange])
        if let req = try await AssetInventory.assetInstallationRequest(supporting: [t]) { try await req.downloadAndInstall() }
        let reader = Task { for try await r in t.results { emit(r.text, r.range, r.isFinal, ctx, onSegment) } }
        return (t, reader, "SpeechTranscriber")
    }
    if let locale = await DictationTranscriber.supportedLocale(equivalentTo: requested) {
        let t = DictationTranscriber(locale: locale, contentHints: [], transcriptionOptions: [.punctuation], reportingOptions: [.volatileResults], attributeOptions: [.audioTimeRange])
        if let req = try await AssetInventory.assetInstallationRequest(supporting: [t]) { try await req.downloadAndInstall() }
        let reader = Task { for try await r in t.results { emit(r.text, r.range, r.isFinal, ctx, onSegment) } }
        return (t, reader, "DictationTranscriber")
    }
    throw NSError(domain: "ssb", code: 1, userInfo: [NSLocalizedDescriptionKey:
        "locale \(localeId) not supported (SpeechTranscriber.isAvailable=\(SpeechTranscriber.isAvailable), dictation locales=\(await DictationTranscriber.supportedLocales.count))"])
}

public typealias SsbLogCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>) -> Void
nonisolated(unsafe) var ssbLog: SsbLogCallback?
func log(_ ctx: Ctx, _ msg: String) { if let l = ssbLog { msg.withCString { l(ctx.raw, $0) } } }

@_cdecl("ssb_set_log")
public func ssb_set_log(_ cb: SsbLogCallback?) { ssbLog = cb }

func finish(_ ctx: Ctx, _ onDone: SsbDoneCallback, _ error: Error?) {
    if let error {
        "\(error)".withCString { onDone(ctx.raw, $0) }
    } else {
        onDone(ctx.raw, nil)
    }
}

// MARK: ahead of time

@_cdecl("ssb_transcribe_file")
public func ssb_transcribe_file(
    _ path: UnsafePointer<CChar>,
    _ locale: UnsafePointer<CChar>,
    _ ctx: UnsafeMutableRawPointer?,
    _ onSegment: SsbSegmentCallback,
    _ onDone: SsbDoneCallback
) {
    let url = URL(fileURLWithPath: String(cString: path))
    let localeId = String(cString: locale)
    let c = Ctx(raw: ctx)

    guard #available(iOS 26.0, macOS 26.0, *) else {
        finish(c, onDone, NSError(domain: "ssb", code: 2, userInfo: [NSLocalizedDescriptionKey: "requires iOS 26"]))
        return
    }
    Task {
        do {
            let (module, reader, engine) = try await makeModule(localeId, c, onSegment)
            let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [module])
            log(c, "engine=\(engine) analyzer format=\(String(describing: format))")
            guard format != nil else {
                throw NSError(domain: "ssb", code: 3, userInfo: [NSLocalizedDescriptionKey: "\(engine) has no on-device model here (simulator?)"])
            }
            let session = LiveSession()
            session.analyzerFormat = format
            let analyzer = SpeechAnalyzer(modules: [module])
            try await analyzer.start(inputSequence: session.stream)

            // AVAudioFile reads the audio track straight out of an .mp4; decode + convert ourselves rather
            // than analyzeSequence(from:), which DictationTranscriber rejects at 44.1k float.
            let file = try AVAudioFile(forReading: url)
            log(c, "AVAudioFile opened mp4: format=\(file.processingFormat) frames=\(file.length)")
            let chunk = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: 16384)!
            while file.framePosition < file.length {
                try file.read(into: chunk)
                if chunk.frameLength == 0 { break }
                session.pushBuffer(chunk)
            }
            session.continuation.finish()
            try await analyzer.finalizeAndFinishThroughEndOfInput()
            try await reader.value
            finish(c, onDone, nil)
        } catch {
            finish(c, onDone, error)
        }
    }
}

// MARK: live

@available(iOS 26.0, macOS 26.0, *)
final class LiveSession: @unchecked Sendable {
    let continuation: AsyncStream<AnalyzerInput>.Continuation
    let stream: AsyncStream<AnalyzerInput>
    var analyzerFormat: AVAudioFormat?
    var converter: AVAudioConverter?
    var inputFormat: AVAudioFormat?
    var analyzer: SpeechAnalyzer?
    let lock = NSLock()

    init() {
        var cont: AsyncStream<AnalyzerInput>.Continuation!
        stream = AsyncStream { cont = $0 }
        continuation = cont
    }

    func push(_ samples: UnsafePointer<Float>, frames: Int, channels: Int, sampleRate: Double) {
        guard let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sampleRate,
                                      channels: AVAudioChannelCount(channels), interleaved: false),
              let inBuf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: AVAudioFrameCount(frames)) else { return }
        // samples arrive non-interleaved: channel 0 frames, then channel 1 frames
        inBuf.frameLength = AVAudioFrameCount(frames)
        for ch in 0..<channels {
            inBuf.floatChannelData![ch].update(from: samples + ch * frames, count: frames)
        }
        pushBuffer(inBuf)
    }

    /// Convert any PCM buffer to the analyzer's preferred format and queue it.
    func pushBuffer(_ inBuf: AVAudioPCMBuffer) {
        lock.lock(); defer { lock.unlock() }
        guard let target = analyzerFormat else { return }
        let inFmt = inBuf.format
        if inputFormat == nil || !inputFormat!.isEqual(inFmt) {
            inputFormat = inFmt
            converter = AVAudioConverter(from: inFmt, to: target)
        }
        guard let converter else { return }

        let ratio = target.sampleRate / inFmt.sampleRate
        let cap = AVAudioFrameCount(Double(inBuf.frameLength) * ratio + 64)
        guard let outBuf = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: cap) else { return }
        var fed = false
        var err: NSError?
        converter.convert(to: outBuf, error: &err) { _, status in
            if fed { status.pointee = .noDataNow; return nil }
            fed = true
            status.pointee = .haveData
            return inBuf
        }
        if err == nil && outBuf.frameLength > 0 {
            continuation.yield(AnalyzerInput(buffer: outBuf))
        }
    }
}

final class Box: @unchecked Sendable { var value: AnyObject? }

@_cdecl("ssb_live_start")
public func ssb_live_start(
    _ locale: UnsafePointer<CChar>,
    _ ctx: UnsafeMutableRawPointer?,
    _ onSegment: SsbSegmentCallback,
    _ onDone: SsbDoneCallback
) -> UnsafeMutableRawPointer? {
    guard #available(iOS 26.0, macOS 26.0, *) else { return nil }
    let localeId = String(cString: locale)
    let c = Ctx(raw: ctx)
    let session = LiveSession()

    Task {
        do {
            let (module, reader, engine) = try await makeModule(localeId, c, onSegment)
            let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [module])
            log(c, "engine=\(engine) analyzer format=\(String(describing: format))")
            guard format != nil else {
                throw NSError(domain: "ssb", code: 3, userInfo: [NSLocalizedDescriptionKey: "\(engine) has no on-device model here (simulator?)"])
            }
            session.lock.withLock { session.analyzerFormat = format }

            let analyzer = SpeechAnalyzer(modules: [module])
            session.lock.withLock { session.analyzer = analyzer }
            // start() returns immediately; results end once ssb_live_finish finalizes the analyzer
            try await analyzer.start(inputSequence: session.stream)
            try await reader.value
            finish(c, onDone, nil)
        } catch {
            finish(c, onDone, error)
        }
    }
    return Unmanaged.passRetained(session as AnyObject).toOpaque()
}

/// Returns 0 while the analyzer is still downloading assets / warming up (samples are dropped).
@_cdecl("ssb_live_push")
public func ssb_live_push(
    _ handle: UnsafeMutableRawPointer,
    _ samples: UnsafePointer<Float>,
    _ frames: Int32,
    _ channels: Int32,
    _ sampleRate: Double
) -> Int32 {
    guard #available(iOS 26.0, macOS 26.0, *) else { return 0 }
    let session = Unmanaged<AnyObject>.fromOpaque(handle).takeUnretainedValue() as! LiveSession
    if session.analyzerFormat == nil { return 0 }
    session.push(samples, frames: Int(frames), channels: Int(channels), sampleRate: sampleRate)
    return 1
}

@_cdecl("ssb_live_finish")
public func ssb_live_finish(_ handle: UnsafeMutableRawPointer) {
    guard #available(iOS 26.0, macOS 26.0, *) else { return }
    let session = Unmanaged<AnyObject>.fromOpaque(handle).takeRetainedValue() as! LiveSession
    session.continuation.finish()
    // closing the input stream is not enough — the last utterance only finalizes when asked to
    let analyzer = session.lock.withLock { session.analyzer }
    Task { try? await analyzer?.finalizeAndFinishThroughEndOfInput() }
}
