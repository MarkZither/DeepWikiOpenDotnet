window.chat = {
  scrollIntoViewById: function (id) {
    try {
      const el = document.getElementById(id);
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'end' });
    } catch (e) {
      // ignore
    }
  },

  // Render LaTeX math inside a message element using KaTeX auto-render.
  // Called from ChatMessage.razor after the message finishes streaming.
  renderMathInElement: function (elementId) {
    try {
      const el = elementId ? document.getElementById(elementId) : document.body;
      if (!el || typeof window.renderMathInElement !== 'function') return;
      window.renderMathInElement(el, {
        delimiters: [
          { left: '$$', right: '$$', display: true },
          { left: '$',  right: '$',  display: false },
          { left: '\\(', right: '\\)', display: false },
          { left: '\\[', right: '\\]', display: true }
        ],
        throwOnError: false
      });
    } catch (e) {
      // ignore render errors - a bad formula should not crash the chat
    }
  }
};
