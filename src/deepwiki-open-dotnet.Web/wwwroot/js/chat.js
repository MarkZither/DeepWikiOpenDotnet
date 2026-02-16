window.chat = {
  scrollIntoViewById: function (id) {
    try {
      const el = document.getElementById(id);
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'end' });
    } catch (e) {
      // ignore
    }
  }
};
