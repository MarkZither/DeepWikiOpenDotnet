// fileDownload.js — JS interop helpers for streaming file downloads in Blazor.
window.fileDownload = {
    /**
     * Triggers a browser file download from a .NET DotNetStreamReference.
     * @param {string} fileName - The suggested filename for the download.
     * @param {any} streamRef - A DotNetStreamReference wrapping the content stream.
     */
    downloadFileFromStream: async function (fileName, streamRef) {
        try {
            const arrayBuffer = await streamRef.arrayBuffer();
            const blob = new Blob([arrayBuffer]);
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = url;
            anchor.download = fileName ?? 'export';
            anchor.style.display = 'none';
            document.body.appendChild(anchor);
            anchor.click();
            document.body.removeChild(anchor);
            URL.revokeObjectURL(url);
        } catch (e) {
            console.error('fileDownload.downloadFileFromStream error:', e);
        }
    }
};
