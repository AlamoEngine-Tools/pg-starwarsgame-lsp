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

const { tokenOffences } = require('./styleTokenRules');

const ROOT = path.join(__dirname, '..', 'src');

/** Where the token layer is declared, and the only file allowed to write the values themselves. */
const TOKENS = path.join(ROOT, 'webview', 'shared', 'tokens.ts');

/**
 * Files whose raw values are not design decisions.
 *
 * The credits crawl reproduces the game's own titles - themed, it would stop being the crawl - and
 * the encyclopedia card draws text over artwork loaded from the game. A colour in either is game
 * truth. Neither carries the token layer either, so a var() in them would resolve to nothing.
 */
const GAME_TRUTH = new Set([
    path.join(ROOT, 'webview', 'creditsCrawl.tsx'),
    path.join(ROOT, 'webview', 'encyclopediaCard.tsx'),
]);

/** Every custom property the layer declares, for checking that a var() resolves to something. */
function definedTokens() {
    const text = fs.readFileSync(TOKENS, 'utf8');
    const names = new Set();

    // Declarations in the CSS blocks, plus the colour roles, which are generated from a list of
    // objects rather than written out as CSS.
    for (const m of text.matchAll(/^\s*(--[a-z0-9-]+)\s*:/gm)) { names.add(m[1]); }
    for (const m of text.matchAll(/name:\s*'(--[a-z0-9-]+)'/g)) { names.add(m[1]); }

    return names;
}

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

/** Every style template in a file, as [body, offsetOfBody]. */
function templates(text) {
    const opener = /(?:styled\.[A-Za-z][A-Za-z0-9]*|createGlobalStyle|[A-Za-z][A-Za-z0-9]*(?:Css|Styles)\s*=)\s*`/g;
    const found = [];

    for (let match = opener.exec(text); match !== null; match = opener.exec(text)) {
        const start = match.index + match[0].length;
        const end = endOfTemplate(text, start);

        if (end === -1) {
            continue;
        }

        found.push([text.slice(start, end), start]);
        opener.lastIndex = end;
    }

    return found;
}

let failed = 0;
let cutShort = 0;
const defined = definedTokens();

for (const file of sources(ROOT)) {
    const text = fs.readFileSync(file, 'utf8');
    const where = path.relative(process.cwd(), file);

    for (const offence of offences(text)) {
        console.error(`${where}:${offence.line}`
            + '  a backtick in this CSS comment ends the styled template.');
        console.error(`    ${offence.text}`);
        cutShort++;
        failed++;
    }

    // The token layer's own file writes the values; everything else references them.
    if (file === TOKENS) {
        continue;
    }

    const exempt = GAME_TRUTH.has(file);

    // A component may set a custom property of its own from JS - the credits crawl drives its
    // animation from `style={{ '--crawl-from': ... }}` - so the declaration is in the same file but
    // outside the stylesheet that reads it.
    // The key may be computed and cast - `['--crawl-from' as string]:` - so anything but a newline
    // is allowed between the closing quote and the colon.
    const alsoDeclared = new Set(
        [...text.matchAll(/['"](--[a-z0-9-]+)['"][^:\n]*:/g)].map(m => m[1]));

    for (const [body, offset] of templates(text)) {
        for (const offence of tokenOffences(body, {
            defined, alsoDeclared, allowRawLengths: exempt, allowRawColours: exempt,
        })) {
            const line = text.slice(0, offset + offence.index).split('\n').length;

            console.error(`${where}:${line}  ${offence.text}`);
            console.error(`    use ${offence.hint}`);
            failed++;
        }
    }
}

if (cutShort > 0) {
    console.error(
        `\n${cutShort} styled template${cutShort === 1 ? '' : 's'} cut short by a comment.`);
    console.error('Rewrite it without backticks - name the class or property in plain words.');
}

if (failed > 0) {
    console.error(`\n${failed} style problem${failed === 1 ? '' : 's'}.`);
    console.error('Spacing, radius and type come from src/webview/shared/tokens.ts. A fitted');
    console.error('measurement that is not on any scale belongs in that file too, named.');
    process.exit(1);
}
