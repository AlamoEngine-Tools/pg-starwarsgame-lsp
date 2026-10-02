// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {
    type DiagnosticReportPayload, ISSUES_URL, diagnosticSection, editorReport, gitHubIssue, issueTitle,
} from './diagnosticReport';

const payload: DiagnosticReportPayload = {
    id: 'aetswg-008-0001',
    severity: 'Error',
    message: 'Not a valid music event entry',
    fileName: 'groundinfantry.xml',
    startLine: 1,
    lines: ['  <Land_Terrain_Model_Mapping>', '    Temperate, EI_TROOPER.ALO'],
};

function field(url: string, name: string): string | null {
    return new URL(url).searchParams.get(name);
}

describe('diagnosticSection', () => {
    it('names the diagnostic and quotes every offending line, in the file\'s language', () => {
        const text = diagnosticSection(payload);

        assert.ok(text.includes('`aetswg-008-0001` (Error)'));
        assert.ok(text.includes('Not a valid music event entry'));
        // 1-based for the reader, as every editor shows it.
        assert.ok(text.includes('`groundinfantry.xml`, line 2'));
        assert.ok(text.includes('```xml\n  <Land_Terrain_Model_Mapping>\n    Temperate, EI_TROOPER.ALO\n```'));
    });
});

describe('issueTitle', () => {
    it('leads with the id', () => {
        assert.equal(issueTitle(payload), 'aetswg-008-0001: Not a valid music event entry');
    });

    it('keeps a long message to a readable title', () => {
        const title = issueTitle({...payload, message: 'x'.repeat(300)});
        assert.ok(title.length <= 100, `${title.length} characters`);
        assert.ok(title.endsWith('...'));
    });
});

describe('gitHubIssue', () => {
    it('fills the issue form by field id', () => {
        const issue = gitHubIssue(payload, 'BASIC', 'EXTENDED');

        assert.ok(issue.url.startsWith(`${ISSUES_URL}/new?`));
        assert.equal(field(issue.url, 'template'), 'bug_report.yml');
        assert.equal(field(issue.url, 'title'), issueTitle(payload));
        assert.ok(field(issue.url, 'what-happened')!.includes('Temperate, EI_TROOPER.ALO'));
        assert.equal(field(issue.url, 'bug-report-info'), 'BASIC');
        assert.equal(field(issue.url, 'extended-stats'), 'EXTENDED');
        assert.deepEqual(issue.trimmed, []);
    });

    it('encodes a space as %20, never as +', () => {
        // vscode.Uri re-encodes a literal + as %2B on the way to the browser, which would turn
        // every space in the prefill into a visible plus sign.
        const url = gitHubIssue(payload, 'a b', null).url;

        assert.ok(!url.includes('+'), url);
        assert.equal(field(url, 'bug-report-info'), 'a b');
    });

    it('drops the extended stats first when the prefill runs long', () => {
        const issue = gitHubIssue(payload, 'BASIC', 'E'.repeat(9000), 8000);

        assert.equal(field(issue.url, 'extended-stats'), null);
        assert.equal(field(issue.url, 'bug-report-info'), 'BASIC');
        assert.deepEqual(issue.trimmed, ['extended']);
    });

    it('then trims the basic block from the end, and never the diagnostic', () => {
        const basic = Array.from({length: 400}, (_, i) => `- line ${i}`).join('\n');
        const issue = gitHubIssue(payload, basic, null, 3000);

        assert.ok(issue.url.length <= 3000, `${issue.url.length} characters`);
        assert.ok(field(issue.url, 'what-happened')!.includes('Temperate, EI_TROOPER.ALO'));
        assert.ok(field(issue.url, 'bug-report-info')!.startsWith('- line 0'));
        assert.deepEqual(issue.trimmed, ['basic']);
    });

    it('says when even the diagnostic alone does not fit', () => {
        const huge = {...payload, lines: ['x'.repeat(10000)]};

        assert.equal(gitHubIssue(huge, 'BASIC', null, 8000).fits, false);
        assert.equal(gitHubIssue(payload, 'BASIC', null, 8000).fits, true);
    });
});

describe('editorReport', () => {
    it('carries the title, the diagnostic and the whole report, untrimmed', () => {
        const text = editorReport(payload, 'BASIC AND EXTENDED');

        assert.ok(text.startsWith(`# ${issueTitle(payload)}`));
        assert.ok(text.includes(diagnosticSection(payload)));
        assert.ok(text.includes('BASIC AND EXTENDED'));
    });

    it('is ASCII only', () => {
        assert.doesNotMatch(editorReport(payload, 'x'), /[^\x00-\x7F]/);
    });
});
