// SPIKE: drive the bridge's C entry points on macOS (real ANE) — file mode, then live mode fed from AVAssetReader at 1x.
import AVFoundation
import Foundation

setvbuf(stdout, nil, _IOLBF, 0)
let path = CommandLine.arguments[1]
let mode = CommandLine.arguments.count > 2 ? CommandLine.arguments[2] : "file"
let t0 = Date()
func stamp() -> String { String(format: "%6.2fs", Date().timeIntervalSince(t0)) }

ssb_set_log { _, msg in print("\(stamp()) log: \(String(cString: msg))") }
print("available=\(ssb_is_available())")

let onSeg: SsbSegmentCallback = { _, s, e, fin, text, words in
    guard fin == 1 else { return }
    print("\(stamp()) [\(String(format: "%.2f-%.2f", s, e))] \(String(cString: text))")
    print("         words: \(String(cString: words).prefix(220))…")
}
let onDone: SsbDoneCallback = { _, err in
    print("\(stamp()) done error=\(err.map { String(cString: $0) } ?? "none")")
    exit(0)
}

if mode == "file" {
    ssb_transcribe_file(path, "en-US", nil, onSeg, onDone)
} else {
    // live: decode the mp4's audio to planar float and push it in real time, like the player tap does
    let handle = ssb_live_start("en-US", nil, onSeg, onDone)!
    Thread.detachNewThread {
        let asset = AVURLAsset(url: URL(fileURLWithPath: path))
        let track = asset.tracks(withMediaType: .audio)[0]
        let reader = try! AVAssetReader(asset: asset)
        let out = AVAssetReaderTrackOutput(track: track, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true,
            AVLinearPCMIsNonInterleaved: true, AVLinearPCMIsBigEndianKey: false])
        reader.add(out); reader.startReading()
        var dropped = 0, pushed = 0
        while let sb = out.copyNextSampleBuffer() {
            let fmt = CMSampleBufferGetFormatDescription(sb)!
            let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(fmt)!.pointee
            let frames = CMSampleBufferGetNumSamples(sb), ch = Int(asbd.mChannelsPerFrame)
            var abl = AudioBufferList.allocate(maximumBuffers: ch); var block: CMBlockBuffer?
            CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sb, bufferListSizeNeededOut: nil,
                bufferListOut: abl.unsafeMutablePointer, bufferListSize: AudioBufferList.sizeInBytes(maximumBuffers: ch),
                blockBufferAllocator: nil, blockBufferMemoryAllocator: nil, flags: 0, blockBufferOut: &block)
            var planar = [Float](repeating: 0, count: frames * ch)
            for c in 0..<ch { memcpy(&planar[c * frames], abl[c].mData!, frames * 4) }
            let ok = planar.withUnsafeBufferPointer { ssb_live_push(handle, $0.baseAddress!, Int32(frames), Int32(ch), asbd.mSampleRate) }
            if ok == 1 { pushed += 1 } else { dropped += 1 }
            Thread.sleep(forTimeInterval: Double(frames) / asbd.mSampleRate)  // real time
            free(abl.unsafeMutablePointer)
        }
        print("\(stamp()) live: audio ended pushed=\(pushed) dropped(warming)=\(dropped); finishing")
        ssb_live_finish(handle)
    }
}
RunLoop.main.run()
