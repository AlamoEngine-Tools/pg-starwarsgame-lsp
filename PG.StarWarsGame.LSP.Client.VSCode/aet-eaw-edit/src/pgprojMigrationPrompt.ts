// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the project-migration proposal says and what each answer means.
//
// Kept apart from the vscode wiring so it can be tested: the diff editor and the modal cannot be
// driven from the unit harness, but the text a user reads and the decision taken from their answer
// are exactly the parts worth pinning.

/** The server's proposal - the migrated project file, and why it changed. */
export interface PgprojMigrationProposal {
    path: string;
    fileName: string;
    proposedText: string;
    notices: string[];
}

/** The two answers, as they appear on the buttons. */
export const ACCEPT = 'Update project file';
export const DECLINE = 'Discard and stop';

/** Title of the diff tab: which file, and which direction the change runs. */
export function diffTitle(fileName: string): string {
    return `${fileName} - on disk <-> updated format`;
}

/**
 * The question put beside the diff.
 *
 * It names the cost, because it is one the diff cannot show: System.Text.Json drops comments while
 * parsing, so any comment in the project file is gone once it is rewritten. And it carries each
 * migration's own notice, which is the one channel a step has for saying it needs something done
 * by hand.
 */
export function proposalMessage(proposal: PgprojMigrationProposal): string {
    const notices = proposal.notices.length > 0 ? ` ${proposal.notices.join(' ')}` : '';
    return `'${proposal.fileName}' uses an older project format.${notices}`
        + ' Review the changes and update the file? Comments in it are not preserved, and a backup'
        + ' is written next to it first. Declining stops the language server.';
}

/** What the client does next, given what the user picked (or dismissed). */
export type ProposalOutcome = 'accept' | 'decline';

/**
 * Anything that is not an explicit yes is a decline.
 *
 * Dismissing the dialog is not "ask me later": the file stays as it is and the server stops, which
 * is the same place the explicit decline leads. Treating dismissal as consent would rewrite a
 * project file because somebody pressed Escape.
 */
export function outcomeOf(answer: string | undefined): ProposalOutcome {
    return answer === ACCEPT ? 'accept' : 'decline';
}

/** What the user is told once the server has stopped, so the state is not a mystery. */
export function declinedMessage(fileName: string): string {
    return `'${fileName}' was left unchanged, so aet-eaw-edit has stopped. `
        + 'Update the project file, or reload the window to be asked again.';
}
