// Plays the guided tour (/demo/guide) in headless Chromium: Harbourline's maker raises non-performing loans in last
// month's MDA draft and validates it, its checker justifies the warnings and submits it, the reviewer starts the review and generates an insight, approver.mfa
// approves it after the TOTP step, the auditor verifies the audit chain and admin.demo looks round the administration
// pages. Every person signs in through WSO2 from their card on the demo page, with the password and authenticator key
// that page publishes, as a visitor would. Run by scripts/demo-scenario.sh. Needs Playwright and axe-core (NODE_PATH
// pointing at a global install is enough).
//
// On every portal page it visits, the tour runs axe (WCAG 2.2 A and AA, accessibility.cjs) and fails on any violation;
// WSO2's own pages are checked and reported, not failed. It also fails on any Content Security Policy violation. With
// SCREENSHOTS_DIR set it saves the screenshots of the documentation there, with the published secrets masked.
//
// Prints steps and outcomes only: never the password, the key, a code or a figure.
'use strict';

const path = require('node:path');
const { env, launchPinned, timeout } = require('./pinned-browser.cjs');
const { accessibilityViolations, settle } = require('./accessibility.cjs');
const { demoSession, expectSuccess, latestMonth } = require('./demo-session.cjs');

const portalBase = env('PORTAL_BASE').replace(/\/$/, '');
const wso2Base = env('WSO2_BASE').replace(/\/$/, '');
const screenshotsDir = process.env.SCREENSHOTS_DIR || '';
const bank = 'hlb';
const returnType = 'MDA';

let step = 0;
const say = (message) => console.log(`    ${message}`);
const heading = (message) => console.log(`\n  ${++step}. ${message}`);

// What the browser and axe found, per page; WSO2's pages are reported once per rule.
const cspViolations = [];
const portalAccessibility = [];
const wso2Accessibility = new Map();
const checkedPages = new Set();

// Runs axe on the page in front of the user and, when asked, saves its screenshot. Each portal page in a given state
// (its label) is checked once.
async function look(page, label, shot) {
  const onWso2 = page.url().startsWith(wso2Base);
  if (!checkedPages.has(label)) {
    checkedPages.add(label);
    const found = await accessibilityViolations(page, label);
    if (onWso2) {
      for (const line of found) wso2Accessibility.set(line.split(' ')[1], line);
    } else {
      portalAccessibility.push(...found);
    }
  }
  if (shot && screenshotsDir) {
    await settle(page);
    // The demo page publishes the shared password and the authenticator keys: never in a picture.
    const mask = [page.locator('[data-demo="password"], [data-demo="totp"], [data-demo="api-secret"], .totp-qr')];
    await page.screenshot({ path: path.join(screenshotsDir, `${shot}.png`), mask, maskColor: '#adb5bd', animations: 'disabled' });
  }
}

async function visit(page, pathAndQuery, label, shot) {
  const response = await page.goto(`${portalBase}${pathAndQuery}`);
  if (!response.ok()) throw new Error(`${pathAndQuery} answered ${response.status()}`);
  await look(page, label ?? pathAndQuery, shot);
}

async function main() {
  const browser = await launchPinned([[wso2Base, env('WSO2_CA')], [portalBase, process.env.APP_CA]]);
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    page.setDefaultTimeout(timeout);
    const period = latestMonth();
    const session = demoSession({
      portalBase,
      wso2Base,
      say,
      onStep: (wso2Page, name) => look(wso2Page, name, name === 'wso2-totp' ? 'wso2-one-time-code' : undefined),
    });

    // Chromium reports every blocked script, style or connection on the console: the tour must cause none.
    page.on('console', (message) => {
      if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) {
        cspViolations.push(`${new URL(page.url()).pathname}: ${message.text().slice(0, 200)}`);
      }
    });

    heading('Visitor reads the public pages and the demo page');
    await visit(page, '/', '/', 'landing');
    await visit(page, '/status', '/status', 'status');
    await visit(page, '/demo/guide', '/demo/guide', 'guided-tour');
    const credentials = await session.readDemoPage(page);
    await look(page, '/demo', 'demo-accounts');
    say(`the page publishes the shared password and the authenticator keys of ${Object.keys(credentials.keys).join(', ')}`);
    await page.setViewportSize({ width: 375, height: 740 });
    for (const publicPath of ['/', '/demo', '/demo/guide', '/status']) await visit(page, publicPath, `${publicPath} (phone)`);
    await page.setViewportSize({ width: 1280, height: 800 });
    say('the public pages also pass at phone width');

    if (process.env.RESET_FIRST === 'true') {
      heading('admin.demo resets the demo');
      await session.signIn(page, 'admin.demo', credentials);
      await page.goto(`${portalBase}/admin`);
      const reset = page.getByRole('button', { name: 'Reset demo' });
      if (await reset.isDisabled()) {
        throw new Error(`the reset is cooling down: ${await page.locator('form:has(button) .text-muted').first().innerText()}`);
      }
      await reset.click();
      say(await expectSuccess(page, 'reset'));
      await session.signOut(page);
    }

    heading(`maker.${bank} raises non-performing loans in the ${returnType} draft for ${period} and validates it`);
    await session.signIn(page, `maker.${bank}`, credentials);
    await visit(page, '/bank', '/bank', 'bank-returns');
    const upload = await page.locator('a[href*="/upload"]').first().getAttribute('href', { timeout: 1_000 }).catch(() => null);
    const open = page.getByRole('link', { name: `Open ${returnType} ${period}`, exact: true });
    const returnPath = await open.getAttribute('href');
    if (!returnPath) throw new Error(`no ${returnType} return for ${period} in the bank's list (reset the demo first)`);
    const submissionId = returnPath.split('/').pop();
    await visit(page, returnPath, 'bank return (draft)');
    // Raise non-performing loans by half and keep the ratio consistent with them: no error, but warnings (the 8%
    // trigger, the 40% movement) for the checker to justify and a movement for the insight to explain.
    const field = (code) => page.locator(`[name="Values[${code}]"]`);
    const number = async (code) => Number((await field(code).inputValue()).replaceAll(',', ''));
    const loans = await number('TOTAL_LOANS');
    const npl = Math.round((await number('NPL_AMOUNT')) * 150) / 100;
    await field('NPL_AMOUNT').fill(npl.toFixed(2));
    await field('NPL_RATIO').fill((Math.round((npl / loans) * 10_000) / 100).toFixed(2));
    await page.getByRole('button', { name: 'Save draft' }).click();
    say(await expectSuccess(page, 'save'));
    await page.getByRole('button', { name: 'Validate', exact: true }).click();
    say(await expectSuccess(page, 'validate'));
    await look(page, 'bank return (validated)', 'return-validated');
    if (await page.getByRole('button', { name: 'Submit return' }).count() > 0) {
      throw new Error('the maker was offered the submit step');
    }
    say('the maker cannot submit');
    if (upload) await visit(page, upload, 'bank upload');
    const missing = await page.goto(`${portalBase}/bank/returns/00000000-0000-0000-0000-000000000000`);
    if (missing.status() !== 404 || !(await page.title()).startsWith('Page not found')) {
      throw new Error(`a return that does not exist answered ${missing.status()} "${await page.title()}"`);
    }
    await look(page, 'page not found');
    say('a return that does not exist shows the not-found page');
    await session.signOut(page);

    heading(`checker.${bank} justifies the warnings and submits it`);
    await session.signIn(page, `checker.${bank}`, credentials);
    await visit(page, returnPath, 'bank return (checker)');
    if (await page.locator('textarea[name="justification"]').count() === 0) {
      throw new Error('the raised non-performing loans caused no warning to justify');
    }
    // The picture shows the findings with their justification boxes rather than the top of the page.
    await page.locator('#findings').evaluate((section) => section.scrollIntoView({ block: 'start', behavior: 'instant' }));
    await look(page, 'bank return (to justify)', 'return-justify');
    for (let left = await page.locator('textarea[name="justification"]').count(); left > 0;
      left = await page.locator('textarea[name="justification"]').count()) {
      const form = page.locator('form', { has: page.locator('textarea[name="justification"]') }).first();
      await form.locator('textarea').fill('Checked against the general ledger; the movement is explained by normal business.');
      await form.getByRole('button', { name: 'Save justification' }).click();
      await expectSuccess(page, 'justify');
      say('justified a warning');
    }
    await look(page, 'bank return (justified)');
    await page.locator('#submit-comment').fill('Figures checked against the general ledger. Submitted by the demo scenario.');
    await page.getByRole('button', { name: 'Submit return' }).click();
    say(await expectSuccess(page, 'submit'));
    await look(page, 'bank return (submitted)');
    await session.signOut(page);

    heading('reviewer starts the review and asks for an insight');
    await session.signIn(page, 'reviewer', credentials);
    await visit(page, '/supervision', '/supervision', 'supervision-worklist');
    const reviewPath = `/supervision/returns/${submissionId}`;
    await visit(page, reviewPath, 'supervision return (submitted)');
    await page.getByRole('button', { name: 'Start review' }).click();
    say(await expectSuccess(page, 'start review'));
    await page.getByRole('button', { name: 'Generate insight' }).click();
    say(await expectSuccess(page, 'insight'));
    const headline = page.locator('[data-insight="headline"]');
    await headline.waitFor();
    await headline.evaluate((element) => element.closest('section, .card')?.scrollIntoView({ block: 'start', behavior: 'instant' }));
    await look(page, 'supervision return (insight)', 'review-insight');
    say('the advisory insight is on the page, with the payload that was shared');
    await visit(page, '/reports', '/reports');
    await page.locator('canvas').first().waitFor();
    await page.waitForTimeout(1_000);
    await look(page, '/reports (charts)', 'reports');
    say('the reports dashboard draws its charts');
    await session.signOut(page);

    heading('approver.mfa approves it after the TOTP step');
    await session.signIn(page, 'approver.mfa', credentials);
    await visit(page, reviewPath, 'supervision return (under review)');
    await page.locator('#decision-comment').fill('Reviewed against prior periods and thresholds. Approved by the demo scenario.');
    await page.getByRole('button', { name: 'Approve', exact: true }).click();
    say(await expectSuccess(page, 'approve'));
    await look(page, 'supervision return (approved)');
    await session.signOut(page);

    heading('auditor verifies the audit chain');
    await session.signIn(page, 'auditor', credentials);
    await visit(page, '/audit', '/audit');
    await page.getByRole('button', { name: 'Verify chain' }).click();
    const verdict = await expectSuccess(page, 'verify chain');
    if (!verdict.includes('intact')) throw new Error(`verify chain: ${verdict}`);
    await look(page, '/audit (verified)', 'audit-verified');
    say(verdict.split('.')[0]);
    await session.signOut(page);

    heading('admin.demo looks round the administration pages');
    await session.signIn(page, 'admin.demo', credentials);
    await visit(page, '/admin', '/admin');
    await visit(page, '/admin/users', '/admin/users', 'admin-users');
    await visit(page, '/admin/diagnostics', '/admin/diagnostics', 'admin-diagnostics');
    await visit(page, '/admin/templates', '/admin/templates');
    const version = await page.getByRole('link', { name: /^Version \d+/ }).first().getAttribute('href');
    if (!version) throw new Error('the templates page lists no published version');
    await visit(page, version, 'template version', 'template-version');
    say('users, diagnostics, templates and a template version');
    await session.signOut(page);

    heading('Every portal page met WCAG 2.2 AA and its Content Security Policy');
    for (const line of wso2Accessibility.values()) say(`reported (WSO2's page, not the portal's): ${line}`);
    if (portalAccessibility.length > 0) {
      throw new Error(`accessibility violations (axe, WCAG 2.2 A and AA):\n      ${portalAccessibility.join('\n      ')}`);
    }
    say(`axe found no WCAG 2.2 A or AA violation on ${[...checkedPages].filter((p) => !p.startsWith('wso2-')).length} portal page states`);
    if (cspViolations.length > 0) throw new Error(`CSP violations:\n      ${cspViolations.join('\n      ')}`);
    say('the browser reported no CSP violation on any page of the tour');
    if (screenshotsDir) say(`screenshots saved in ${screenshotsDir}`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => {
  console.error(`    FAIL ${error.message}`);
  process.exit(1);
});
