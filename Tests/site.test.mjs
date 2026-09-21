import assert from 'node:assert/strict';
import { test } from 'node:test';
import { installerUrl, releasesUrl, resolveInstaller, updateDownload } from '../site/download.mjs';

const url = 'https://github.com/tzachbon/win-mix/releases/download/v1.0.3/win-mix-Setup-1.0.3-x64.exe';
const release = () => ({ draft: false, prerelease: false, tag_name: 'v1.0.3', assets: [
  { name: 'win-mix-Setup-1.0.3-x64.exe', browser_download_url: url },
  { name: 'win-mix-Setup-1.0.3-x64.exe.sha256', browser_download_url: `${url}.sha256` },
] });

test('selects exactly the stable x64 installer, not its checksum', () => {
  assert.equal(installerUrl(release()), url);
});

test('rejects missing, malformed, draft, prerelease and ambiguous releases', () => {
  for (const value of [null, {}, { ...release(), draft: true },
    { ...release(), prerelease: true }, { ...release(), assets: null },
    { ...release(), assets: [] }, { ...release(), assets: [null, {}] },
    { ...release(), tag_name: 'v1.0.4' }, { ...release(), tag_name: 'v1.0.3-beta' },
    { ...release(), assets: [release().assets[0], release().assets[0]] }]) {
    assert.equal(installerUrl(value), null);
  }
});

test('rejects unexpected download origins, paths and suffixes', () => {
  for (const invalid of [url.replace('https:', 'http:'), url.replace('github.com', 'evil.test'),
    url.replace('github.com', 'github.com.evil.test'), url.replace('/tzachbon/', '/other/'),
    url.replace('/v1.0.3/', '/v1.0.2/'), `${url}?redirect=evil`, `${url}#fragment`,
    url.replace('github.com', 'user@github.com'), 'not a URL', null]) {
    const value = release();
    value.assets[0].browser_download_url = invalid;
    assert.equal(installerUrl(value), null);
  }
});

test('fetches once without credentials and upgrades the link', async () => {
  let calls = 0;
  const link = { href: releasesUrl };
  const label = { textContent: 'Install for Windows' };
  await updateDownload(link, label, async (endpoint, options) => {
    calls++;
    assert.equal(endpoint, 'https://api.github.com/repos/tzachbon/win-mix/releases/latest');
    assert.equal(options.credentials, 'omit');
    assert.ok(options.signal instanceof AbortSignal);
    return { ok: true, json: async () => release() };
  });
  assert.equal(calls, 1);
  assert.equal(link.href, url);
  assert.equal(label.textContent, 'Install for Windows');
});

test('HTTP failures, malformed JSON and network errors keep a usable fallback', async () => {
  for (const fetcher of [async () => ({ ok: false, status: 403 }),
    async () => ({ ok: false, status: 404 }),
    async () => ({ ok: true, json: async () => { throw new SyntaxError(); } }),
    async () => ({ ok: true, json: async () => ({}) }),
    async () => { throw new TypeError('Network failed'); }]) {
    const link = { href: releasesUrl }, label = {};
    await updateDownload(link, label, fetcher);
    assert.equal(link.href, releasesUrl);
    assert.equal(label.textContent, 'View downloads');
  }
});

test('aborted request returns fallback without retrying', async () => {
  const controller = new AbortController();
  const pending = resolveInstaller(async (_, { signal }) => new Promise((resolve, reject) => {
    signal.addEventListener('abort', () => reject(signal.reason), { once: true });
  }), controller.signal);
  controller.abort(new DOMException('Timed out', 'TimeoutError'));
  assert.equal(await pending, null);
});
