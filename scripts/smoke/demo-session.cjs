// Signing in to the demo portal as a visitor would: the password and authenticator keys come from the public /demo page,
// and every person signs in through WSO2 from their card there. Shared by demo-scenario.cjs and its page checks.
//
// Never prints the password, a key or a code.
'use strict';

const crypto = require('node:crypto');

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

/**
 * Sign-in helpers bound to one portal and WSO2. `onStep(page, name)` is called on WSO2's pages (sign-in, one-time
 * code) before anything is typed, so a caller can check or capture them.
 */
function demoSession({ portalBase, wso2Base, say, onStep = async () => {} }) {
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
    await onStep(page, 'wso2-sign-in');
    const userField = page.locator('#usernameUserInput, input[name="username"]').first();
    const hinted = (await userField.inputValue()) === user;
    if (!hinted) await userField.fill(user);
    await passwordField.fill(credentials.password);
    await page.locator('button[type="submit"]').first().click();

    const backAtPortal = (url) => url.href.startsWith(portalBase) && !url.pathname.startsWith('/signin-oidc');
    if (withTotp) {
      await page.waitForURL((url) => backAtPortal(url) || url.pathname.toLowerCase().includes('totp'));
      if (backAtPortal(new URL(page.url()))) throw new Error(`WSO2 did not ask ${user} for a one-time code`);
      await onStep(page, 'wso2-totp');
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

  return { readDemoPage, signIn, signOut };
}

module.exports = { demoSession, expectSuccess, latestMonth, totp };
