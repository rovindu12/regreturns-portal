// Site-wide scripts. Page scripts live with their views.
'use strict';

// A table wider than its column scrolls inside its .table-responsive wrapper. Keyboard users can scroll it only if the
// wrapper takes focus (WCAG 2.1.1), so a wrapper that overflows becomes a focusable region named after the table's
// caption; one that fits stays out of the tab order.
(() => {
  const wrappers = document.querySelectorAll('.table-responsive');
  if (wrappers.length === 0) return;

  const update = () => {
    for (const wrapper of wrappers) {
      if (wrapper.scrollWidth > wrapper.clientWidth) {
        const caption = wrapper.querySelector('caption');
        wrapper.tabIndex = 0;
        wrapper.setAttribute('role', 'region');
        wrapper.setAttribute('aria-label', caption ? caption.textContent.replace(/\s+/g, ' ').trim() : 'Table');
      } else if (wrapper.hasAttribute('tabindex')) {
        wrapper.removeAttribute('tabindex');
        wrapper.removeAttribute('role');
        wrapper.removeAttribute('aria-label');
      }
    }
  };

  let pending = false;
  window.addEventListener('resize', () => {
    if (pending) return;
    pending = true;
    window.requestAnimationFrame(() => {
      pending = false;
      update();
    });
  });
  update();
})();
