'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {test} = require('node:test');
const {enabled, draftState, localAssets, publish} = require('../.github/scripts/publish-release.cjs');

const policyEnv = {RELEASE_BOT_LOGIN: 'release-bot[bot]', RELEASE_BOT_USER_ID: '456', RELEASE_BOT_APP_ID: '1234'};

test('private App lookup failure blocks publication before asset writes', async t => {
  const local = artifacts(t);
  const fixture = fakeGitHub();
  fixture.github.rest.apps.getBySlug = async () => { throw Object.assign(Error('private App forbidden'), {status: 403}); };
  await assert.rejects(publish({...fixture, directory: local.directory, env: policyEnv}), /private App forbidden/);
  assert.equal(fixture.calls.uploads.length, 0);
  assert.equal(fixture.calls.updates.length, 0);
});

function artifacts(t, tag = 'v1.0.4') {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'win-mix-publish-test-'));
  t.after(() => fs.rmSync(directory, {recursive: true, force: true}));
  const exe = `win-mix-Setup-${tag.slice(1)}-x64.exe`;
  const data = Buffer.from('verified installer bytes');
  const hash = crypto.createHash('sha256').update(data).digest('hex');
  fs.writeFileSync(path.join(directory, exe), data);
  fs.writeFileSync(path.join(directory, `${exe}.sha256`), `${hash}  ${exe}\n`);
  return {directory, exe};
}

function remoteAsset(local, id = 1) {
  return {id, name: local.name, state: 'uploaded', size: local.size, digest: local.digest};
}

function fakeGitHub(options = {}) {
  const repo = {owner: 'owner', repo: 'repo'};
  const sha = options.sha || '4'.repeat(40);
  const headSha = '3'.repeat(40);
  const baseSha = '2'.repeat(40);
  const version = options.version || '1.0.4';
  const oldVersion = '1.0.3';
  const draft = {id: 7, tag_name: options.tag || `v${version}`, draft: true, prerelease: false, upload_url: 'https://uploads.invalid'};
  const releases = options.releases || [draft, {id: 6, tag_name: `v${oldVersion}`, draft: false, prerelease: false}];
  const assets = [...(options.assets || [])];
  const calls = {uploads: [], updates: []};
  const outputs = {};
  const pr = {
    number: 42,
    changed_files: 3,
    title: `chore(main): release ${version}`,
    merged_at: '2026-09-21T00:00:00Z',
    merge_commit_sha: sha,
    user: {id: 456, login: policyEnv.RELEASE_BOT_LOGIN, type: 'Bot'},
    performed_via_github_app: {id: Number(policyEnv.RELEASE_BOT_APP_ID)},
    base: {ref: 'main', sha: baseSha},
    head: {ref: 'release-please--branches--main', sha: headSha, repo: {full_name: 'owner/repo'}},
  };
  const textFile = text => ({data: {type: 'file', encoding: 'base64', content: Buffer.from(text).toString('base64')}});
  const github = {
    paginate: async (call, args) => (await call(args)).data,
    rest: {
      apps: {getBySlug: async ({app_slug}) => ({data: {id: 1234, slug: app_slug}})},
      actions: {
        getRepoVariable: async () => {
          if (options.missingVariable) throw Object.assign(Error('missing'), {status: 404});
          return {data: {name: 'RELEASE_AUTOMATION_ENABLED', value: options.variable ?? 'true'}};
        },
      },
      git: {
        getRef: async () => ({data: {object: {type: 'commit', sha}}}),
        getTag: async () => { throw Error('unexpected annotated tag'); },
      },
      pulls: {
        get: async () => ({data: pr}),
        listFiles: async ({page}) => ({data: page === 1 ? [
          {filename: '.release-please-manifest.json', status: 'modified'},
          {filename: 'CHANGELOG.md', status: 'modified'},
          {filename: 'VERSION', status: 'modified'},
        ] : []}),
      },
      repos: {
        listReleases: async () => ({data: releases}),
        listReleaseAssets: async () => ({data: assets}),
        listPullRequestsAssociatedWithCommit: async ({page}) => ({data: page === 1 ? [{number: pr.number}] : []}),
        compareCommitsWithBasehead: async () => ({data: {status: options.ancestry || 'ahead'}}),
        getContent: async ({path: filePath, ref}) => {
          if (filePath === 'VERSION') return textFile(`${ref === sha && options.taggedVersion ? options.taggedVersion : ref === baseSha ? oldVersion : version}\n`);
          if (filePath === '.release-please-manifest.json') return textFile(`${JSON.stringify({'.': ref === baseSha ? oldVersion : version})}\n`);
          if (filePath === 'CHANGELOG.md') return textFile(`# Changelog\n\n## ${version}\n`);
          throw Object.assign(Error('missing'), {status: 404});
        },
        getRelease: async () => ({data: options.releaseAtUpload || draft}),
        uploadReleaseAsset: async args => {
          calls.uploads.push(args);
          const uploaded = remoteAsset({
            name: args.name,
            size: args.data.length,
            digest: `sha256:${crypto.createHash('sha256').update(args.data).digest('hex')}`,
          }, assets.length + 1);
          assets.push(uploaded);
          return {data: uploaded};
        },
        updateRelease: async args => {
          calls.updates.push(args);
          draft.draft = false;
          return {data: draft};
        },
      },
    },
  };
  return {
    github, context: {repo}, core: {setOutput: (key, value) => outputs[key] = value, info() {}},
    env: policyEnv, draft, assets, calls, outputs, sha,
    expected: {id: draft.id, tag: draft.tag_name, sha},
  };
}

test('local assets require the exact files and checksum bytes', t => {
  const set = artifacts(t);
  const found = localAssets(set.directory, 'v1.0.4');
  assert.deepEqual(found.map(asset => asset.name), [set.exe, `${set.exe}.sha256`]);
  assert.match(found[0].digest, /^sha256:[0-9a-f]{64}$/);

  fs.writeFileSync(path.join(set.directory, `${set.exe}.sha256`), 'wrong checksum\n');
  assert.throws(() => localAssets(set.directory, 'v1.0.4'), /checksum sidecar mismatch/);
  fs.writeFileSync(path.join(set.directory, `${set.exe}.sha256`), `${found[0].digest.slice(7)}  ${set.exe}\n`);
  fs.writeFileSync(path.join(set.directory, 'unexpected.txt'), 'unexpected');
  assert.throws(() => localAssets(set.directory, 'v1.0.4'), /Unexpected installer artifacts/);
});

test('a false or missing repository flag pauses automation', async () => {
  assert.equal(await enabled(fakeGitHub({variable: 'false'}).github, {owner: 'owner', repo: 'repo'}), false);
  assert.equal(await enabled(fakeGitHub({missingVariable: true}).github, {owner: 'owner', repo: 'repo'}), false);
});

test('publish stops before upload when the flag is missing', async t => {
  const set = artifacts(t);
  const fake = fakeGitHub({missingVariable: true});
  await assert.rejects(publish({...fake, directory: set.directory}), /Release automation is paused/);
  assert.equal(fake.calls.uploads.length, 0);
  assert.equal(fake.calls.updates.length, 0);
});

test('publish resumes a partial upload without replacing verified bytes', async t => {
  const set = artifacts(t);
  const local = localAssets(set.directory, 'v1.0.4');
  const fake = fakeGitHub({assets: [remoteAsset(local[0])]});
  await publish({...fake, directory: set.directory});
  assert.deepEqual(fake.calls.uploads.map(call => call.name), [`${set.exe}.sha256`]);
  assert.equal(fake.calls.updates.length, 1);
  assert.equal(fake.calls.updates[0].draft, false);
});

test('publish rejects conflicting existing assets', async t => {
  const set = artifacts(t);
  const local = localAssets(set.directory, 'v1.0.4');
  const conflict = {...remoteAsset(local[0]), digest: `sha256:${'0'.repeat(64)}`};
  const fake = fakeGitHub({assets: [conflict]});
  await assert.rejects(publish({...fake, directory: set.directory}), /Existing asset conflicts/);
  assert.equal(fake.calls.uploads.length, 0);
  assert.equal(fake.calls.updates.length, 0);
});

test('publish rejects unexpected draft assets', async t => {
  const set = artifacts(t);
  const fake = fakeGitHub({assets: [{id: 1, name: 'unexpected.zip', state: 'uploaded', size: 1, digest: `sha256:${'0'.repeat(64)}`}]});
  await assert.rejects(publish({...fake, directory: set.directory}), /Unexpected or duplicate draft assets/);
  assert.equal(fake.calls.uploads.length, 0);
  assert.equal(fake.calls.updates.length, 0);
});

test('draft state returns the verified identity and version', async () => {
  const fake = fakeGitHub();
  const state = await draftState(fake);
  assert.deepEqual(state, {id: 7, tag: 'v1.0.4', version: '1.0.4', sha: fake.sha});
  assert.deepEqual(fake.outputs, {id: '7', tag: 'v1.0.4', version: '1.0.4', sha: fake.sha});
});

test('draft state rejects version and ancestry mismatches', async t => {
  await t.test('tagged VERSION', async () => {
    await assert.rejects(draftState(fakeGitHub({taggedVersion: '1.0.5'})), /Tagged VERSION does not match release/);
  });
  await t.test('main ancestry', async () => {
    await assert.rejects(draftState(fakeGitHub({ancestry: 'diverged'})), /Release commit is not an ancestor of main/);
  });
});

test('publish rejects a changed draft identity', async t => {
  const set = artifacts(t);
  const fake = fakeGitHub();
  await assert.rejects(publish({...fake, directory: set.directory, expected: {...fake.expected, id: 8}}), /Draft identity changed/);
  assert.equal(fake.calls.uploads.length, 0);
  assert.equal(fake.calls.updates.length, 0);
});

test('publish never overwrites a release that is already published', async t => {
  const set = artifacts(t);
  const fake = fakeGitHub({releaseAtUpload: {id: 7, draft: false}});
  await assert.rejects(publish({...fake, directory: set.directory}), /Published assets are immutable/);
  assert.equal(fake.calls.uploads.length, 0);
  assert.equal(fake.calls.updates.length, 0);
});
