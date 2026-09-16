// One recognizer per page - the browser only runs one at a time anyway. `owner` is the .NET reference that
// started it, so a disposing button only tears down its own session, never another button's.
let recognition = null;
let owner = null;

export function startListening(dotnetRef, culture, continuous) {
    if (!('webkitSpeechRecognition' in window) && !('SpeechRecognition' in window)) {
        dotnetRef.invokeMethodAsync('OnSpeechError', 'SpeechRecognition not supported');
        return;
    }

    // A second start would otherwise orphan the running recognizer, still listening and still holding
    // the previous caller's reference.
    abortCurrent();

    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const rec = new SpeechRecognition();
    rec.continuous = continuous || false;
    rec.interimResults = false;

    if (culture) {
        rec.lang = culture;
    }

    const release = () => {
        if (recognition === rec) {
            recognition = null;
            owner = null;
        }
    };

    rec.onresult = (event) => {
        let transcript = '';
        for (let i = event.resultIndex; i < event.results.length; i++) {
            if (event.results[i].isFinal) {
                transcript += event.results[i][0].transcript;
            }
        }
        if (transcript) {
            dotnetRef.invokeMethodAsync('OnSpeechResult', transcript.trim()).catch(() => { });
        }
    };

    rec.onend = () => {
        release();
        dotnetRef.invokeMethodAsync('OnSpeechEnd').catch(() => { });
    };

    rec.onerror = (event) => {
        release();
        dotnetRef.invokeMethodAsync('OnSpeechError', event.error).catch(() => { });
    };

    recognition = rec;
    owner = dotnetRef;
    rec.start();
}

export function stopListening() {
    if (recognition) {
        recognition.stop();
        recognition = null;
        owner = null;
    }
}

// Called when a button is disposed: abort (not stop - no final result is wanted) and detach the handlers,
// so nothing calls back into the reference being disposed.
export function disposeListening(dotnetRef) {
    if (recognition && owner && dotnetRef && owner._id !== undefined && owner._id !== dotnetRef._id)
        return;
    abortCurrent();
}

function abortCurrent() {
    const rec = recognition;
    recognition = null;
    owner = null;
    if (!rec)
        return;

    rec.onresult = null;
    rec.onend = null;
    rec.onerror = null;
    try { rec.abort(); } catch { /* already ended */ }
}
