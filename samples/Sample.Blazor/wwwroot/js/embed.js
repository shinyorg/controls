// The gallery's side of the shinylib.net embed (see Layout/EmbedLayout.razor and CLAUDE.md).
// The docs page hosts a demo in an iframe; the two talk over postMessage:
//   frame -> docs   { type: 'shiny-embed:size',  height, fill }   whenever the content's height changes
//   docs  -> frame  { type: 'shiny-embed:theme', mode: 'dark' | 'light' }   when the Starlight theme flips
// A `.demo-fill` page owns its own scrolling, so it reports fill: true and the docs page keeps the
// frame at its fixed height instead of growing it to the content - that would be two scrollers on
// one axis (Jon rule 1).

// Only the docs site may drive the theme. localhost is the Astro dev server.
const allowedOrigins = [
    'https://shinylib.net',
    'https://www.shinylib.net'
];

function isAllowed(origin) {
    if (allowedOrigins.includes(origin))
        return true;

    try {
        const host = new URL(origin).hostname;
        return host === 'localhost' || host === '127.0.0.1';
    } catch {
        return false;
    }
}

let lastHeight = -1;
let lastFill = null;
let pending = false;

// animated demos mutate the DOM every frame - measure at most once per frame
function schedule() {
    if (pending)
        return;

    pending = true;
    requestAnimationFrame(() => {
        pending = false;
        report();
    });
}

function report() {
    const content = document.querySelector('.embed-content');
    if (!content)
        return;

    const fill = content.querySelector('.demo-fill') !== null;
    const height = Math.ceil(content.getBoundingClientRect().height);
    if (height === lastHeight && fill === lastFill)
        return;

    lastHeight = height;
    lastFill = fill;
    // the height is not sensitive, and the frame cannot know the parent's origin before it hears from it
    window.parent.postMessage({ type: 'shiny-embed:size', height, fill }, '*');
}

export function start(dotnet) {
    document.documentElement.classList.add('shiny-embed');

    window.addEventListener('message', e => {
        if (!isAllowed(e.origin))
            return;

        const data = e.data;
        if (data?.type === 'shiny-embed:theme' && (data.mode === 'dark' || data.mode === 'light'))
            dotnet.invokeMethodAsync('SetTheme', data.mode);
    });

    // the content box resizes when the page renders, navigates or a demo expands something; a
    // MutationObserver catches a .demo-fill appearing on navigation without a size change
    const content = document.querySelector('.embed-content');
    new ResizeObserver(schedule).observe(content);
    new MutationObserver(schedule).observe(content, { childList: true, subtree: true });
    report();

    window.parent.postMessage({ type: 'shiny-embed:ready' }, '*');
}
