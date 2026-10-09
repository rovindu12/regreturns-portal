// Plays the guided tour (/demo/guide) in headless Chromium: Harbourline's maker validates last month's MDA draft, its
// checker justifies any warnings and submits it, the reviewer starts the review and generates an insight, approver.mfa
// approves it after the TOTP step, and the auditor verifies the audit chain. Every person signs in through WSO2 from
// their card on the demo page, with the password and authenticator key that page publishes, as a visitor would.
// Run by scripts/demo-scenario.sh. Needs Playwright (NODE_PATH pointing at a global install is enough).
//
// Prints steps and outcomes only: never the password, the key, a code or a figure.
'use strict';

const crypto = require('node:crypto');
const { env, launchPinned, timeout } = require('./pinned-browser.cjs');

const portalBase = env('PORTAL_BASE').replace(/\/$/, '');
const wso2Base = env('WSO2_BASE').replace(/\/$/, '');
const bank = 'hlb';
const returnType = 'MDA';

let step = 0;
const say = (message) => console.log(`    ${message}`);
const heading = (message) => console.log(`\n  ${++step}. ${message}`);

// RFC 6238 with WSO2's parameters: HMAC-SHA1, six digits, 30-second steps.
function base32Decode(text) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of text.replace(/=+$/, '').toUpperCase()) {
    const value = alphabet.indexOf(c);
    if (value < 0) throw new Error('the published authenticator key is not Base32');
    bits += value.toString(2).padStart(5, '0');
  }
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

function totp(secret, at = Date.now()) {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 30_000)));
  const hmac = crypto.createHmac('sha1', base32Decode(secret)).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  return String((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

// The return being filed now: the latest completed month (UTC), as the seed files it.
function latestMonth(now = new Date()) {
  const month = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
  return `${month.getUTCFullYear()}-${String(month.getUTCMonth() + 1).padStart(2, '0')}`;
}

async function expectSuccess(page, what) {
  const error = page.locator('.alert-danger');
  if (await error.count() > 0) throw new Error(`${what}: ${await error.first().innerText()}`);
  const success = page.locator('.alert-success');
  if (await success.count() === 0) throw new Error(`${what}: the page shows no confirmation`);
  return success.first().innerText();
}

// The shared password and every published authenticator key, by user name.
async function readDemoPage(page) {
  await page.goto(`${portalBase}/demo`);
  const password = (await page.locator('[data-demo="password"]').innerText()).trim();
  const keys = Object.fromEntries(await page.locator('[data-demo-user]').evaluateAll((cards) => cards
    .filter((card) => card.querySelector('[data-demo="totp"]'))
    .map((card) => [card.dataset.demoUser, card.querySelector('[data-demo="totp"]').textContent.trim()])));
  if (!password || !keys['approver.mfa']) {
    throw new Error('the demo page does not publish the password and approver.mfa\'s authenticator key');
  }
  return { password, keys };
}

async function signIn(page, user, credentials) {
  const key = credentials.keys[user];
  const withTotp = key !== undefined;
  await page.goto(`${portalBase}/demo`);
  await page.locator(`[data-demo-user="${user}"]`).getByRole('link', { name: `Sign in as ${user}` }).click();
  await page.waitForURL((url) => url.href.startsWith(wso2Base));

  // With the login hint WSO2 shows the account and asks only for the password (the user name is a hidden field).
  const passwordField = page.locator('#password, input[name="password"]').first();
  await passwordField.waitFor();
  const userField = page.locator('#usernameUserInput, input[name="username"]').first();
  const hinted = (await userField.inputValue()) === user;
  if (!hinted) await userField.fill(user);
  await passwordField.fill(credentials.password);
  await page.locator('button[type="submit"]').first().click();

  const backAtPortal = (url) => url.href.startsWith(portalBase) && !url.pathname.startsWith('/signin-oidc');
  if (withTotp) {
    await page.waitForURL((url) => backAtPortal(url) || url.pathname.toLowerCase().includes('totp'));
    if (backAtPortal(new URL(page.url()))) throw new Error(`WSO2 did not ask ${user} for a one-time code`);
    // Never type a code in the last seconds of its window.
    const left = 30_000 - (Date.now() % 30_000);
    if (left < 4_000) await page.waitForTimeout(left + 500);
    const code = totp(key);
    const digits = page.locator('input[id^="pincode-"]');
    if (await digits.count() === 6) {
      // WSO2's page enables Continue from key events, so type the digits rather than setting the values.
      for (let i = 0; i < 6; i++) await digits.nth(i).press(code[i]);
    } else {
      await page.locator('input[name="token"], #token').first().fill(code);
    }
    await page.locator('button[type="submit"], input[type="submit"]').first().click();
  }

  await page.waitForURL(backAtPortal);
  say(`signed in as ${user}${withTotp ? ' with password and one-time code' : ''}` +
    `${hinted ? ' (WSO2 took the user name from the demo card)' : ''}; landed on ${new URL(page.url()).pathname}`);
}

async function signOut(page) {
  await page.getByRole('button', { name: /sign out/i }).first().click();
  await page.waitForURL((url) => url.href.startsWith(portalBase) && !url.pathname.startsWith('/signout-callback-oidc'));
}

async function main() {
  const browser = await launchPinned([[wso2Base, env('WSO2_CA')], [portalBase, process.env.APP_CA]]);
  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(timeout);
    const period = latestMonth();

    // Chromium reports every blocked script, style or connection on the console: the tour must cause none.
    const violations = [];
    page.on('console', (message) => {
      if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) {
        violations.push(`${new URL(page.url()).pathname}: ${message.text().slice(0, 200)}`);
      }
    });

    heading('Visitor reads the public pages and the demo page');
    for (const path of ['/', '/status', '/demo/guide']) {
      const response = await page.goto(`${portalBase}${path}`);
      if (!response.ok()) throw new Error(`${path} answered ${response.status()}`);
    }
    const credentials = await readDemoPage(page);
    say(`the page publishes the shared password and the authenticator keys of ${Object.keys(credentials.keys).join(', ')}`);

    if (process.env.RESET_FIRST === 'true') {
      heading('admin.demo resets the demo');
      await signIn(page, 'admin.demo', credentials);
      await page.goto(`${portalBase}/admin/users`);
      await page.goto(`${portalBase}/admin`);
      await page.getByRole('button', { name: 'Reset demo' }).click();
      say(await expectSuccess(page, 'reset'));
      await signOut(page);
    }

    heading(`maker.${bank} validates the ${returnType} draft for ${period}`);
    await signIn(page, `maker.${bank}`, credentials);
    await page.goto(`${portalBase}/bank`);
    const open = page.getByRole('link', { name: `Open ${returnType} ${period}`, exact: true });
    const returnPath = await open.getAttribute('href');
    if (!returnPath) throw new Error(`no ${returnType} return for ${period} in the bank's list (reset the demo first)`);
    const submissionId = returnPath.split('/').pop();
    await page.goto(`${portalBase}${returnPath}`);
    await page.getByRole('button', { name: 'Validate', exact: true }).click();
    say(await expectSuccess(page, 'validate'));
    if (await page.getByRole('button', { name: 'Submit return' }).count() > 0) {
      throw new Error('the maker was offered the submit step');
    }
    say('the maker cannot submit');
    await signOut(page);

    heading(`checker.${bank} justifies any warnings and submits it`);
    await signIn(page, `checker.${bank}`, credentials);
    await page.goto(`${portalBase}${returnPath}`);
    for (let left = await page.locator('textarea[name="justification"]').count(); left > 0;
      left = await page.locator('textarea[name="justification"]').count()) {
      const form = page.locator('form', { has: page.locator('textarea[name="justification"]') }).first();
      await form.locator('textarea').fill('Checked against the general ledger; the movement is explained by normal business.');
      await form.getByRole('button', { name: 'Save justification' }).click();
      await expectSuccess(page, 'justify');
      say('justified a warning');
    }
    await page.locator('#submit-comment').fill('Figures checked against the general ledger. Submitted by the demo scenario.');
    await page.getByRole('button', { name: 'Submit return' }).click();
    say(await expectSuccess(page, 'submit'));
    await signOut(page);

    heading('reviewer starts the review and asks for an insight');
    await signIn(page, 'reviewer', credentials);
    const reviewPath = `/supervision/returns/${submissionId}`;
    await page.goto(`${portalBase}${reviewPath}`);
    await page.getByRole('button', { name: 'Start review' }).click();
    say(await expectSuccess(page, 'start review'));
    await page.getByRole('button', { name: 'Generate insight' }).click();
    say(await expectSuccess(page, 'insight'));
    await page.locator('[data-insight="headline"]').waitFor();
    say('the advisory insight is on the page, with the payload that was shared');
    await page.goto(`${portalBase}/reports`);
    await page.locator('canvas').first().waitFor();
    say('the reports dashboard draws its charts');
    await signOut(page);

    heading('approver.mfa approves it after the TOTP step');
    await signIn(page, 'approver.mfa', credentials);
    await page.goto(`${portalBase}${reviewPath}`);
    await page.locator('#decision-comment').fill('Reviewed against prior periods and thresholds. Approved by the demo scenario.');
    await page.getByRole('button', { name: 'Approve', exact: true }).click();
    say(await expectSuccess(page, 'approve'));
    await signOut(page);

    heading('auditor verifies the audit chain');
    await signIn(page, 'auditor', credentials);
    await page.goto(`${portalBase}/audit`);
    await page.getByRole('button', { name: 'Verify chain' }).click();
    const verdict = await expectSuccess(page, 'verify chain');
    if (!verdict.includes('intact')) throw new Error(`verify chain: ${verdict}`);
    say(verdict.split('.')[0]);
    await signOut(page);

    heading('No page broke its Content Security Policy');
    if (violations.length > 0) throw new Error(`CSP violations:\n      ${violations.join('\n      ')}`);
    say('the browser reported no CSP violation on any page of the tour');
  } finally {
    await browser.close();
  }
}

main().catch((error) => {
  console.error(`    FAIL ${error.message}`);
  process.exit(1);
});
