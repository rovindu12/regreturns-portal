// Signs in to the portal through WSO2 in headless Chromium, checks the landing page, then signs out.
// Run by scripts/smoke-wso2.sh --browser. Needs Playwright (NODE_PATH pointing at a global install is enough).
//
// TLS stays verified: WSO2 against WSO2_CA, the portal against APP_CA (or the system store when APP_CA=system); see
// pinned-browser.cjs.
'use strict';

const { env, launchPinned, timeout } = require('./pinned-browser.cjs');

const portalBase = env('PORTAL_BASE').replace(/\/$/, '');
const wso2Base = env('WSO2_BASE').replace(/\/$/, '');
const user = env('LOGIN_USER');
const password = env('LOGIN_PASSWORD');
const expectText = env('EXPECT_TEXT');

async function main() {
  const browser = await launchPinned([[wso2Base, env('WSO2_CA')], [portalBase, process.env.APP_CA]]);
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
