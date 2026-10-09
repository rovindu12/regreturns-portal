// Headless Chromium that trusts exactly the servers a script has verified itself, for the browser smoke tests.
//
// TLS stays verified: Node first connects to each server with its explicit CA (a PEM file, or the system store when
// the CA is 'system' or empty) and reads the server's public key; Chromium is then told to accept exactly those keys
// (--ignore-certificate-errors-spki-list), so it trusts the servers this script has already verified and nothing else.
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const tls = require('node:tls');
const { chromium } = require('playwright');

const timeout = 30_000;

/** Reads a required environment variable. */
function env(name, fallback) {
  const value = process.env[name] ?? fallback;
  if (value === undefined || value === '') throw new Error(`${name} is not set`);
  return value;
}

/** Connects to a server, verifying it against a CA file (or the system store), and returns its SPKI pin. */
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

/** Launches Chromium trusting only the given servers, each as [baseUrl, caFile]. */
async function launchPinned(servers) {
  const pins = [];
  for (const [baseUrl, caFile] of servers) pins.push(await verifiedKeyHash(baseUrl, caFile));
  return chromium.launch({ args: [`--ignore-certificate-errors-spki-list=${pins.join(',')}`] });
}

module.exports = { env, launchPinned, timeout, verifiedKeyHash };
