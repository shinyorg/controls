// Shiny Blazor audio analysis: an AudioWorklet that taps a playing <audio>/<video> (or a MediaStream, the camera's
// microphone), downmixes to mono, downsamples to the analyzer's rate and posts ~100 ms Float32 chunks to .NET.
//
// ⚠️ createMediaElementSource is one-way: once an element is routed through Web Audio it plays only through its
// AudioContext, for the element's lifetime. So the graph is built on first use and kept (the element still plays
// normally through ctx.destination); stopping only detaches the tap.
// ⚠️ Cross-origin media without CORS headers is delivered to Web Audio as silence. Serve it with
// Access-Control-Allow-Origin and set crossorigin="anonymous" on the element.

const graphs = new WeakMap();   // element -> { ctx, source, tap, sink, onSeeking, onPlay }
const streams = new Map();      // key -> { ctx, source, tap, sink }

const processor = `
class ShinyPcmTap extends AudioWorkletProcessor {
    constructor(options) {
        super();
        this.target = options.processorOptions.rate;
        this.ratio = sampleRate / this.target;
        this.acc = 0; this.sum = 0; this.n = 0;
        this.buf = new Float32Array(Math.max(1, Math.round(this.target * 0.1)));
        this.len = 0;
        this.on = true;
        this.port.onmessage = e => { if (e.data === 'stop') this.on = false; };
    }
    process(inputs) {
        if (!this.on) return false;
        const input = inputs[0];
        if (!input || input.length === 0) return true;
        const channels = input.length, frames = input[0].length;
        for (let i = 0; i < frames; i++) {
            let s = 0;
            for (let c = 0; c < channels; c++) s += input[c][i];
            this.sum += s / channels; this.n++; this.acc += 1;
            if (this.acc >= this.ratio) {
                this.buf[this.len++] = this.sum / this.n;
                this.sum = 0; this.n = 0; this.acc -= this.ratio;
                if (this.len === this.buf.length) {
                    this.port.postMessage(this.buf.slice(0));
                    this.len = 0;
                }
            }
        }
        return true;
    }
}
registerProcessor('shiny-pcm-tap', ShinyPcmTap);
`;

const moduleUrl = URL.createObjectURL(new Blob([processor], { type: 'application/javascript' }));
const loaded = new WeakSet();

async function ensureModule(ctx) {
    if (loaded.has(ctx)) return;
    await ctx.audioWorklet.addModule(moduleUrl);
    loaded.add(ctx);
}

function createTap(ctx, source, rate, onChunk) {
    const tap = new AudioWorkletNode(ctx, 'shiny-pcm-tap', { processorOptions: { rate } });
    // a worklet is only pulled while it reaches the destination; a muted gain keeps it running without sound
    const sink = ctx.createGain();
    sink.gain.value = 0;
    source.connect(tap);
    tap.connect(sink);
    sink.connect(ctx.destination);
    tap.port.onmessage = e => onChunk(e.data);
    return { tap, sink };
}

function releaseTap(g) {
    if (!g.tap) return;
    try { g.tap.port.postMessage('stop'); } catch { }
    try { g.source.disconnect(g.tap); } catch { }
    try { g.tap.disconnect(); g.sink.disconnect(); } catch { }
    g.tap = null; g.sink = null;
}

// Tap a playing media element. Chunks go to dotnet.OnAudio(bytes, rate, position); seeks to dotnet.OnAudioSeek(position).
export async function startMedia(media, dotnet, rate) {
    let g = graphs.get(media);
    if (!g) {
        const ctx = new AudioContext();
        const source = ctx.createMediaElementSource(media);
        source.connect(ctx.destination);
        g = { ctx, source, tap: null, sink: null };
        // autoplay policy: a context created outside a gesture starts suspended; playing is the gesture
        g.onPlay = () => { if (ctx.state === 'suspended') ctx.resume().catch(() => { }); };
        media.addEventListener('play', g.onPlay);
        graphs.set(media, g);
    }
    await ensureModule(g.ctx);
    releaseTap(g);

    const { tap, sink } = createTap(g.ctx, g.source, rate, chunk => {
        const position = Math.max(0, media.currentTime - chunk.length / rate);
        dotnet.invokeMethodAsync('OnAudio', new Uint8Array(chunk.buffer), rate, position).catch(() => { });
    });
    g.tap = tap; g.sink = sink;

    if (g.onSeeking) media.removeEventListener('seeking', g.onSeeking);
    g.onSeeking = () => dotnet.invokeMethodAsync('OnAudioSeek', media.currentTime).catch(() => { });
    media.addEventListener('seeking', g.onSeeking);

    if (!media.paused && g.ctx.state === 'suspended') g.ctx.resume().catch(() => { });
}

export function stopMedia(media) {
    const g = graphs.get(media);
    if (!g) return;
    releaseTap(g);
    if (g.onSeeking) {
        media.removeEventListener('seeking', g.onSeeking);
        g.onSeeking = null;
    }
}

// Tap a MediaStream (the camera's microphone). It is not played back — that would echo.
export async function startStream(key, stream, dotnet, rate) {
    stopStream(key);
    const ctx = new AudioContext();
    await ensureModule(ctx);
    const source = ctx.createMediaStreamSource(stream);
    const started = performance.now();
    const { tap, sink } = createTap(ctx, source, rate, chunk => {
        const position = Math.max(0, (performance.now() - started) / 1000 - chunk.length / rate);
        dotnet.invokeMethodAsync('OnAudio', new Uint8Array(chunk.buffer), rate, position).catch(() => { });
    });
    streams.set(key, { ctx, source, tap, sink });
    if (ctx.state === 'suspended') ctx.resume().catch(() => { });
}

export function stopStream(key) {
    const g = streams.get(key);
    if (!g) return;
    releaseTap(g);
    try { g.source.disconnect(); } catch { }
    g.ctx.close().catch(() => { });
    g.owned?.getTracks().forEach(t => t.stop());
    streams.delete(key);
}

// Open the microphone and tap it (the camera's audio analyzer). Resolves false when permission is refused.
export async function startMicrophone(key, dotnet, rate) {
    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: true } });
    }
    catch {
        return false;
    }
    await startStream(key, stream, dotnet, rate);
    const g = streams.get(key);
    if (g) g.owned = stream;
    return true;
}

// Decode a whole source ahead of playback: fetch, decode, resample to mono `rate`, and send 10 s chunks to
// dotnet.OnSourceAudio(bytes, rate, position), then dotnet.OnSourceAudioEnd(ok). Needs the media to be fetchable
// (same origin, or CORS).
export async function decodeSource(url, dotnet, rate) {
    let ok = false;
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const encoded = await response.arrayBuffer();
        const decoder = new OfflineAudioContext(1, 1, 44100);
        const decoded = await decoder.decodeAudioData(encoded);

        const length = Math.ceil(decoded.duration * rate);
        const offline = new OfflineAudioContext(1, Math.max(1, length), rate);
        const source = offline.createBufferSource();
        source.buffer = decoded;
        source.connect(offline.destination);
        source.start();
        const rendered = await offline.startRendering();
        const samples = rendered.getChannelData(0);

        const chunk = rate * 10;
        for (let i = 0; i < samples.length; i += chunk) {
            const part = samples.slice(i, Math.min(samples.length, i + chunk));
            await dotnet.invokeMethodAsync('OnSourceAudio', new Uint8Array(part.buffer), rate, i / rate);
        }
        ok = samples.length > 0;
    }
    catch {
        ok = false;
    }
    await dotnet.invokeMethodAsync('OnSourceAudioEnd', ok).catch(() => { });
}
