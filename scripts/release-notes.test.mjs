import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { analyzeCommits } from '@semantic-release/commit-analyzer';
import { generateNotes } from '@semantic-release/release-notes-generator';

const config = JSON.parse(await readFile(new URL('../.releaserc.json', import.meta.url), 'utf8'));
const analyzerOptions = config.plugins.find(([name]) => name === '@semantic-release/commit-analyzer')[1];
const notesOptions = config.plugins.find(([name]) => name === '@semantic-release/release-notes-generator')[1];
const logger = { log() {} };

for (const [message, releaseType, version, heading] of [
    ['fix: preserve error details', 'patch', '1.1.1', 'Bug Fixes'],
    ['feat: add telemetry correlation', 'minor', '1.2.0', 'Features'],
    ['feat!: change reporting contract\n\nBREAKING CHANGE: callers must supply context', 'major', '2.0.0', 'BREAKING CHANGES']
]) {
    for (const branch of config.branches) {
        const branchName = typeof branch === 'string' ? branch : branch.name;
        const nextVersion = typeof branch === 'string' ? version : `${version}-${branch.prerelease}.1`;
        test(`${branchName}: analyzes and renders ${releaseType} release notes`, async () => {
            const context = {
                cwd: process.cwd(),
                logger,
                options: { repositoryUrl: config.repositoryUrl },
                commits: [{ hash: '1234567890abcdef1234567890abcdef12345678', message }],
                lastRelease: { version: '1.1.0', gitTag: 'v1.1.0' },
                nextRelease: { version: nextVersion, gitTag: `v${nextVersion}` }
            };

            assert.equal(await analyzeCommits(analyzerOptions, context), releaseType);
            const notes = await generateNotes(notesOptions, context);
            assert.ok(notes.includes(nextVersion), notes);
            assert.ok(notes.includes(heading), notes);
            assert.ok(notes.includes(message.split('\n')[0].split(': ')[1]), notes);
            assert.ok(notes.includes(`compare/v1.1.0...v${nextVersion}`), notes);
            if (releaseType === 'major') {
                assert.ok(notes.includes('callers must supply context'), notes);
            }
        });
    }
}

test('maintenance commits do not trigger a release', async () => {
    assert.equal(await analyzeCommits(analyzerOptions, {
        cwd: process.cwd(), logger, commits: [{ hash: 'abcdef1', message: 'chore: update dependencies' }]
    }), null);
});
