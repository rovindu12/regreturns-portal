// Bank return pages. Progressive enhancement only: every check here is repeated on the server.
(function () {
  'use strict';

  // Refuse files over the size limit before uploading them.
  document.querySelectorAll('input[type="file"][data-max-bytes]').forEach(function (input) {
    input.addEventListener('change', function () {
      var max = Number(input.dataset.maxBytes);
      var file = input.files && input.files[0];
      input.setCustomValidity(file && file.size > max ? input.dataset.tooLarge : '');
      input.reportValidity();
    });
  });

  // Warn before leaving a return form with unsaved changes.
  document.querySelectorAll('form[data-warn-unsaved]').forEach(function (form) {
    var dirty = false;
    form.addEventListener('input', function () { dirty = true; });
    form.addEventListener('change', function () { dirty = true; });
    form.addEventListener('submit', function () { dirty = false; });
    window.addEventListener('beforeunload', function (event) {
      if (dirty) {
        event.preventDefault();
        event.returnValue = '';
      }
    });
  });
})();
