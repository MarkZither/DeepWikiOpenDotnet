// Programmatically opens the file/folder picker for the given input element.
// This is the standard Blazor pattern — JS .click() on a file input is
// reliable across all browsers and render modes, and is required because
// Blazor event handlers cannot directly call browser APIs.
export function triggerPicker(inputId) {
    const el = document.getElementById(inputId);
    if (el) el.click();
}

// Returns the webkitRelativePath for every file in the picker selection.
// IBrowserFile.Name only exposes the bare filename; webkitRelativePath gives
// the full repo-relative path (e.g. "src/MyProject/Program.cs"). This property
// exists only on the browser's native File object — unreachable from C# on any
// Blazor hosting model, so JS interop is the only option.
export function getRelativePaths(inputId) {
    const el = document.getElementById(inputId);
    if (!el || !el.files) return [];
    return Array.from(el.files).map(f => f.webkitRelativePath || f.name);
}
