const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const policy = require('./release-policy.cjs');

function version(tag) {
  if (typeof tag !== 'string' || !/^v\d+\.\d+\.\d+$/.test(tag)) throw Error('Invalid release tag.');
  return policy.parseStableVersion(tag.slice(1));
}
function compare(a, b) {
  return policy.compareVersions(version(a), version(b));
}
async function enabled(github, repo) {
  try {
    return (await github.rest.actions.getRepoVariable({ ...repo, name: 'RELEASE_AUTOMATION_ENABLED' })).data.value === 'true';
  } catch (error) {
    if (error.status === 404) return false;
    throw error;
  }
}
async function tagSha(github, repo, tag) {
  version(tag);
  let object = (await github.rest.git.getRef({ ...repo, ref: `tags/${tag}` })).data.object;
  for (let i = 0; object.type === 'tag' && i < 5; i++)
    object = (await github.rest.git.getTag({ ...repo, tag_sha: object.sha })).data.object;
  if (object.type !== 'commit' || !/^[a-f0-9]{40}$/.test(object.sha)) throw Error('Tag does not resolve to a commit.');
  return object.sha;
}
async function draftState({ github, context, core, env = process.env }) {
  const repo = context.repo;
  const releases = await github.paginate(github.rest.repos.listReleases, { ...repo, per_page: 100 });
  const drafts = releases.filter(r => r.draft);
  if (!drafts.length) return null;
  if (drafts.length !== 1) throw Error('Resolve existing draft releases before continuing.');
  const draft = drafts[0];
  version(draft.tag_name);
  if (draft.prerelease) throw Error('Prerelease drafts cannot be published by this workflow.');
  for (const release of releases.filter(r => !r.draft && !r.prerelease)) {
    if (compare(draft.tag_name, release.tag_name) <= 0) throw Error('Draft is not newer than published releases.');
  }
  const sha = await tagSha(github, repo, draft.tag_name);
  const ancestry = (await github.rest.repos.compareCommitsWithBasehead({ ...repo, basehead: `${sha}...main` })).data;
  if (!['ahead', 'identical'].includes(ancestry.status)) throw Error('Release commit is not an ancestor of main.');
  const file = (await github.rest.repos.getContent({ ...repo, path: 'VERSION', ref: sha })).data;
  if (file.type !== 'file' || file.encoding !== 'base64' || Buffer.from(file.content, 'base64').toString('utf8').trim() !== draft.tag_name.slice(1))
    throw Error('Tagged VERSION does not match release.');
  await require('./release-policy.cjs').assertReleaseCommit({github, context, env, sha, version: draft.tag_name.slice(1)});
  const state = { id: draft.id, tag: draft.tag_name, version: draft.tag_name.slice(1), sha };
  if (core) for (const [key, value] of Object.entries(state)) core.setOutput(key, String(value));
  return state;
}
async function releaseGate({github, context, core, env = process.env}) {
  const active = await enabled(github, context.repo);
  if (active && await draftState({github, context, env})) return;
  const commits = (await github.rest.repos.listCommits({...context.repo, sha: 'main', path: 'VERSION', per_page: 1})).data;
  if (commits.length !== 1) throw Error('Cannot resolve version commit.');
  const file = (await github.rest.repos.getContent({...context.repo, path: 'VERSION', ref: commits[0].sha})).data;
  const currentVersion = Buffer.from(file.content, 'base64').toString('utf8').trim();
  try {
    const release = (await github.rest.repos.getReleaseByTag({...context.repo, tag: `v${currentVersion}`})).data;
    if (!release.draft) {
      if (active) core.info('Current version is already published.');
      else core.notice('Paused: PR preparation only. No merge, tag, draft or publication.');
      return;
    }
  } catch (error) { if (error.status !== 404) throw error; }
  await policy.assertReleaseCommit({github, context, env, sha: commits[0].sha, version: currentVersion});
  if (!active) {
    core.warning(`Release ${currentVersion} was merged but has no published release. Publish v${currentVersion} before another release PR can be prepared.`);
    return;
  }
  core.setOutput('create', String(await enabled(github, context.repo)));
}
function localAssets(directory, tag) {
  version(tag);
  const exe = `win-mix-Setup-${tag.slice(1)}-x64.exe`;
  const names = [exe, `${exe}.sha256`];
  if (fs.readdirSync(directory).sort().join('\n') !== [...names].sort().join('\n')) throw Error('Unexpected installer artifacts.');
  const assets = names.map(name => {
    const file = path.join(directory, name), stat = fs.lstatSync(file);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size <= 0 || stat.size > 256 * 1024 * 1024) throw Error('Invalid installer artifact.');
    const data = fs.readFileSync(file);
    return { name, data, size: data.length, digest: `sha256:${crypto.createHash('sha256').update(data).digest('hex')}` };
  });
  if (assets[1].data.toString('utf8') !== `${assets[0].digest.slice(7)}  ${exe}\n`) throw Error('Installer checksum sidecar mismatch.');
  return assets;
}
function verifyAsset(remote, local) {
  if (remote.state !== 'uploaded' || remote.size !== local.size || remote.digest !== local.digest)
    throw Error(`Existing asset conflicts with verified bytes: ${local.name}`);
}
async function publish({ github, context, core, directory, expected, env = process.env }) {
  const repo = context.repo;
  const state = await draftState({ github, context, env });
  if (!state || ['id', 'tag', 'sha'].some(key => String(state[key]) !== String(expected[key]))) throw Error('Draft identity changed.');
  const assets = localAssets(directory, state.tag);
  let remote = await github.paginate(github.rest.repos.listReleaseAssets, { ...repo, release_id: state.id, per_page: 100 });
  if (remote.some(a => !assets.some(b => b.name === a.name)) || new Set(remote.map(a => a.name)).size !== remote.length)
    throw Error('Unexpected or duplicate draft assets.');
  const draft = (await github.rest.repos.getRelease({ ...repo, release_id: state.id })).data;
  if (!draft.draft) throw Error('Published assets are immutable.');
  for (const asset of assets) {
    const existing = remote.find(a => a.name === asset.name);
    if (existing) { verifyAsset(existing, asset); continue; }
    if (!await enabled(github, repo)) throw Error('Release automation is paused.');
    const uploaded = (await github.rest.repos.uploadReleaseAsset({ ...repo, release_id: state.id,
      url: draft.upload_url, name: asset.name, data: asset.data,
      headers: { 'content-type': 'application/octet-stream', 'content-length': asset.size } })).data;
    verifyAsset(uploaded, asset);
  }
  remote = await github.paginate(github.rest.repos.listReleaseAssets, { ...repo, release_id: state.id, per_page: 100 });
  if (remote.length !== 2) throw Error('Release assets are incomplete.');
  for (const asset of assets) {
    const matches = remote.filter(a => a.name === asset.name);
    if (matches.length !== 1) throw Error('Release asset is missing or duplicated.');
    verifyAsset(matches[0], asset);
  }
  const current = await draftState({ github, context, env });
  if (!current || current.id !== state.id || current.sha !== state.sha) throw Error('Draft changed before publication.');
  if (!await enabled(github, repo)) throw Error('Release automation is paused.');
  await github.rest.repos.updateRelease({ ...repo, release_id: state.id, draft: false, prerelease: false, make_latest: 'true' });
  core?.info(`Published ${state.tag} from ${state.sha} with verified installer assets.`);
}
module.exports = { enabled, tagSha, draftState, releaseGate, localAssets, verifyAsset, publish };
