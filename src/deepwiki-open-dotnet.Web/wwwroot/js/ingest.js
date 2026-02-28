// Programmatically opens the file/folder picker for the given input element.
export function triggerPicker(inputId) {
    const el = document.getElementById(inputId);
    if (el) el.click();
}

// Pre-cached relative paths captured by the change listener below.
// Key: inputId, Value: string[]
const _cachedPaths = {};

// Attaches a capture-phase 'change' listener to the input so we can:
//   1. Pre-cache webkitRelativePaths immediately (before Blazor's own serialisation)
//   2. Notify .NET synchronously via invokeMethodAsync so the spinner appears
//      *before* Blazor finishes enumerating the FileList and sending it over SignalR.
//
// Call this once from OnAfterRenderAsync (first render) with a DotNetObjectReference
// whose target exposes the [JSInvokable] method 'OnFolderChangeDetected'.
export function setupInputListener(inputId, dotNetRef) {
    const el = document.getElementById(inputId);
    if (!el) return;

    el.addEventListener('change', (ev) => {
        const files = ev.target.files;
        if (!files || files.length === 0) return;

        // Pre-cache all relative paths NOW — this is pure in-memory iteration
        // and completes in milliseconds even for large repos.
        _cachedPaths[inputId] = Array.from(files).map(f => f.webkitRelativePath || f.name);

        // Notify .NET immediately so the spinner renders before Blazor starts
        // serialising the full FileList to ship over SignalR.
        dotNetRef.invokeMethodAsync('OnFolderChangeDetected', files.length)
            .catch(() => { /* ignore if component has been disposed */ });
    }, { capture: true });
}

// Returns the pre-cached relative paths captured at change time.
// Falls back to reading el.files directly if the cache is empty (e.g. in tests).
export function getRelativePaths(inputId) {
    if (_cachedPaths[inputId]) {
        const paths = _cachedPaths[inputId];
        delete _cachedPaths[inputId]; // consume once
        return paths;
    }
    const el = document.getElementById(inputId);
    if (!el || !el.files) return [];
    return Array.from(el.files).map(f => f.webkitRelativePath || f.name);
}
