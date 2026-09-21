export const releasesUrl = 'https://github.com/tzachbon/win-mix/releases/latest';
const apiUrl = 'https://api.github.com/repos/tzachbon/win-mix/releases/latest';

export function installerUrl(release) {
  if (release?.draft !== false || release?.prerelease !== false ||
      !/^v\d+\.\d+\.\d+$/.test(release.tag_name) || !Array.isArray(release.assets)) return null;
  const assets = release.assets.filter(asset =>
    /^win-mix-Setup-\d+\.\d+\.\d+-x64\.exe$/.test(asset?.name));
  if (assets.length !== 1) return null;
  const asset = assets[0];
  if (asset.name !== `win-mix-Setup-${release.tag_name.slice(1)}-x64.exe`) return null;
  const expected = `https://github.com/tzachbon/win-mix/releases/download/${release.tag_name}/${asset.name}`;
  return asset.browser_download_url === expected ? expected : null;
}

export async function resolveInstaller(fetchRelease = fetch, signal = AbortSignal.timeout(5000)) {
  try {
    const response = await fetchRelease(apiUrl, {
      signal,
      credentials: 'omit',
      headers: { Accept: 'application/vnd.github+json' },
    });
    return response.ok ? installerUrl(await response.json()) : null;
  } catch {
    return null;
  }
}

export async function updateDownload(link, label, fetchRelease = fetch, signal) {
  const url = await resolveInstaller(fetchRelease, signal);
  link.href = url ?? releasesUrl;
  label.textContent = url ? 'Install for Windows' : 'View downloads';
}

if (typeof document !== 'undefined') {
  const link = document.getElementById('download');
  const label = document.getElementById('download-label');
  if (link && label) void updateDownload(link, label);
}
