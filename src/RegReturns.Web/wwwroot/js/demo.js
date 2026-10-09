// Demo page: copy buttons for the published user names, password and keys (ADR 0031).
(function () {
  'use strict';

  function flash(button, text) {
    const original = button.dataset.label || button.textContent;
    button.dataset.label = original;
    button.textContent = text;
    window.setTimeout(function () { button.textContent = original; }, 1500);
  }

  document.querySelectorAll('[data-copy-target]').forEach(function (button) {
    button.addEventListener('click', function () {
      const target = document.getElementById(button.dataset.copyTarget);
      if (!target || !navigator.clipboard) {
        return;
      }

      navigator.clipboard.writeText(target.textContent.trim())
        .then(function () { flash(button, 'Copied'); })
        .catch(function () { flash(button, 'Select and copy'); });
    });
  });
})();
