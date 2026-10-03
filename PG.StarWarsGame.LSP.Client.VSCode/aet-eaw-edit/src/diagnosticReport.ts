// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A report about one diagnostic, for a GitHub issue or an editor tab. Kept free of `vscode` so the
// unit harness can run it.
//
// No GitHub login: the issue is PREFILLED and opened in the browser, and the author reviews and
// submits it under their own account. Nothing is posted from here.

/** What the server sends with the report actions: see `SuppressionCodeActionBuilder`. */
export interface DiagnosticReportPayload {
    id: string;
    severity: string;
    message: string;
    /** The file's name only - the report goes into a public issue, so never its path. */
    fileName: string;
    /** 0-based, as the server counts. */
    startLine: number;
    /**
     * The source lines the diagnostic's range touches. The server truncates: a range over 40
     * lines keeps its first and last 20 with one line saying how many were left out, and a line
     * over 400 characters is cut.
     */
    lines: string[];
}

export const ISSUES_URL = 'https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues';

/**
 * Prefill URLs stop working somewhere past 8 KB, depending on browser and proxy. Kept a little
 * under, because the limit is not documented and failing quietly is worse than trimming.
 */
const DEFAULT_MAX_URL = 8000;

const TITLE_LIMIT = 100;

/** The diagnostic and its source, as Markdown. Never trimmed. */
export function diagnosticSection(report: DiagnosticReportPayload): string {
    return [
        `**Diagnostic** \`${report.id}\` (${report.severity})`,
        '',
        `> ${report.message}`,
        '',
        `\`${report.fileName}\`, line ${report.startLine + 1}:`,
        '',
        '```' + fenceLanguage(report.fileName),
        ...report.lines,
        '```',
    ].join('\n');
}

/** `id: message`, kept to a readable length. */
export function issueTitle(report: DiagnosticReportPayload): string {
    const title = `${report.id}: ${report.message}`;
    return title.length <= TITLE_LIMIT ? title : title.slice(0, TITLE_LIMIT - 3) + '...';
}

/** The prefilled issue, and what had to go to fit it. */
export interface GitHubIssue {
    url: string;
    /** What was cut to fit, in the order it was cut. */
    trimmed: ('extended' | 'basic')[];
    /** False when even the diagnostic alone overflows - the editor report is then the way. */
    fits: boolean;
}

/**
 * The issue form, filled by field id (`.github/ISSUE_TEMPLATE/bug_report.yml`).
 *
 * Trimmed in a fixed order when it runs long: the extended stats go first, then the basic block
 * from its end. The diagnostic and its source never do - they are the point of the report.
 */
export function gitHubIssue(
    report: DiagnosticReportPayload, basic: string, extended: string | null, maxLength = DEFAULT_MAX_URL,
): GitHubIssue {
    const trimmed: GitHubIssue['trimmed'] = [];
    let ext = extended;
    let basicLines = basic.split('\n');

    const build = (): string => {
        const params = new URLSearchParams({
            template: 'bug_report.yml',
            title: issueTitle(report),
            'what-happened': diagnosticSection(report),
            'bug-report-info': basicLines.join('\n'),
        });
        if (ext !== null) {
            params.set('extended-stats', ext);
        }
        // URLSearchParams writes a space as "+", and a literal plus as "%2B" - so every remaining
        // "+" is a space. Spelled %20 instead, because vscode.Uri re-encodes a "+" on the way to
        // the browser and the issue would show plus signs where the spaces were.
        return `${ISSUES_URL}/new?${params.toString().replace(/\+/g, '%20')}`;
    };

    let url = build();
    if (url.length > maxLength && ext !== null) {
        ext = null;
        trimmed.push('extended');
        url = build();
    }

    if (url.length > maxLength) {
        trimmed.push('basic');
        while (url.length > maxLength && basicLines.length > 0) {
            basicLines = basicLines.slice(0, -1);
            url = build();
        }
    }

    return {url, trimmed, fits: url.length <= maxLength};
}

/** The report for an editor tab: everything, untrimmed. */
export function editorReport(report: DiagnosticReportPayload, fullReport: string): string {
    return [`# ${issueTitle(report)}`, '', diagnosticSection(report), '', fullReport].join('\n');
}

function fenceLanguage(fileName: string): string {
    const dot = fileName.lastIndexOf('.');
    const extension = dot < 0 ? '' : fileName.slice(dot + 1).toLowerCase();
    return extension === 'xml' || extension === 'lua' ? extension : 'text';
}
