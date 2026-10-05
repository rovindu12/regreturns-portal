// Signs in to the portal through WSO2 in headless Chromium, checks the landing page, then signs out.
// Run by scripts/smoke-wso2.sh --browser. Needs Playwright (NODE_PATH pointing at a global install is enough).
//
// TLS stays verified: Node first connects to WSO2 and the portal with the explicit CAs (WSO2_CA, APP_CA or the system
// store when APP_CA=system) and reads each server's public key; Chromium is then told to accept exactly those keys
// (--ignore-certificate-errors-spki-list), so it trusts the two servers this script has already verified and nothing else.
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const tls = require('node:tls');
const { chromium } = require('playwright');

const env = (name, fallback) => {
  const value = process.env[name] ?? fallback;
  if (value === undefined || value === '') throw new Error(`${name} is not set`);
  return value;
};

const portalBase = env('PORTAL_BASE').replace(/\/$/, '');
const wso2Base = env('WSO2_BASE').replace(/\/$/, '');
const user = env('LOGIN_USER');
const password = env('LOGIN_PASSWORD');
const expectText = env('EXPECT_TEXT');
const timeout = 30_000;

function verifiedKeyHash(baseUrl, caFile) {
  const url = new URL(baseUrl);
  const options = { host: url.hostname, port: Number(url.port || 443), servername: url.hostname };
  if (caFile && caFile !== 'system') options.ca = fs.readFileSync(caFile);
  return new Promise((resolve, reject) => {
    const socket = tls.connect(options, () => {
      const certificate = new crypto.X509Certificate(socket.getPeerCertificate().raw);
      socket.end();
      const spki = certificate.publicKey.export({ type: 'spki', format: 'der' });
      resolve(crypto.createHash('sha256').update(spki).digest('base64'));
    });
    socket.setTimeout(timeout, () => socket.destroy(new Error(`timed out connecting to ${baseUrl}`)));
    socket.on('error', (error) => reject(new Error(`${baseUrl}: ${error.message}`)));
  });
}

async function main() {
  const pins = [await verifiedKeyHash(wso2Base, env('WSO2_CA')), await verifiedKeyHash(portalBase, process.env.APP_CA)];
  const browser = await chromium.launch({ args: [`--ignore-certificate-errors-spki-list=${pins.join(',')}`] });
  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(timeout);

    await page.goto(`${portalBase}/`);
    await page.getByRole('link', { name: /sign in/i }).first().click();

    await page.waitForURL((url) => url.href.startsWith(wso2Base));
    const userField = page.locator('#usernameUserInput, input[name="username"]').first();
    await userField.fill(user);
    await page.locator('#password, input[name="password"]').first().fill(password);
    await page.locator('button[type="submit"]').first().click();

    await page.waitForURL((url) => url.href.startsWith(portalBase) && !url.pathname.startsWith('/signin-oidc'));
    console.log(`    signed in as ${user}, returned to ${new URL(page.url()).pathname}`);

    const dashboard = await page.goto(`${portalBase}${env('EXPECT_PATH')}`);
    const body = await page.locator('main').innerText();
    if (dashboard.status() !== 200 || !body.includes(expectText)) {
      throw new Error(`${env('EXPECT_PATH')} returned ${dashboard.status()} without ${expectText}`);
    }
    console.log(`    ${env('EXPECT_PATH')} shows the workspace for ${expectText}`);

    await page.goto(`${portalBase}${env('DENIED_PATH')}`);
    if (!new URL(page.url()).pathname.toLowerCase().includes('accessdenied')) {
      throw new Error(`${env('DENIED_PATH')} did not send ${user} to the access-denied page`);
    }
    console.log(`    ${env('DENIED_PATH')} is refused`);

    await page.getByRole('button', { name: /sign out/i }).first().click();
    await page.waitForURL((url) => url.href.startsWith(portalBase) && !url.pathname.startsWith('/signout-callback-oidc'));
    if (await page.getByRole('button', { name: /sign out/i }).count() > 0) throw new Error('still signed in after sign-out');
    console.log(`    signed out, landed on ${new URL(page.url()).pathname}`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => {
  console.error(`    ${error.message}`);
  process.exit(1);
});
