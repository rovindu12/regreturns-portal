// Accessibility checks for the browser runs: axe-core against WCAG 2.2 level A and AA (ADR 0035).
//
// axe runs in the page through Playwright's evaluate, which the portal's Content Security Policy does not block, so the
// check adds no script tag and causes no CSP report. Every violation of a WCAG A or AA rule fails the run, whatever
// axe's impact rating: each one is a conformance failure. Needs axe-core next to Playwright (NODE_PATH).
'use strict';

const fs = require('node:fs');

const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22a', 'wcag22aa'];
let axeSource;

/**
 * Moves the pointer off the page's controls and waits for running transitions, so colours are measured (and pictured)
 * as a reader sees them: a button the tour just pressed would otherwise be caught halfway through its hover fade.
 */
async function settle(page) {
  await page.mouse.move(0, 0);
  await page.evaluate(async () => {
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await Promise.all(document.getAnimations()
      .filter((animation) => animation.effect?.getComputedTiming().iterations !== Infinity)
      .map((animation) => animation.finished.catch(() => undefined)));
  });
}

/** Runs axe on the current page and returns one line per violation: page, rule, impact and the first targets. */
async function accessibilityViolations(page, label) {
  await settle(page);
  axeSource ??= fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
  if (!(await page.evaluate(() => typeof window.axe === 'object'))) await page.evaluate(axeSource);
  const result = await page.evaluate(
    (tags) => window.axe.run(document, { runOnly: { type: 'tag', values: tags }, resultTypes: ['violations'] }),
    wcagTags);
  return result.violations.map((violation) => {
    const targets = violation.nodes.slice(0, 3).map((node) => node.target.join(' ')).join(', ');
    const more = violation.nodes.length > 3 ? ` and ${violation.nodes.length - 3} more` : '';
    return `${label}: ${violation.id} (${violation.impact}) ${violation.help}; ${targets}${more}`;
  });
}

module.exports = { accessibilityViolations, settle, wcagTags };
