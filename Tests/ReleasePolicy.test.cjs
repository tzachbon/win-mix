'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const policy = require('../.github/scripts/release-policy.cjs');

const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);
const MERGE = 'c'.repeat(40);
const RELEASE = 'd'.repeat(40);
const START = 'e'.repeat(40);
const MAIN = 'f'.repeat(40);
const env = {RELEASE_BOT_LOGIN: 'release-bot[bot]', RELEASE_BOT_USER_ID: '456', RELEASE_BOT_APP_ID: '123', RELEASE_POLICY_START_SHA: START};
const context = {repo: {owner: 'owner', repo: 'repo'}, payload: {pull_request: {number: 7}}};

function encoded(text, type = 'file') {
  return {data: {type, encoding: 'base64', content: Buffer.from(text).toString('base64')}};
}

function releasePull(overrides = {}) {
  return {
    number: 7,
    title: 'chore(main): release 1.2.4',
    user: {id: 456, login: env.RELEASE_BOT_LOGIN, type: 'Bot'},
    performed_via_github_app: {id: 123},
    base: {ref: 'main', sha: BASE},
    head: {ref: policy.RELEASE_BRANCH, sha: HEAD, repo: {full_name: 'owner/repo'}},
    merge_commit_sha: MERGE,
    changed_files: 3,
    state: 'open',
    draft: false,
    mergeable: true,
    ...overrides,
  };
}

function releaseFiles(extra = []) {
  return [
    {filename: 'VERSION', status: 'modified'},
    {filename: 'CHANGELOG.md', status: 'modified'},
    {filename: '.release-please-manifest.json', status: 'modified'},
    ...extra,
  ];
}

function metadataFixture(options = {}) {
  const files = options.files || releaseFiles();
  const pulls = options.pulls || [releasePull({changed_files: files.length})];
  let getIndex = 0;
  const mergeCalls = [];
  const github = {rest: {
    pulls: {
      get: async () => ({data: pulls[Math.min(getIndex++, pulls.length - 1)]}),
      listFiles: options.listFiles || (async () => ({data: files})),
      list: async () => ({data: options.candidates === undefined ? [{number: 7}] : options.candidates}),
      merge: async args => { mergeCalls.push(args); return {data: {merged: true, sha: RELEASE}}; },
    },
    repos: {
      getContent: options.getContent || (async ({path, ref}) => {
        if (ref === HEAD && path === 'VERSION') return encoded(options.newVersion || '1.2.4\n');
        if (ref === HEAD && path === '.release-please-manifest.json') return encoded(JSON.stringify({'.': options.newVersion || '1.2.4'}));
        if (ref === HEAD && path === 'CHANGELOG.md') return encoded('# Changelog\n');
        if (ref === BASE && path === 'VERSION') {
          if (options.baseVersionMissing) throw Object.assign(new Error('not found'), {status: 404});
          return encoded(options.oldVersion || '1.2.3\n');
        }
        if (ref === BASE && path === '.release-please-manifest.json') return encoded(JSON.stringify({'.': options.oldVersion || '1.2.3'}));
        if (ref === BASE && path === 'Directory.Build.props') return encoded(`<Project><PropertyGroup><Version>${options.oldVersion || '1.2.3'}</Version></PropertyGroup></Project>`);
        throw new Error(`Unexpected content read: ${ref}:${path}`);
      }),
      listReleases: async () => ({data: options.releases || []}),
      getBranch: async () => ({data: {commit: {sha: options.mainSha || BASE}}}),
      compareCommitsWithBasehead: async () => ({data: {status: options.ancestry || 'ahead'}}),
    },
    actions: {
      getRepoVariable: options.getRepoVariable || (async () => ({data: {name: 'RELEASE_AUTOMATION_ENABLED', value: 'true'}})),
    },
    apps: {getBySlug: async ({app_slug}) => ({data: {id: options.appId || 123, slug: app_slug}})},
  }};
  return {github, mergeCalls};
}

function successfulChecks(overrides = {}) {
  return policy.REQUIRED_CHECKS.map((name, index) => ({
    id: index + 1, name, head_sha: HEAD, app: {slug: 'github-actions'},
    status: 'completed', conclusion: 'success', ...overrides,
  }));
}

function autoContext() {
  return {repo: context.repo, eventName: 'workflow_run', payload: {workflow_run: {
    name: 'PR policy', event: 'pull_request', conclusion: 'success',
    head_sha: HEAD, head_branch: policy.RELEASE_BRANCH,
  }}};
}

function readClient(checks = successfulChecks(), total = checks.length) {
  return {rest: {checks: {listForRef: async () => ({data: {total_count: total, check_runs: checks}})}}};
}

test('strict pull request titles reject malformed input and accept the corrected form', () => {
  assert.throws(() => policy.parsePullRequestTitle('feature(ui): add mute'), /allowed conventional-commit/);
  assert.throws(() => policy.parsePullRequestTitle('feat(ui): add mute '), /Examples:/);
  assert.throws(() => policy.parsePullRequestTitle('feat(ui): add\nmute'), /Examples:/);
  assert.deepEqual(policy.parsePullRequestTitle('feat(ui)!: add mute'), {
    type: 'feat', scope: 'ui', breaking: true, subject: 'add mute',
  });
});

test('stable versions enforce numeric bounds and formatting', () => {
  assert.equal(policy.parseStableVersion('0.65535.7\n').text, '0.65535.7');
  for (const version of ['01.2.3', '1.2', '1.2.65536', '1.2.3-beta', '1.2.3\n4.5.6']) {
    assert.throws(() => policy.parseStableVersion(version));
  }
  assert.equal(policy.compareVersions('1.2.4', '1.2.3'), 1);
});

test('release metadata accepts the VERSION bootstrap only when manifest and props agree', async () => {
  const {github} = metadataFixture({baseVersionMissing: true});
  const result = await policy.validateReleaseMetadata({github, context, env});
  assert.equal(result.newVersion, '1.2.4');
  assert.equal(result.bump, 'patch');
  assert.equal(result.automatic, true);
});

test('initial metadata migration may add matching VERSION and manifest with other setup files', async () => {
  const files = [
    {filename: 'VERSION', status: 'added'},
    {filename: '.release-please-manifest.json', status: 'added'},
    {filename: '.github/workflows/pr-policy.yml', status: 'added'},
  ];
  const fixture = metadataFixture({files, newVersion: '1.2.3', pulls: [releasePull({
    title: 'ci: establish release policy', changed_files: files.length,
  })]});
  const originalGet = fixture.github.rest.repos.getContent;
  fixture.github.rest.repos.getContent = async args => {
    if (args.ref === BASE && ['VERSION', '.release-please-manifest.json'].includes(args.path)) {
      throw Object.assign(new Error('not found'), {status: 404});
    }
    return originalGet(args);
  };
  const result = await policy.validateReleaseMetadata({github: fixture.github, context, env});
  assert.equal(result.kind, 'bootstrap');
  assert.equal(result.version, '1.2.3');
});

test('regular pull requests pass unless they edit protected release metadata', async () => {
  const regular = metadataFixture({files: [{filename: 'README.md', status: 'modified'}]});
  assert.equal((await policy.validateReleaseMetadata({github: regular.github, context, env})).kind, 'regular');

  const mixed = metadataFixture({files: releaseFiles([{filename: 'README.md', status: 'modified'}])});
  await assert.rejects(policy.validateReleaseMetadata({github: mixed.github, context, env}), /exactly VERSION/);
});

test('release metadata rejects a fake bot and non-regular files', async () => {
  const fakeBot = metadataFixture({pulls: [releasePull({user: {login: 'attacker[bot]', type: 'Bot'}})]});
  await assert.rejects(policy.validateReleaseMetadata({github: fakeBot.github, context, env}), /configured bot/);

  const normal = metadataFixture();
  normal.github.rest.repos.getContent = async ({path, ref}) => {
    if (path === 'CHANGELOG.md' && ref === HEAD) return encoded('target', 'symlink');
    return metadataFixture().github.rest.repos.getContent({path, ref});
  };
  await assert.rejects(policy.validateReleaseMetadata({github: normal.github, context, env}), /CHANGELOG.md must be a regular file/);
});

test('privileged merge rejects a bot login whose App ID does not match', async () => {
  const fixture = metadataFixture({appId: 999});
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /App ID/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('read-only metadata validates the pinned bot without a private App lookup', async () => {
  const fixture = metadataFixture();
  fixture.github.rest.apps.getBySlug = async () => { throw Object.assign(Error('private App forbidden'), {status: 403}); };
  assert.equal((await policy.validateReleaseMetadata({github: fixture.github, context, env})).kind, 'release');
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /private App forbidden/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('release metadata rejects missing, malformed or mismatched bot user IDs', async () => {
  for (const userId of [undefined, '', '0', '-1', '0456', '456.0', '9007199254740992']) {
    const fixture = metadataFixture();
    await assert.rejects(policy.validateReleaseMetadata({github: fixture.github, context, env: {...env, RELEASE_BOT_USER_ID: userId}}), /identity environment/);
  }
  for (const user of [{id: 999}, {id: undefined}, {id: '456'}, {type: 'User'}, {login: 'other[bot]'}]) {
    const fixture = metadataFixture({pulls: [releasePull({user: {...releasePull().user, ...user}})]});
    await assert.rejects(policy.validateReleaseMetadata({github: fixture.github, context, env}), /configured bot/);
  }
});

test('release metadata fails closed when changed_files does not match the complete list', async () => {
  const fixture = metadataFixture({pulls: [releasePull({changed_files: 4})]});
  await assert.rejects(policy.validateReleaseMetadata({github: fixture.github, context, env}), /file list is incomplete/);
});

test('major release metadata is valid but never automatic', async () => {
  const pr = releasePull({title: 'chore(main): release 2.0.0'});
  const {github} = metadataFixture({pulls: [pr], newVersion: '2.0.0'});
  const result = await policy.validateReleaseMetadata({github, context, env});
  assert.equal(result.bump, 'major');
  assert.equal(result.automatic, false);
});

test('metadata pagination failures fail closed', async () => {
  const files = Array.from({length: 100}, (_, index) => ({filename: `file-${index}`, status: 'modified'}));
  const fixture = metadataFixture({listFiles: async ({page}) => {
    if (page === 1) return {data: files};
    throw new Error('page two unavailable');
  }});
  await assert.rejects(policy.validateReleaseMetadata({github: fixture.github, context, env}), /page two unavailable/);
});

function preflightFixture(message, prOverrides = {}, associated = [{number: 9}]) {
  const pr = {
    number: 9, title: 'fix(audio): keep endpoint stable', merged_at: '2026-09-21T00:00:00Z',
    merge_commit_sha: MAIN, base: {ref: 'main'}, ...prOverrides,
  };
  return {rest: {
    repos: {
      listReleases: async () => ({data: [{tag_name: 'v1.2.3', draft: false, prerelease: false, published_at: '2026-09-20T00:00:00Z'}]}),
      compareCommitsWithBasehead: async ({basehead}) => basehead === `${START}...${RELEASE}`
        ? {data: {status: 'ahead', total_commits: 1, commits: []}}
        : {data: {status: 'ahead', total_commits: 1, commits: [{sha: MAIN, commit: {message}}]}},
      listPullRequestsAssociatedWithCommit: async () => ({data: associated}),
    },
    git: {
      getRef: async () => ({data: {object: {type: 'commit', sha: RELEASE}}}),
      getTag: async () => { throw new Error('not annotated'); },
    },
    pulls: {get: async () => ({data: pr})},
  }};
}

test('main preflight rejects post-merge title intent changes', async () => {
  const github = preflightFixture('feat(audio): keep endpoint stable (#9)');
  await assert.rejects(policy.validateMainPreflight({github, context, env, headSha: MAIN}), /does not match PR #9/);
});

test('main preflight rejects hidden release footers', async () => {
  const github = preflightFixture('fix(audio): keep endpoint stable (#9)\n\nRelease-As: 9.0.0');
  await assert.rejects(policy.validateMainPreflight({github, context, env, headSha: MAIN}), /forbidden release override/);
});

test('main preflight allows only blank body lines and co-author trailers', async () => {
  const github = preflightFixture('fix(audio): keep endpoint stable (#9)\n\nCo-authored-by: Dev <dev@example.com>');
  const result = await policy.validateMainPreflight({github, context, env, headSha: MAIN});
  assert.deepEqual(result.commits, [MAIN]);
  assert.equal(result.boundarySha, RELEASE);
});

test('main preflight permits subject and scope edits that preserve type and breaking intent', async () => {
  const github = preflightFixture('fix: stabilize endpoint (#9)');
  const result = await policy.validateMainPreflight({github, context, env, headSha: MAIN});
  assert.deepEqual(result.commits, [MAIN]);
});

test('main preflight rejects a release directive hidden in the subject', async () => {
  const github = preflightFixture('fix: ignore Release-As: 9.0.0 (#9)');
  await assert.rejects(policy.validateMainPreflight({github, context, env, headSha: MAIN}), /forbidden release override/);
});

test('main preflight rejects ambiguous associated pull requests', async () => {
  const github = preflightFixture('fix(audio): keep endpoint stable', {}, [{number: 9}, {number: 10}]);
  await assert.rejects(policy.validateMainPreflight({github, context, env, headSha: MAIN}), /exactly one associated/);
});

test('failed required checks stop auto-merge', async () => {
  const fixture = metadataFixture();
  const checks = successfulChecks();
  checks[1] = {...checks[1], conclusion: 'failure'};
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(checks), context: autoContext(), env}), /not a current successful/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('auto-merge cleanly ignores runs when no release pull request exists', async () => {
  const fixture = metadataFixture({candidates: []});
  const result = await policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env});
  assert.deepEqual(result, {eligible: false, reason: 'no-release-pr'});
  assert.equal(fixture.mergeCalls.length, 0);
});

test('major releases are reported for manual merge without reading checks or merging', async () => {
  const major = releasePull({title: 'chore(main): release 2.0.0'});
  const fixture = metadataFixture({pulls: [major], newVersion: '2.0.0'});
  const notices = [];
  const result = await policy.autoMergeRelease({
    github: fixture.github,
    readGithub: {rest: {checks: {listForRef: async () => { throw new Error('checks must not be read'); }}}},
    context: autoContext(), env, core: {notice: message => notices.push(message)},
  });
  assert.deepEqual(result, {eligible: false, reason: 'major-release', number: 7});
  assert.equal(notices.length, 1);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('incomplete check pagination stops auto-merge', async () => {
  const fixture = metadataFixture();
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(successfulChecks(), 4), context: autoContext(), env}), /ended before its declared total/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('missing automation flag reports a paused would-merge without merging', async () => {
  const fixture = metadataFixture({getRepoVariable: async () => { throw Object.assign(new Error('missing'), {status: 404}); }});
  const notices = [];
  const result = await policy.autoMergeRelease({
    github: fixture.github, readGithub: readClient(), context: autoContext(), env,
    core: {notice: message => notices.push(message)},
  });
  assert.deepEqual(result, {paused: true, wouldMerge: {number: 7, headSha: HEAD}});
  assert.equal(fixture.mergeCalls.length, 0);
  assert.equal(notices.length, 1);
});

test('a changed release head fails before merge', async () => {
  const changed = releasePull({head: {...releasePull().head, sha: '9'.repeat(40)}});
  const fixture = metadataFixture({pulls: [releasePull(), releasePull(), changed]});
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /changed after validation/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('paused automation never reports a conflicting pull request as would-merge', async () => {
  const fixture = metadataFixture({
    pulls: [releasePull({mergeable: false})],
    getRepoVariable: async () => { throw Object.assign(new Error('missing'), {status: 404}); },
  });
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /not currently mergeable/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('paused automation rejects a release pull request based behind current main', async () => {
  const fixture = metadataFixture({
    mainSha: '8'.repeat(40),
    getRepoVariable: async () => { throw Object.assign(new Error('missing'), {status: 404}); },
  });
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /base is behind/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('manual dispatch independently revalidates and merges the current release pull request', async () => {
  const fixture = metadataFixture();
  const dispatch = {repo: context.repo, eventName: 'workflow_dispatch', payload: {}};
  const result = await policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: dispatch, env});
  assert.equal(result.merged, true);
  assert.equal(fixture.mergeCalls.length, 1);
});

test('eligible live release uses an exact-head squash with validated title and blank body', async () => {
  const fixture = metadataFixture();
  const result = await policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env});
  assert.equal(result.merged, true);
  assert.deepEqual(fixture.mergeCalls, [{
    owner: 'owner', repo: 'repo', pull_number: 7, sha: HEAD, merge_method: 'squash',
    commit_title: 'chore(main): release 1.2.4', commit_message: '',
  }]);
});

test('current PR base alone does not make old branch checks current', async () => {
  const fixture = metadataFixture({ancestry: 'diverged'});
  await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(), context: autoContext(), env}), /must contain current main/);
  assert.equal(fixture.mergeCalls.length, 0);
});

test('corrected titles use the latest check attempt on the same head', async () => {
  const fixture = metadataFixture();
  const checks = successfulChecks();
  checks.push({...checks[0], id: 10, conclusion: 'failure'});
  checks.push({...checks[0], id: 11});
  const result = await policy.autoMergeRelease({github: fixture.github, readGithub: readClient(checks), context: autoContext(), env});
  assert.equal(result.merged, true);
});

test('a newer failed or queued check invalidates an older success', async () => {
  for (const state of [{status: 'completed', conclusion: 'failure'}, {status: 'queued', conclusion: null}]) {
    const fixture = metadataFixture();
    const checks = successfulChecks();
    checks.push({...checks[0], id: 10, ...state});
    await assert.rejects(policy.autoMergeRelease({github: fixture.github, readGithub: readClient(checks), context: autoContext(), env}), /not a current successful/);
    assert.equal(fixture.mergeCalls.length, 0);
  }
});
