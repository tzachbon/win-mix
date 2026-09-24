'use strict';

const TYPES = new Set(['feat', 'fix', 'perf', 'docs', 'chore', 'ci', 'build', 'test', 'refactor', 'style', 'revert']);
const METADATA_FILES = ['.release-please-manifest.json', 'CHANGELOG.md', 'VERSION'];
const REQUIRED_CHECKS = ['PR title', 'Windows build', 'Release metadata'];
const RELEASE_BRANCH = 'release-please--branches--main';
const MAIN_BRANCH = 'main';
const SHA = /^[0-9a-f]{40}$/;
const VERSION = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const TITLE_ERROR = 'Pull request title must use an allowed conventional-commit type, optional scope and !, then ": ". Examples: "fix: preserve mute", "feat(audio): add a channel", "feat!: change settings".';

function invariant(condition, message) {
  if (!condition) throw new Error(message);
}

function parsePullRequestTitle(title) {
  invariant(typeof title === 'string' && title === title.trim() && !/[\r\n]/.test(title), TITLE_ERROR);
  const match = /^(feat|fix|perf|docs|chore|ci|build|test|refactor|style|revert)(?:\(([^()\r\n]+)\))?(!)?: (\S(?:.*\S)?)$/.exec(title);
  invariant(match && TYPES.has(match[1]) && (!match[2] || match[2] === match[2].trim()), TITLE_ERROR);
  return {type: match[1], scope: match[2] || null, breaking: Boolean(match[3]), subject: match[4]};
}

function parseStableVersion(value) {
  invariant(typeof value === 'string', 'Version must be a stable three-component number.');
  const trimmed = value.trim();
  invariant(!/[\r\n]/.test(trimmed), 'Version must be a stable three-component number.');
  const match = VERSION.exec(trimmed);
  invariant(match, 'Version must contain three integers from 0 to 65535 without leading zeros.');
  const parts = match.slice(1).map(Number);
  invariant(parts.every(part => part <= 65535), 'Version must contain three integers from 0 to 65535 without leading zeros.');
  return {text: parts.join('.'), parts};
}

function compareVersions(left, right) {
  const a = typeof left === 'string' ? parseStableVersion(left).parts : left.parts;
  const b = typeof right === 'string' ? parseStableVersion(right).parts : right.parts;
  for (let index = 0; index < 3; index++) {
    if (a[index] !== b[index]) return Math.sign(a[index] - b[index]);
  }
  return 0;
}

function repository(context) {
  const {owner, repo} = context?.repo || {};
  invariant(typeof owner === 'string' && owner && typeof repo === 'string' && repo, 'Repository context is missing.');
  return {owner, repo};
}

function requireSha(value, label) {
  invariant(typeof value === 'string' && SHA.test(value), `${label} must be a full lowercase commit SHA.`);
  return value;
}

async function pages(call, args, key, totalKey) {
  const all = [];
  let expected;
  for (let page = 1; page <= 10000; page++) {
    const response = await call({...args, per_page: 100, page});
    invariant(response && response.data !== undefined, 'GitHub returned an incomplete paginated response.');
    const data = key ? response.data[key] : response.data;
    invariant(Array.isArray(data) && data.length <= 100, 'GitHub returned an invalid paginated response.');
    if (totalKey) {
      const total = response.data[totalKey];
      invariant(Number.isSafeInteger(total) && total >= 0, 'GitHub omitted the pagination total.');
      if (expected === undefined) expected = total;
      invariant(expected === total, 'GitHub pagination changed while it was being read.');
    }
    all.push(...data);
    invariant(expected === undefined || all.length <= expected, 'GitHub pagination exceeded its declared total.');
    if (expected !== undefined && all.length === expected) return all;
    if (expected === undefined && data.length < 100) return all;
    invariant(data.length === 100, 'GitHub pagination ended before its declared total.');
  }
  throw new Error('GitHub pagination did not terminate.');
}

function requireUnique(items, key, label) {
  const values = items.map(item => item?.[key]);
  invariant(values.every(Boolean) && new Set(values).size === values.length, `${label} contained missing or duplicate entries.`);
}

async function getTextFile(github, repo, path, ref) {
  const response = await github.rest.repos.getContent({...repo, path, ref});
  const file = response?.data;
  invariant(file && !Array.isArray(file) && file.type === 'file' && file.encoding === 'base64' && typeof file.content === 'string', `${path} must be a regular file.`);
  return Buffer.from(file.content.replace(/\s/g, ''), 'base64').toString('utf8');
}

function manifestVersion(text) {
  let manifest;
  try {
    manifest = JSON.parse(text);
  } catch {
    throw new Error('.release-please-manifest.json must be valid JSON.');
  }
  invariant(manifest && typeof manifest === 'object' && !Array.isArray(manifest) && Object.keys(manifest).length === 1 && typeof manifest['.'] === 'string', 'Release manifest must contain only the root package version.');
  return parseStableVersion(manifest['.']);
}

function propsVersion(text) {
  const matches = [...text.matchAll(/<Version>([^<]+)<\/Version>/g)];
  invariant(matches.length === 1, 'Directory.Build.props must contain exactly one Version value.');
  return parseStableVersion(matches[0][1]);
}

function releaseAuthor(pr, env) {
  const login = env.RELEASE_BOT_LOGIN;
  const userId = env.RELEASE_BOT_USER_ID;
  invariant(typeof login === 'string' && login.endsWith('[bot]') && /^[1-9]\d*$/.test(String(userId || '')) && Number.isSafeInteger(Number(userId)), 'Release bot user identity environment is missing or invalid.');
  invariant(pr?.user?.login === login && pr.user.type === 'Bot' && Number.isSafeInteger(pr.user.id) && String(pr.user.id) === String(userId), 'Release pull request author is not the configured bot.');
}

async function appIdentity(github, pr, env) {
  releaseAuthor(pr, env);
  const appId = env.RELEASE_BOT_APP_ID;
  invariant(/^[1-9]\d*$/.test(String(appId || '')) && Number.isSafeInteger(Number(appId)), 'Release App identity environment is missing or invalid.');
  const slug = env.RELEASE_BOT_LOGIN.slice(0, -5);
  const app = (await github.rest.apps.getBySlug({app_slug: slug}))?.data;
  invariant(app?.slug === slug && String(app.id) === String(appId), 'Release bot login does not match the configured GitHub App ID.');
}

async function validatePullRequestTitle({context}) {
  return parsePullRequestTitle(context?.payload?.pull_request?.title);
}

async function getPullRequest(github, repo, number) {
  invariant(Number.isSafeInteger(number) && number > 0, 'Pull request number is missing.');
  const response = await github.rest.pulls.get({...repo, pull_number: number});
  invariant(response?.data?.number === number, 'GitHub returned the wrong pull request.');
  return response.data;
}

async function missingFile(github, repo, path, ref) {
  try {
    await getTextFile(github, repo, path, ref);
    return false;
  } catch (error) {
    if (error?.status === 404) return true;
    throw error;
  }
}

async function validateBootstrap(github, repo, pr, files) {
  const bootstrap = files.filter(file => METADATA_FILES.includes(file.filename));
  if (bootstrap.length !== 2 || !['VERSION', '.release-please-manifest.json'].every(path => bootstrap.some(file => file.filename === path && file.status === 'added'))) return null;
  invariant(pr.base?.ref === MAIN_BRANCH, 'Initial release metadata migration must target main.');
  const headSha = requireSha(pr.head?.sha, 'Bootstrap pull request head');
  const baseSha = requireSha(pr.base?.sha, 'Bootstrap pull request base');
  const [versionMissing, manifestMissing] = await Promise.all([
    missingFile(github, repo, 'VERSION', baseSha),
    missingFile(github, repo, '.release-please-manifest.json', baseSha),
  ]);
  if (!versionMissing || !manifestMissing) return null;
  const [headVersion, headManifest, oldProps] = await Promise.all([
    getTextFile(github, repo, 'VERSION', headSha).then(parseStableVersion),
    getTextFile(github, repo, '.release-please-manifest.json', headSha).then(manifestVersion),
    getTextFile(github, repo, 'Directory.Build.props', baseSha).then(propsVersion),
  ]);
  invariant(compareVersions(headVersion, oldProps) === 0 && compareVersions(headManifest, oldProps) === 0, 'Initial VERSION and release manifest must match the previous Directory.Build.props version.');
  return {kind: 'bootstrap', pr, version: oldProps.text, headSha};
}

async function validateReleaseMetadata({github, context, env = process.env, pullRequest}) {
  const repo = repository(context);
  const number = pullRequest?.number ?? context?.payload?.pull_request?.number;
  const pr = await getPullRequest(github, repo, number);
  const files = await pages(github.rest.pulls.listFiles, {...repo, pull_number: pr.number}, null);
  requireUnique(files, 'filename', 'Pull request files');
  invariant(Number.isSafeInteger(pr.changed_files) && pr.changed_files >= 0 && pr.changed_files < 3000 && files.length === pr.changed_files, 'Pull request file list is incomplete or exceeds the GitHub API limit.');
  const protectedFiles = files.filter(file => METADATA_FILES.includes(file.filename));
  if (protectedFiles.length === 0) return {kind: 'regular', pr};

  const bootstrap = await validateBootstrap(github, repo, pr, files);
  if (bootstrap) return bootstrap;

  invariant(files.length === METADATA_FILES.length && METADATA_FILES.every(path => files.some(file => file.filename === path)), 'Release metadata changes must contain exactly VERSION, CHANGELOG.md, and .release-please-manifest.json.');
  invariant(files.every(file => file.status === 'added' || file.status === 'modified'), 'Release metadata files must be regular added or modified files.');
  // PR jobs have no credentials to look up a private App. Pin its immutable bot user ID.
  releaseAuthor(pr, env);
  invariant(pr.base?.ref === MAIN_BRANCH, 'Release pull request must target main.');
  invariant(pr.head?.ref === RELEASE_BRANCH, 'Release pull request has the wrong branch.');
  invariant(pr.head?.repo?.full_name === `${repo.owner}/${repo.repo}`, 'Release pull request branch must be in the same repository.');
  const headSha = requireSha(pr.head?.sha, 'Release pull request head');
  const baseSha = requireSha(pr.base?.sha, 'Release pull request base');

  const [headVersionText, headManifestText] = await Promise.all([
    getTextFile(github, repo, 'VERSION', headSha),
    getTextFile(github, repo, '.release-please-manifest.json', headSha),
    getTextFile(github, repo, 'CHANGELOG.md', headSha),
  ]);
  const newVersion = parseStableVersion(headVersionText);
  invariant(compareVersions(newVersion, manifestVersion(headManifestText)) === 0, 'VERSION and release manifest versions must match.');

  const baseManifest = manifestVersion(await getTextFile(github, repo, '.release-please-manifest.json', baseSha));
  let oldVersion;
  try {
    oldVersion = parseStableVersion(await getTextFile(github, repo, 'VERSION', baseSha));
  } catch (error) {
    if (error?.status !== 404) throw error;
    oldVersion = propsVersion(await getTextFile(github, repo, 'Directory.Build.props', baseSha));
  }
  invariant(compareVersions(oldVersion, baseManifest) === 0, 'Base VERSION and release manifest versions must match.');
  invariant(compareVersions(newVersion, oldVersion) > 0, 'Release version must increase.');
  const bump = newVersion.parts[0] !== oldVersion.parts[0] ? 'major' : newVersion.parts[1] !== oldVersion.parts[1] ? 'minor' : 'patch';
  const expectedTitle = `chore(main): release ${newVersion.text}`;
  invariant(pr.title === expectedTitle, `Release pull request title must be "${expectedTitle}".`);
  parsePullRequestTitle(pr.title);
  return {
    kind: 'release', pr, oldVersion: oldVersion.text, newVersion: newVersion.text,
    bump, automatic: bump !== 'major', headSha, mergeCandidateSha: pr.merge_commit_sha || null,
  };
}

async function validateReleaseMetadataCheck(args) {
  const metadata = await validateReleaseMetadata(args);
  invariant(metadata.kind !== 'release' || (args.env || process.env).RELEASE_AUTOMATION_ENABLED === 'true', 'Release automation is paused; release pull requests cannot merge.');
  return metadata;
}

async function associatedPullRequests(github, repo, sha) {
  const pulls = await pages(github.rest.repos.listPullRequestsAssociatedWithCommit, {...repo, commit_sha: sha}, null);
  requireUnique(pulls, 'number', 'Associated pull requests');
  invariant(pulls.length === 1, `Commit ${sha} must have exactly one associated pull request.`);
  return pulls[0];
}

function validateMergedCommit(commit, pr) {
  invariant(pr.merged_at && pr.merge_commit_sha === commit.sha && pr.base?.ref === MAIN_BRANCH, `Commit ${commit.sha} must exactly match one pull request merged to main.`);
  const lines = commit.commit?.message?.split(/\r?\n/);
  invariant(Array.isArray(lines) && lines[0], `Commit ${commit.sha} has no message.`);
  const suffix = / \(#(\d+)\)$/.exec(lines[0]);
  if (suffix) invariant(Number(suffix[1]) === pr.number, `Commit ${commit.sha} has the wrong GitHub pull request suffix.`);
  const title = suffix ? lines[0].slice(0, suffix.index) : lines[0];
  const commitIntent = parsePullRequestTitle(title);
  const pullIntent = parsePullRequestTitle(pr.title);
  invariant(commitIntent.type === pullIntent.type && commitIntent.breaking === pullIntent.breaking, `Commit ${commit.sha} type or breaking intent does not match PR #${pr.number}.`);
  const body = lines.slice(1).join('\n');
  invariant(!/(?:BREAKING(?: |- )CHANGE|Release-As):/i.test(`${title}\n${body}`), `Commit ${commit.sha} for PR #${pr.number} contains a forbidden release override.`);
  invariant(lines.slice(1).every(line => !line.trim() || /^Co-authored-by: .+ <[^<>\s]+@[^<>\s]+>$/i.test(line)), `Commit ${commit.sha} body may contain only Co-authored-by trailers.`);
}

async function compare(github, repo, base, head, collectCommits) {
  requireSha(base, 'Comparison base');
  requireSha(head, 'Comparison head');
  if (!collectCommits) {
    const response = await github.rest.repos.compareCommitsWithBasehead({...repo, basehead: `${base}...${head}`, per_page: 1, page: 1});
    invariant(response?.data && ['ahead', 'behind', 'identical', 'diverged'].includes(response.data.status), 'GitHub returned an incomplete comparison.');
    return {status: response.data.status, commits: []};
  }
  let status;
  let total;
  const commits = [];
  for (let page = 1; page <= 10000; page++) {
    const response = await github.rest.repos.compareCommitsWithBasehead({...repo, basehead: `${base}...${head}`, per_page: 100, page});
    const data = response?.data;
    invariant(data && ['ahead', 'behind', 'identical', 'diverged'].includes(data.status) && Number.isSafeInteger(data.total_commits) && data.total_commits >= 0 && Array.isArray(data.commits), 'GitHub returned an incomplete comparison.');
    if (status === undefined) ({status, total_commits: total} = data);
    invariant(status === data.status && total === data.total_commits, 'GitHub comparison changed while it was being read.');
    commits.push(...data.commits);
    invariant(commits.length <= total, 'GitHub comparison exceeded its declared total.');
    if (commits.length === total) break;
    invariant(data.commits.length === 100, 'GitHub comparison ended before its declared total.');
  }
  requireUnique(commits, 'sha', 'Compared commits');
  invariant(commits.length === total, 'GitHub comparison did not return every commit.');
  return {status, commits};
}

async function resolveTag(github, repo, tag) {
  let object = (await github.rest.git.getRef({...repo, ref: `tags/${tag}`}))?.data?.object;
  for (let depth = 0; depth < 10; depth++) {
    invariant(object && (object.type === 'commit' || object.type === 'tag') && SHA.test(object.sha), `Tag ${tag} did not resolve to a commit.`);
    if (object.type === 'commit') return object.sha;
    object = (await github.rest.git.getTag({...repo, tag_sha: object.sha}))?.data?.object;
  }
  throw new Error(`Tag ${tag} has too many annotation layers.`);
}

async function latestStableRelease(github, repo) {
  const releases = await pages(github.rest.repos.listReleases, repo, null);
  const stable = releases.filter(release => !release?.draft && !release?.prerelease && typeof release.tag_name === 'string' && /^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/.test(release.tag_name));
  invariant(stable.length, 'No published stable release was found.');
  stable.forEach(release => invariant(Number.isFinite(Date.parse(release.published_at)), 'Published release is missing its publication time.'));
  stable.sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
  invariant(stable.length === 1 || stable[0].published_at !== stable[1].published_at, 'Latest published stable release is ambiguous.');
  return stable[0];
}

async function validateMainPreflight({github, context, env = process.env, headSha = context?.sha}) {
  const repo = repository(context);
  const startSha = requireSha(env.RELEASE_POLICY_START_SHA, 'RELEASE_POLICY_START_SHA');
  requireSha(headSha, 'Main head');
  const release = await latestStableRelease(github, repo);
  const releaseSha = await resolveTag(github, repo, release.tag_name);
  const relation = await compare(github, repo, startSha, releaseSha, false);
  invariant(relation.status !== 'diverged', 'Policy start and latest stable release are not in one ancestry chain.');
  const boundarySha = relation.status === 'ahead' ? releaseSha : startSha;
  const range = await compare(github, repo, boundarySha, headSha, true);
  invariant(range.status === 'ahead' || range.status === 'identical', 'Main head does not descend from the validated release boundary.');
  for (const commit of range.commits) {
    const associated = await associatedPullRequests(github, repo, commit.sha);
    const pr = await getPullRequest(github, repo, associated.number);
    validateMergedCommit(commit, pr);
  }
  return {boundarySha, releaseTag: release.tag_name, releaseSha, commits: range.commits.map(commit => commit.sha)};
}

async function assertReleaseCommit({github, context, env = process.env, sha, version}) {
  const repo = repository(context);
  requireSha(sha, 'Release commit');
  const parsedVersion = parseStableVersion(version);
  const associated = await associatedPullRequests(github, repo, sha);
  const pr = await getPullRequest(github, repo, associated.number);
  invariant(pr.merged_at && pr.merge_commit_sha === sha, 'Release commit must exactly match its merged pull request.');
  invariant(pr.title === `chore(main): release ${parsedVersion.text}`, 'Release commit pull request has the wrong title or version.');
  const metadata = await validateReleaseMetadata({github, context, env, pullRequest: pr});
  await appIdentity(github, metadata.pr, env);
  invariant(metadata.kind === 'release' && metadata.newVersion === parsedVersion.text, 'Release commit metadata does not match its version.');
  return {pr: metadata.pr, metadata};
}

async function currentReleasePullRequest(github, repo) {
  const pulls = await pages(github.rest.pulls.list, {...repo, state: 'open', base: MAIN_BRANCH, head: `${repo.owner}:${RELEASE_BRANCH}`}, null);
  requireUnique(pulls, 'number', 'Release pull requests');
  if (pulls.length === 0) return null;
  invariant(pulls.length === 1, 'More than one current release pull request exists.');
  return getPullRequest(github, repo, pulls[0].number);
}

async function validateChecks(github, repo, ref) {
  const checks = await pages(github.rest.checks.listForRef, {...repo, ref, filter: 'latest'}, 'check_runs', 'total_count');
  requireUnique(checks, 'id', 'Check runs');
  let ready = true;
  for (const name of REQUIRED_CHECKS) {
    const matches = checks.filter(check => check.name === name);
    if (!matches.length) { ready = false; continue; }
    invariant(matches.every(check => Number.isSafeInteger(check.id) && check.id > 0), `Required check "${name}" is invalid.`);
    // Title edits create additional suites on the same head. A newer pending or failed check supersedes an older success.
    const check = matches.reduce((latest, candidate) => candidate.id > latest.id ? candidate : latest);
    invariant(check.head_sha === ref && check.app?.slug === 'github-actions', `Required check "${name}" has the wrong commit or provider.`);
    if (['queued', 'in_progress'].includes(check.status) && check.conclusion === null) { ready = false; continue; }
    invariant(check.status === 'completed' && check.conclusion === 'success', `Required check "${name}" is not a current successful GitHub Actions check.`);
  }
  return ready;
}

async function automationEnabled(github, repo) {
  try {
    const response = await github.rest.actions.getRepoVariable({...repo, name: 'RELEASE_AUTOMATION_ENABLED'});
    return response?.data?.name === 'RELEASE_AUTOMATION_ENABLED' && response.data.value === 'true';
  } catch (error) {
    if (error?.status === 404) return false;
    throw error;
  }
}

async function autoMergeRelease({github, readGithub = github, context, core, env = process.env}) {
  const repo = repository(context);
  const run = context?.payload?.workflow_run;
  if (run) {
    if (!['CI', 'PR policy'].includes(run.name) || run.event !== 'pull_request' || run.conclusion !== 'success') return {eligible: false, reason: 'unrelated-workflow-run'};
  } else {
    invariant(context?.eventName === 'workflow_dispatch', 'Auto-merge accepts only trusted workflow_run or workflow_dispatch events.');
  }
  const pr = await currentReleasePullRequest(github, repo);
  if (!pr) return {eligible: false, reason: 'no-release-pr'};
  const metadata = await validateReleaseMetadata({github, context, env, pullRequest: pr});
  await appIdentity(github, metadata.pr, env);
  invariant(metadata.kind === 'release', 'Release branch does not contain a valid release pull request.');
  if (!metadata.automatic) {
    core?.notice?.(`Release pull request #${pr.number} is a major release and requires manual merge.`);
    return {eligible: false, reason: 'major-release', number: pr.number};
  }
  const checkedSha = metadata.headSha;
  const mergeCandidateSha = requireSha(metadata.mergeCandidateSha, 'Release merge candidate');
  if (run && (run.head_sha !== checkedSha || run.head_branch !== RELEASE_BRANCH)) return {eligible: false, reason: 'unrelated-release-head'};
  if (!await validateChecks(readGithub, repo, checkedSha)) return {eligible: false, reason: 'checks-pending', number: pr.number};
  const releases = await pages(github.rest.repos.listReleases, repo, null);
  invariant(!releases.some(release => release?.draft), 'A draft release is already pending.');

  const current = await getPullRequest(github, repo, pr.number);
  invariant(current.state === 'open' && !current.draft && current.head?.sha === metadata.headSha && current.base?.sha === metadata.pr.base.sha && current.merge_commit_sha === mergeCandidateSha && current.title === metadata.pr.title, 'Release pull request changed after validation.');
  invariant(current.mergeable === true, 'Release pull request is not currently mergeable.');
  const mainSha = requireSha((await github.rest.repos.getBranch({...repo, branch: MAIN_BRANCH}))?.data?.commit?.sha, 'Current main head');
  invariant(current.base.sha === mainSha, 'Release pull request base is behind current main.');
  const mainVersion = parseStableVersion(await getTextFile(github, repo, 'VERSION', mainSha)).text;
  const published = await latestStableRelease(github, repo);
  invariant(published.tag_name === `v${mainVersion}`, `Main current version ${mainVersion} is not published; finish its release before merging another version.`);
  const ancestry = await compare(github, repo, mainSha, metadata.headSha, false);
  invariant(['ahead', 'identical'].includes(ancestry.status), 'Release branch must contain current main before using its checks.');
  if (!await automationEnabled(github, repo)) {
    core?.notice?.(`Release automation is paused; pull request #${current.number} at ${metadata.headSha} would merge.`);
    return {paused: true, wouldMerge: {number: current.number, headSha: metadata.headSha}};
  }
  const response = await github.rest.pulls.merge({
    ...repo, pull_number: current.number, sha: metadata.headSha, merge_method: 'squash',
    commit_title: current.title, commit_message: '',
  });
  invariant(response?.data?.merged === true, `GitHub did not merge pull request #${current.number}.`);
  return {paused: false, merged: true, number: current.number, sha: response.data.sha};
}

module.exports = {
  METADATA_FILES, REQUIRED_CHECKS, RELEASE_BRANCH,
  parsePullRequestTitle, parseStableVersion, compareVersions,
  validatePullRequestTitle, validateReleaseMetadata, validateReleaseMetadataCheck, validateMainPreflight,
  assertReleaseCommit, autoMergeRelease,
};
