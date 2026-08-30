// A backtick inside a CSS comment in a styled template literal.
//
// This cannot be an ESLint rule, and that is the whole reason the check exists. The stray backtick
// ENDS the template literal, so everything after it is reinterpreted as code and the file stops
// parsing - ESLint never gets to see the construct it would have flagged. TypeScript does report
// it, but from wherever the wreckage first becomes a syntax error, which in practice has been
// twenty to forty lines away and about something else entirely:
//
//     TS1443: Module declaration names may only use ' or " quoted strings
//     TS2604: JSX element type 'Shell' does not have any construct or call signatures
//
// Four separate incidents in one session were spent rediscovering that. So this reads the file as
// TEXT, before any parser has an opinion, and names the actual line.
//
// How it decides. Not "a comment contains a backtick" - after the template, the rest of the file is
// ordinary TypeScript whose JSDoc quotes identifiers constantly, and flagging those buried the real
// thing in forty false positives on the first attempt. The signature is exact instead: walk the
// template from its opening backtick to the backtick that closes it, and ask whether that closing
// backtick is sitting INSIDE a comment. In a healthy file it never is. When it is, that backtick
// was meant to be prose and has silently become the end of the stylesheet.

const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..', 'src');

/** Every .ts/.tsx under src, so a styled template added anywhere is covered. */
function sources(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
        const full = path.join(dir, entry.name);

        if (entry.isDirectory()) {
            return sources(full);
        }

        return /\.tsx?$/.test(entry.name) ? [full] : [];
    });
}

/**
 * Where the template opened at `from` ends, or -1 if it never does.
 *
 * Interpolations are skipped by brace depth: a `${...}` may itself hold a string, an object or
 * another template, and a backtick inside one of those is not the stylesheet's end.
 */
function endOfTemplate(text, from) {
    for (let at = from; at < text.length; at++) {
        const ch = text[at];

        if (ch === '\\') {
            at++;
        } else if (ch === '$' && text[at + 1] === '{') {
            let depth = 1;
            at += 2;

            while (at < text.length && depth > 0) {
                if (text[at] === '{') { depth++; } else if (text[at] === '}') { depth--; }
                at++;
            }

            at--;
        } else if (ch === '`') {
            return at;
        }
    }

    return -1;
}

/** The offences in one file. */
function offences(text) {
    const found = [];
    // Two ways a stylesheet is written here, and BOTH have to be covered:
    //
    //   styled.div`...`            - a component's own styles
    //   const dockChromeCss = `...` - the chrome every editor shares
    //
    // Only the first was matched, so the shared stylesheet - the one a mistake in breaks four
    // editors rather than one - was the single unguarded file in the project. A backtick landed in
    // one of its comments and the build failed with a JSX error naming a different file.
    const opener = /(?:styled\.[A-Za-z][A-Za-z0-9]*|[A-Za-z][A-Za-z0-9]*(?:Css|Styles)\s*=)\s*`/g;

    for (let match = opener.exec(text); match !== null; match = opener.exec(text)) {
        const start = match.index + match[0].length;
        const end = endOfTemplate(text, start);

        if (end === -1) {
            continue;
        }

        const body = text.slice(start, end);
        const lastOpen = body.lastIndexOf('/*');
        const lastClose = body.lastIndexOf('*/');

        // A comment opened and never closed before the template did: the backtick that closed the
        // template is inside it.
        if (lastOpen > lastClose) {
            found.push({
                line: text.slice(0, start + lastOpen).split('\n').length,
                text: body.slice(lastOpen).split('\n')[0].trim(),
            });
        }

        opener.lastIndex = end;
    }

    return found;
}

let failed = 0;

for (const file of sources(ROOT)) {
    for (const offence of offences(fs.readFileSync(file, 'utf8'))) {
        console.error(
            `${path.relative(process.cwd(), file)}:${offence.line}`
            + '  a backtick in this CSS comment ends the styled template.');
        console.error(`    ${offence.text}`);
        failed++;
    }
}

if (failed > 0) {
    console.error(
        `\n${failed} styled template${failed === 1 ? '' : 's'} cut short by a comment.`);
    console.error('Rewrite it without backticks - name the class or property in plain words.');
    process.exit(1);
}
