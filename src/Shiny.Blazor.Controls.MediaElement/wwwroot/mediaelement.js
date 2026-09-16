// Shiny.Blazor.Controls.MediaElement — HTML5 media bridge.
//
// Everything crossing back into .NET goes through one `onStatus` snapshot rather than a call per event.
// The media element fires timeupdate/progress several times a second, and one interop call per event
// per property is enough traffic to be visible in a WASM profile.

const registry = new WeakMap();

// navigator.mediaSession is page-global: its action handlers close over whichever player set them last.
// Tracked so that player's dispose can clear them - otherwise the session keeps the removed <video> (and
// its buffered media) plus the dead .NET reference alive for the life of the page.
let mediaSessionOwner = null;
const mediaSessionActions = ['play', 'pause', 'stop', 'seekto'];

function clearMediaSession() {
    mediaSessionOwner = null;
    if (!('mediaSession' in navigator))
        return;

    for (const action of mediaSessionActions) {
        try { navigator.mediaSession.setActionHandler(action, null); }
        catch { /* the browser doesn't support this action */ }
    }
    try { navigator.mediaSession.metadata = null; } catch { /* ignore */ }
}

function stateOf(video) {
    if (video.error) return 'Failed';
    if (!video.currentSrc && !video.src) return 'None';
    if (video.readyState < 1) return 'Opening';
    if (video.seeking || (video.readyState < 3 && !video.paused)) return 'Buffering';
    if (video.paused) return video.currentTime === 0 ? 'Stopped' : 'Paused';
    return 'Playing';
}

function bufferedFraction(video) {
    const duration = video.duration;
    if (!isFinite(duration) || duration <= 0 || video.buffered.length === 0)
        return 0;

    // The last range's end is what "buffered ahead" means for a linear player; earlier ranges are
    // leftovers from seeking around.
    const end = video.buffered.end(video.buffered.length - 1);
    return Math.max(0, Math.min(1, end / duration));
}

function snapshot(video) {
    const duration = isFinite(video.duration) ? video.duration : 0;
    return {
        state: stateOf(video),
        position: video.currentTime || 0,
        duration: duration,
        buffered: bufferedFraction(video),
        muted: video.muted,
        volume: video.volume,
        width: video.videoWidth || 0,
        height: video.videoHeight || 0
    };
}

function probeCapabilities(video) {
    // Safari on iOS silently ignores writes to `volume`. Nothing advertises that, so the only reliable
    // test is to write a value and read it back.
    let volumeSettable = false;
    try {
        const original = video.volume;
        video.volume = original === 0.5 ? 0.4 : 0.5;
        volumeSettable = video.volume !== original;
        video.volume = original;
    } catch {
        volumeSettable = false;
    }

    return {
        volume: volumeSettable,
        pictureInPicture: typeof video.requestPictureInPicture === 'function'
            && document.pictureInPictureEnabled === true,
        fullscreen: typeof document.fullscreenEnabled === 'boolean'
            ? document.fullscreenEnabled
            : typeof video.webkitEnterFullscreen === 'function',
        mediaSession: 'mediaSession' in navigator
    };
}

export function init(video, container, dotnet) {
    if (registry.has(video))
        dispose(video);

    const push = () => dotnet.invokeMethodAsync('OnStatus', snapshot(video));

    const handlers = {
        loadedmetadata: () => { dotnet.invokeMethodAsync('OnOpened', snapshot(video)); },
        timeupdate: push,
        progress: push,
        durationchange: push,
        play: push,
        playing: push,
        pause: push,
        waiting: push,
        seeked: push,
        volumechange: push,
        ratechange: push,
        emptied: push,
        ended: () => { push(); dotnet.invokeMethodAsync('OnEnded'); },
        error: () => {
            const err = video.error;
            const codes = {
                1: 'Playback was aborted.',
                2: 'A network error interrupted the media download.',
                3: 'The media could not be decoded.',
                4: 'The media format is not supported, or the source could not be found.'
            };
            dotnet.invokeMethodAsync('OnFailed', (err && codes[err.code]) || 'The media failed to load.');
        },
        enterpictureinpicture: () => dotnet.invokeMethodAsync('OnPictureInPictureChangedJs', true),
        leavepictureinpicture: () => dotnet.invokeMethodAsync('OnPictureInPictureChangedJs', false)
    };

    for (const [name, fn] of Object.entries(handlers))
        video.addEventListener(name, fn);

    // Fullscreen is reported on the document, not the element, and fires for exits we did not initiate
    // (Escape, the browser's own control) — which is exactly why the component can't just track its own
    // requests and assume.
    const onFullscreen = () => dotnet.invokeMethodAsync(
        'OnFullScreenChangedJs', document.fullscreenElement === container);

    document.addEventListener('fullscreenchange', onFullscreen);

    registry.set(video, { handlers, onFullscreen, dotnet });
    return probeCapabilities(video);
}

export function dispose(video) {
    const entry = registry.get(video);
    if (!entry) return;

    for (const [name, fn] of Object.entries(entry.handlers))
        video.removeEventListener(name, fn);

    document.removeEventListener('fullscreenchange', entry.onFullscreen);
    registry.delete(video);

    if (mediaSessionOwner === video)
        clearMediaSession();

    try { video.pause(); } catch { /* already torn down */ }
    video.removeAttribute('src');
    video.load();
}

export function setSource(video, src, autoPlay) {
    if (!src) {
        video.removeAttribute('src');
        video.load();
        return;
    }

    video.src = src;
    video.load();

    if (autoPlay) {
        // Autoplay with sound is blocked unless the user has interacted with the page; report it rather
        // than leaving a player that silently never starts.
        const attempt = video.play();
        if (attempt && typeof attempt.catch === 'function') {
            attempt.catch(() => {
                const entry = registry.get(video);
                entry?.dotnet.invokeMethodAsync(
                    'OnFailed',
                    'Autoplay was blocked by the browser. Mute the player or start playback from a user gesture.');
            });
        }
    }
}

export function play(video) {
    const attempt = video.play();
    if (attempt && typeof attempt.catch === 'function')
        attempt.catch(() => { /* surfaced through the error/status channel */ });
}

export function pause(video) { video.pause(); }

export function stop(video) {
    video.pause();
    video.currentTime = 0;
}

export function seek(video, seconds) {
    if (isFinite(video.duration))
        video.currentTime = Math.max(0, Math.min(seconds, video.duration));
}

export function setVolume(video, volume) { video.volume = Math.max(0, Math.min(1, volume)); }
export function setMuted(video, muted) { video.muted = !!muted; }
export function setRate(video, rate) { video.playbackRate = rate; }
export function setLoop(video, loop) { video.loop = !!loop; }
export function setObjectFit(video, fit) { video.style.objectFit = fit; }

export async function requestFullscreen(container) {
    if (typeof container.requestFullscreen === 'function') {
        await container.requestFullscreen();
        return true;
    }

    // iOS Safari has no Fullscreen API on arbitrary elements — only the video element's own native
    // presentation. That loses the custom transport bar, but it beats no fullscreen at all.
    const video = container.querySelector('video');
    if (video && typeof video.webkitEnterFullscreen === 'function') {
        video.webkitEnterFullscreen();
        return true;
    }

    return false;
}

export async function exitFullscreen() {
    if (document.fullscreenElement && typeof document.exitFullscreen === 'function')
        await document.exitFullscreen();
}

export async function requestPictureInPicture(video) {
    if (typeof video.requestPictureInPicture !== 'function' || !document.pictureInPictureEnabled)
        return false;

    try {
        await video.requestPictureInPicture();
        return true;
    } catch {
        return false;
    }
}

export async function exitPictureInPicture() {
    if (document.pictureInPictureElement)
        await document.exitPictureInPicture();
}

export function setMediaSession(video, title, artist, album, artwork, dotnet) {
    if (!('mediaSession' in navigator))
        return;

    if (!title && !artist && !album) {
        if (mediaSessionOwner === video || mediaSessionOwner === null)
            clearMediaSession();
        return;
    }

    mediaSessionOwner = video;

    navigator.mediaSession.metadata = new MediaMetadata({
        title: title || '',
        artist: artist || '',
        album: album || '',
        artwork: artwork ? [{ src: artwork }] : []
    });

    const set = (action, fn) => {
        try { navigator.mediaSession.setActionHandler(action, fn); }
        catch { /* the browser doesn't support this action */ }
    };

    set('play', () => { video.play(); dotnet.invokeMethodAsync('OnRemoteCommand', 'Play'); });
    set('pause', () => { video.pause(); dotnet.invokeMethodAsync('OnRemoteCommand', 'Pause'); });
    set('stop', () => { video.pause(); video.currentTime = 0; dotnet.invokeMethodAsync('OnRemoteCommand', 'Stop'); });
    set('seekto', details => {
        if (details.seekTime != null) {
            video.currentTime = details.seekTime;
            dotnet.invokeMethodAsync('OnRemoteCommand', 'Seek');
        }
    });
}
