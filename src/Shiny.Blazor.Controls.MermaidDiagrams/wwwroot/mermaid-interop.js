// Lazily loads mermaid.js from a CDN and renders the supplied diagram source into the host element.
let mermaidPromise = null;
let counter = 0;

function loadMermaid() {
    if (mermaidPromise) return mermaidPromise;
    mermaidPromise = import('https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.esm.min.mjs')
        .then(module => {
            module.default.initialize({ startOnLoad: false, securityLevel: 'loose' });
            return module.default;
        });
    return mermaidPromise;
}

export async function render(host, source, theme) {
    if (!host) return;
    if (!source || source.trim().length === 0) {
        host.innerHTML = '';
        return;
    }

    const id = 'shiny-mermaid-' + (++counter);
    try {
        const mermaid = await loadMermaid();
        if (theme) {
            mermaid.initialize({ startOnLoad: false, theme: theme, securityLevel: 'loose' });
        }
        const { svg } = await mermaid.render(id, source);
        host.innerHTML = svg;
    } catch (err) {
        // A failed render can leave mermaid's scratch container (and its error graphic) appended to
        // <body>; with a fresh id per render, every bad edit in a live editor would add another one.
        document.getElementById('d' + id)?.remove();
        document.getElementById(id)?.remove();
        host.innerHTML = '<pre style="color:#b91c1c;">' + (err && err.message ? err.message : 'Mermaid render failed') + '</pre>';
    }
}
