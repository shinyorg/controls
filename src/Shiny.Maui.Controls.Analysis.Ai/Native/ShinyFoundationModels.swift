// A C ABI over Apple's Swift-only Foundation Models framework (the on-device Apple Intelligence model), so
// FoundationModelsChatClient can reach it with [DllImport]. Built into ShinyFoundationModels.xcframework by
// build.sh; the C# side is Platforms/Apple/FoundationModelsChatClient.Apple.cs.
//
// One request is one JSON document (instructions, prior turns, the prompt, an optional JSON schema, options)
// plus the raw bytes of any images it references, so the managed side never has to model Swift types.
// Text generation needs iOS/macOS 26; image input and the Vision tools (OCRTool, BarcodeReaderTool) need 27.

import CoreGraphics
import Foundation
import FoundationModels
import ImageIO
#if canImport(_Vision_FoundationModels)
import _Vision_FoundationModels
#endif

public typealias SfmDeltaCallback = @convention(c) (
    UnsafeMutableRawPointer?,   // ctx
    UnsafePointer<CChar>        // the text generated so far (utf8) - a snapshot, not a delta
) -> Void

public typealias SfmDoneCallback = @convention(c) (
    UnsafeMutableRawPointer?,   // ctx
    UnsafePointer<CChar>?,      // result text / JSON (utf8), null on failure
    Int32,                      // error kind (SfmError), 0 on success
    UnsafePointer<CChar>?       // error message (utf8), null on success
) -> Void

// Mirrored by FoundationModelsErrorKind in C#.
enum SfmError: Int32 {
    case none = 0
    case unavailable = 1
    case guardrail = 2
    case contextSize = 3
    case unsupportedLanguage = 4
    case cancelled = 5
    case badRequest = 6
    case refusal = 7
    case rateLimited = 8
    case other = 9
}

struct Ctx: @unchecked Sendable { let raw: UnsafeMutableRawPointer? }

struct SfmTurn: Decodable {
    let role: String            // "user" | "assistant"
    let text: String
    let images: [Int]?          // indices into the request's image array
}

struct SfmRequest: Decodable {
    let instructions: String?
    let history: [SfmTurn]?
    let prompt: SfmTurn
    let schema: String?         // JSON schema already rewritten into Apple's dialect by the C# side
    let temperature: Double?
    let maxTokens: Int?
    let tools: [String]?        // "ocr", "barcode"
    let stream: Bool?
    let useCase: String?        // "general" | "contentTagging"
}

// 0 available, 1 device not eligible, 2 Apple Intelligence off, 3 model not ready (downloading),
// 4 OS too old, 5 unknown. Mirrored by FoundationModelsAvailability in C#.
@_cdecl("sfm_availability")
public func sfm_availability() -> Int32 {
    guard #available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *) else { return 4 }
    switch SystemLanguageModel.default.availability {
    case .available: return 0
    case .unavailable(.deviceNotEligible): return 1
    case .unavailable(.appleIntelligenceNotEnabled): return 2
    case .unavailable(.modelNotReady): return 3
    default: return 5
    }
}

// Loads the model ahead of the first request; the first call otherwise pays a cold start of many seconds.
@_cdecl("sfm_prewarm")
public func sfm_prewarm(_ instructions: UnsafePointer<CChar>?) {
    guard #available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *) else { return }
    let text = instructions.map { String(cString: $0) }
    LanguageModelSession(instructions: text).prewarm()
}

final class Inflight: @unchecked Sendable {
    static let shared = Inflight()
    private let lock = NSLock()
    private var next: Int64 = 1
    private var tasks: [Int64: Task<Void, Never>] = [:]

    func add(_ make: (Int64) -> Task<Void, Never>) -> Int64 {
        lock.lock(); defer { lock.unlock() }
        let id = next
        next += 1
        tasks[id] = make(id)
        return id
    }

    func remove(_ id: Int64) {
        lock.lock(); defer { lock.unlock() }
        tasks[id] = nil
    }

    func cancel(_ id: Int64) {
        lock.lock(); defer { lock.unlock() }
        tasks[id]?.cancel()
    }
}

// Starts a request and returns a handle for sfm_cancel. onDone is called exactly once, on a background thread.
// The image bytes are copied before this returns, so the caller may release them straight away.
@_cdecl("sfm_respond")
public func sfm_respond(
    _ requestJson: UnsafePointer<CChar>,
    _ images: UnsafePointer<UnsafePointer<UInt8>?>?,
    _ imageLengths: UnsafePointer<Int32>?,
    _ imageCount: Int32,
    _ ctx: UnsafeMutableRawPointer?,
    _ onDelta: SfmDeltaCallback?,
    _ onDone: SfmDoneCallback
) -> Int64 {
    let context = Ctx(raw: ctx)
    let json = String(cString: requestJson)
    var blobs: [Data] = []
    if let images, let imageLengths {
        for i in 0..<Int(imageCount) {
            if let p = images[i] {
                blobs.append(Data(bytes: p, count: Int(imageLengths[i])))
            } else {
                blobs.append(Data())
            }
        }
    }

    guard #available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *) else {
        finish(context, onDone, nil, .unavailable, "Apple Foundation Models needs iOS / macOS 26 or later")
        return 0
    }

    return Inflight.shared.add { id in
        Task.detached {
            defer { Inflight.shared.remove(id) }
            do {
                let request = try JSONDecoder().decode(SfmRequest.self, from: Data(json.utf8))
                let text = try await run(request, blobs, context, onDelta)
                finish(context, onDone, text, .none, nil)
            } catch is CancellationError {
                finish(context, onDone, nil, .cancelled, "The request was cancelled")
            } catch {
                let (kind, message) = classify(error)
                finish(context, onDone, nil, kind, message)
            }
        }
    }
}

@_cdecl("sfm_cancel")
public func sfm_cancel(_ handle: Int64) {
    Inflight.shared.cancel(handle)
}

func finish(_ ctx: Ctx, _ onDone: SfmDoneCallback, _ text: String?, _ kind: SfmError, _ message: String?) {
    switch (text, message) {
    case let (t?, _):
        t.withCString { onDone(ctx.raw, $0, kind.rawValue, nil) }
    case let (nil, m?):
        m.withCString { onDone(ctx.raw, nil, kind.rawValue, $0) }
    default:
        onDone(ctx.raw, nil, kind.rawValue, nil)
    }
}

struct SfmBadRequest: Error, LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

@available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *)
func run(_ request: SfmRequest, _ blobs: [Data], _ ctx: Ctx, _ onDelta: SfmDeltaCallback?) async throws -> String {
    let model = request.useCase == "contentTagging"
        ? SystemLanguageModel(useCase: .contentTagging)
        : SystemLanguageModel.default

    let tools = try makeTools(request.tools ?? [])
    let session: LanguageModelSession
    if let history = request.history, !history.isEmpty {
        var entries: [Transcript.Entry] = []
        if let instructions = request.instructions {
            entries.append(.instructions(Transcript.Instructions(segments: [.text(.init(content: instructions))], toolDefinitions: [])))
        }
        for turn in history {
            let segments = try transcriptSegments(turn, blobs)
            entries.append(turn.role == "assistant"
                ? .response(Transcript.Response(assetIDs: [], segments: segments))
                : .prompt(Transcript.Prompt(segments: segments)))
        }
        session = LanguageModelSession(model: model, tools: tools, transcript: Transcript(entries: entries))
    } else {
        session = LanguageModelSession(model: model, tools: tools, instructions: request.instructions)
    }

    let options = GenerationOptions(temperature: request.temperature, maximumResponseTokens: request.maxTokens)
    let pieces = try promptPieces(request.prompt, blobs)
    let prompt = Prompt { for piece in pieces { piece } }

    if let schemaJson = request.schema {
        let schema: GenerationSchema
        do {
            schema = try JSONDecoder().decode(GenerationSchema.self, from: Data(schemaJson.utf8))
        } catch {
            throw SfmBadRequest(message: "The response schema could not be converted for Foundation Models: \(error)")
        }
        if request.stream == true, let onDelta {
            var last = GeneratedContent(properties: [:])
            for try await snapshot in session.streamResponse(to: prompt, schema: schema, options: options) {
                last = snapshot.rawContent
                last.jsonString.withCString { onDelta(ctx.raw, $0) }
            }
            return last.jsonString
        }
        return try await session.respond(to: prompt, schema: schema, options: options).content.jsonString
    }

    if request.stream == true, let onDelta {
        var last = ""
        for try await snapshot in session.streamResponse(to: prompt, options: options) {
            last = snapshot.content
            last.withCString { onDelta(ctx.raw, $0) }
        }
        return last
    }
    return try await session.respond(to: prompt, options: options).content
}

@available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *)
func makeTools(_ names: [String]) throws -> [any Tool] {
    if names.isEmpty { return [] }
    #if canImport(_Vision_FoundationModels)
    guard #available(iOS 27.0, macOS 27.0, macCatalyst 27.0, *) else {
        throw SfmBadRequest(message: "The Vision tools (OCR, barcode reader) need iOS / macOS 27 or later")
    }
    var tools: [any Tool] = []
    if names.contains("ocr") { tools.append(OCRTool()) }
    if names.contains("barcode") { tools.append(BarcodeReaderTool()) }
    return tools
    #else
    throw SfmBadRequest(message: "The Vision tools (OCR, barcode reader) are not available on this platform")
    #endif
}

// One piece of the prompt: text or an image. A single conforming type, so a PromptBuilder `for` loop can
// interleave them in order (buildArray wants one element type).
@available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *)
struct PromptPiece: PromptRepresentable {
    let text: String?
    let image: CGImage?
    let orientation: CGImagePropertyOrientation?

    var promptRepresentation: Prompt {
        if let image, #available(iOS 27.0, macOS 27.0, macCatalyst 27.0, *) {
            Attachment(image, orientation: orientation)
        } else {
            Prompt(text ?? "")
        }
    }
}

@available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *)
func promptPieces(_ turn: SfmTurn, _ blobs: [Data]) throws -> [PromptPiece] {
    var pieces: [PromptPiece] = []
    if !turn.text.isEmpty {
        pieces.append(PromptPiece(text: turn.text, image: nil, orientation: nil))
    }
    for index in turn.images ?? [] {
        let (image, orientation) = try decodeImage(index, blobs)
        pieces.append(PromptPiece(text: nil, image: image, orientation: orientation))
    }
    return pieces
}

@available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *)
func transcriptSegments(_ turn: SfmTurn, _ blobs: [Data]) throws -> [Transcript.Segment] {
    var segments: [Transcript.Segment] = [.text(.init(content: turn.text))]
    for index in turn.images ?? [] {
        let (image, orientation) = try decodeImage(index, blobs)
        if #available(iOS 27.0, macOS 27.0, macCatalyst 27.0, *) {
            segments.append(.attachment(.init(content: .image(.init(image, orientation: orientation)))))
        }
    }
    return segments
}

// JPEG / PNG / HEIC bytes -> CGImage, keeping the EXIF orientation so the model sees the picture upright.
func decodeImage(_ index: Int, _ blobs: [Data]) throws -> (CGImage, CGImagePropertyOrientation?) {
    guard #available(iOS 27.0, macOS 27.0, macCatalyst 27.0, *) else {
        throw SfmBadRequest(message: "Image input needs iOS / macOS 27 or later")
    }
    guard index >= 0, index < blobs.count,
          let source = CGImageSourceCreateWithData(blobs[index] as CFData, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw SfmBadRequest(message: "Image \(index) could not be decoded")
    }
    let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
    let orientation = (props?[kCGImagePropertyOrientation] as? UInt32).flatMap(CGImagePropertyOrientation.init(rawValue:))
    return (image, orientation)
}

func classify(_ error: Error) -> (SfmError, String) {
    let message = (error as? LocalizedError)?.errorDescription ?? String(describing: error)
    if error is SfmBadRequest { return (.badRequest, message) }
    if #available(iOS 27.0, macOS 27.0, macCatalyst 27.0, *), let e = error as? LanguageModelError {
        switch e {
        case .contextSizeExceeded: return (.contextSize, message)
        case .rateLimited: return (.rateLimited, message)
        case .guardrailViolation: return (.guardrail, message)
        case .refusal: return (.refusal, message)
        case .unsupportedLanguageOrLocale: return (.unsupportedLanguage, message)
        default: return (.other, message)
        }
    }
    if #available(iOS 26.0, macOS 26.0, macCatalyst 26.0, *), let e = error as? LanguageModelSession.GenerationError {
        switch e {
        case .exceededContextWindowSize: return (.contextSize, message)
        case .guardrailViolation: return (.guardrail, message)
        case .unsupportedLanguageOrLocale: return (.unsupportedLanguage, message)
        case .assetsUnavailable: return (.unavailable, message)
        case .rateLimited: return (.rateLimited, message)
        default: return (.other, message)
        }
    }
    return (.other, message)
}
