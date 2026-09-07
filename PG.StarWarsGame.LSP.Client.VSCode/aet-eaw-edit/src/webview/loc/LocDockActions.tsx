// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The things you do to the file as a whole, rather than to a row: give it another language, write
// it out in a different format, or export what the game loads.
//
// They sit at the top of the dock - under Save and the severity pill, above whatever the editor
// puts below - because they are file-level, like those two, and unlike everything under them. Each
// is a tile that opens a dialog: all three change the file as a whole, which is not something to
// have happen the instant a control is touched.

import { useState } from 'react';

import { DIALOG_IDS } from '../shared/dialogGeometryStore';
import {
    canCopyBetweenLanguages, copyFromOptions, FillSource, nextCopyFrom, resolveFillSource,
} from './fillLanguage';
import { LocModal } from './LocModal';
import { DockSection } from '../shared/DockSection';
import { LocTile, LocTileGrid } from './LocTile';
import { Field } from '../shared/Field';

/** The text formats a file can be rewritten as. DAT is not one - it is what Export produces. */
export const CONVERTIBLE_FORMATS = ['CSV', 'XML', 'NLS'] as const;
export type ConvertibleFormat = typeof CONVERTIBLE_FORMATS[number];

const FORMAT_DESCRIPTIONS: Record<ConvertibleFormat, string> = {
    CSV: 'Comma-separated values (.csv)',
    XML: 'eaw-translation v1 XML (.xml)',
    NLS: 'Java-style properties (.properties)',
};

export interface LocDockActionsProps {
    languages: string[];
    /** False for the single-language formats, where the action is left out rather than shown failing. */
    canAddLanguage: boolean;
    /**
     * Whether adding a language creates a sibling file rather than a column. Single-language
     * formats (.properties, .dat) cannot take a column, so the same action writes a new file.
     */
    addLanguageCreatesFile: boolean;
    /**
     * The languages the engine supports. Only these are offered: a made-up identifier compiles into
     * a column the game never reads, and the mistake is invisible until someone plays in it.
     */
    supportedLanguages: string[];
    /** Total entries in the file, so the fill offer can say "N of M". */
    rowCount: number;
    /**
     * @param fillFromBaseline Copy the game's own text for this language into every key the file
     *   shares with the baseline. Only ever true where {@link baselineFillCount} is supplied.
     */
    onAddLanguage: (language: string, fillFromBaseline: boolean) => void;
    /**
     * How many cells the game's own text could fill for a language, or undefined where there is no
     * baseline to draw on. Answers for both dialogs - the one that adds a language and the one that
     * fills an existing one - because it is the same question either way.
     */
    baselineFillCount?: (language: string) => number;
    /**
     * Copies one language into the cells another has left empty. Absent where the file has fewer
     * than two languages, since there is nothing to copy between.
     */
    onCopyLanguage?: (from: string, to: string) => void;
    /** How many cells the copy would fill, so the offer says what it will actually do. */
    copyLanguageCount?: (from: string, to: string) => number;
    /**
     * Fills a language from the game's own text. Absent where there is no baseline to draw on.
     */
    onFillFromBaseline?: (language: string) => void;
    /**
     * Whether that fill ADDS lines rather than filling cells that are already there - which is what
     * it means on a file with nothing in it. Only the wording changes; the editor decides when.
     */
    fillsByAdding?: boolean;
    /**
     * The project's other language files, offered as a starting point for a file with nothing in
     * it. Empty where there are none, or where starting from one makes no sense.
     */
    seedSources?: readonly {
        filePath: string; label: string; language: string; rowCount: number;
    }[];
    /** Copies every line of one of {@link seedSources} in, ready to translate. */
    onSeedFrom?: (filePath: string, language: string) => void;
    onConvertFormat: (format: ConvertibleFormat) => void;
    onExportDat: () => void;
}

type OpenDialog = 'language' | 'copy' | 'convert' | 'export' | null;

export function LocDockActions(props: LocDockActionsProps): React.JSX.Element {
    const [dialog, setDialog] = useState<OpenDialog>(null);
    const [language, setLanguage] = useState('');
    const [fillFromBaseline, setFillFromBaseline] = useState(true);
    const [format, setFormat] = useState<ConvertibleFormat>('CSV');
    const [copyFrom, setCopyFrom] = useState('');
    const [copyTo, setCopyTo] = useState('');
    // Where the text comes from: another column of this file, or what the game itself ships.
    const [fillSource, setFillSource] = useState<FillSource>('language');
    const [seedFrom, setSeedFrom] = useState('');

    const close = (): void => setDialog(null);

    // What the engine supports and this file does not have yet. Nothing else is offerable, so
    // there is no free text to validate - picking from a list cannot produce a bad identifier.
    const have = new Set(props.languages.map(l => l.toUpperCase()));
    const addable = props.supportedLanguages.filter(l => !have.has(l.toUpperCase()));
    const fillCount = language === '' ? undefined : props.baselineFillCount?.(language);

    // Whether this file has a second language at all. The tile already gates on it; the radio
    // inside the dialog did not, so a single-language file offered "From another language" and
    // then a dropdown holding only the language being filled in.
    const canCopy = props.onCopyLanguage !== undefined && canCopyBetweenLanguages(props.languages);
    // Starting from a sibling file only makes sense for a file with nothing in it - otherwise it
    // would be appending someone else's list to content that is already there.
    const seedSources = props.seedSources ?? [];
    const canSeed = props.onSeedFrom !== undefined && props.fillsByAdding === true
        && seedSources.length > 0;
    // Resolved to exactly one source, so these three cannot disagree - see resolveFillSource for
    // what went wrong when they were derived independently.
    const source = resolveFillSource(fillSource, { canCopy, canSeed });
    const fromFile = source === 'file';
    const fromGame = source === 'baseline';
    const fromLanguage = source === 'language';
    const copyFromLanguages = copyFromOptions(props.languages, copyTo);

    // Also the whole confirm condition for the dialog: the source list excludes the target, and
    // languageCopyValues answers 0 for a language copied into itself in any case.
    const copyCount = fromFile
        ? seedSources.find(s => s.filePath === seedFrom)?.rowCount ?? 0
        : copyTo === ''
            ? 0
            : fromGame
                ? props.baselineFillCount?.(copyTo) ?? 0
                : copyFrom === '' ? 0 : props.copyLanguageCount?.(copyFrom, copyTo) ?? 0;

    // Re-aims the source when the target takes it, so the two can never name the same language.
    const changeCopyTo = (next: string): void => {
        setCopyTo(next);
        setCopyFrom(current => nextCopyFrom(props.languages, next, current));
    };

    const addLanguage = (): void => {
        if (language === '') { return; }
        props.onAddLanguage(language, fillFromBaseline && (fillCount ?? 0) > 0);
        close();
    };

    return (
        <>
            <DockSection title="File">
                <LocTileGrid>
                    {(props.canAddLanguage || props.addLanguageCreatesFile) && (
                        <LocTile
                            icon="add"
                            label="Add language"
                            title={props.addLanguageCreatesFile
                                ? 'Create a file for another language beside this one'
                                : 'Add a language column to this file'}
                            onClick={() => { setLanguage(''); setDialog('language'); }}
                        />
                    )}
                    {(props.onFillFromBaseline !== undefined || canCopy || canSeed) && (
                        <LocTile
                            icon="copy"
                            label="Fill language"
                            title="Copy one language into the cells another has left empty"
                            onClick={() => {
                                // Fill the second language from the first, which is the common
                                // case: the first is the one that was written, the rest follow it.
                                const target = props.languages[1] ?? props.languages[0] ?? '';
                                setCopyTo(target);
                                setCopyFrom(nextCopyFrom(props.languages, target, ''));
                                setSeedFrom(current => current || seedSources[0]?.filePath || '');
                                // A file with nothing in it is being started rather than topped up,
                                // and its own project's text beats the game's as a starting point.
                                // Otherwise the usual case is copying between its own columns.
                                setFillSource(props.fillsByAdding
                                    ? (canSeed ? 'file' : 'baseline')
                                    : canCopyBetweenLanguages(props.languages)
                                        ? 'language'
                                        : 'baseline');
                                setDialog('copy');
                            }}
                        />
                    )}
                    <LocTile
                        icon="replace-all"
                        label="Convert"
                        title="Write this file in another format, keeping the original"
                        onClick={() => setDialog('convert')}
                    />
                    <LocTile
                        icon="package"
                        label="Export DAT"
                        title="Write the .dat files the game loads"
                        onClick={() => setDialog('export')}
                    />
                </LocTileGrid>
            </DockSection>

            {dialog === 'language' && (
                <LocModal
                    dialogId={DIALOG_IDS.addLanguage}
                    // Two different things happen, so the dialog must not describe only one of
                    // them: a format that takes a column stages an edit, and a single-language
                    // format writes a whole new file on the spot.
                    footnote={props.addLanguageCreatesFile
                        ? 'A new file is written beside this one, carrying every key from it, '
                          + 'ready to translate.'
                        : 'The column is added to every row, and nothing reaches the file '
                          + 'until you save.'}
                    title="Add a language"
                    confirmLabel="Add"
                    canConfirm={language !== ''}
                    onCancel={close}
                    onConfirm={addLanguage}
                >
                    {addable.length === 0 ? (
                        <p className="modal-note">
                            This file already has every language the game supports.
                        </p>
                    ) : (
                        <>
                            <div className="choice-list">
                                {addable.map(l => (
                                    <label key={l} className={`choice${language === l ? ' selected' : ''}`}>
                                        <input
                                            type="radio"
                                            name="add-language"
                                            checked={language === l}
                                            onChange={() => setLanguage(l)}
                                        />
                                        <span className="choice-label">{l}</span>
                                    </label>
                                ))}
                            </div>

                            {/* Not offered when the language becomes a new file: creating one is
                                the host's job and cannot be staged with the rest of the batch, so
                                both editors return the moment they have asked for it and the fill
                                never runs. The checkbox was shown, ticked by default, and silently
                                discarded. Fill the new file from the game once it is open. */}
                            {props.baselineFillCount !== undefined
                                && !props.addLanguageCreatesFile && (
                                <label
                                    className="check-field"
                                    title="Copy the game's own text for this language into the keys it defines"
                                >
                                    <input
                                        type="checkbox"
                                        checked={fillFromBaseline}
                                        disabled={fillCount === 0}
                                        onChange={e => setFillFromBaseline(e.target.checked)}
                                    />
                                    <span>
                                        Fill from the game's translations
                                        {fillCount !== undefined && (
                                            <span className="check-detail">
                                                {fillCount === 0
                                                    ? ' - the game translates none of these keys'
                                                    : ` - ${fillCount} of ${props.rowCount} entries`}
                                            </span>
                                        )}
                                    </span>
                                </label>
                            )}

                        </>
                    )}
                </LocModal>
            )}

            {dialog === 'copy' && (
                <LocModal
                    dialogId={DIALOG_IDS.fillLanguage}
                    title="Fill in a language"
                    confirmLabel="Fill"
                    canConfirm={copyCount > 0}
                    onCancel={close}
                    onConfirm={() => {
                        if (fromFile) { props.onSeedFrom?.(seedFrom, copyTo); }
                        else if (fromGame) { props.onFillFromBaseline?.(copyTo); }
                        else { props.onCopyLanguage?.(copyFrom, copyTo); }
                        close();
                    }}
                >
                    <Field label="Fill in" as="label">
                        <select value={copyTo} onChange={e => changeCopyTo(e.target.value)}>
                            {props.languages.map(l => <option key={l} value={l}>{l}</option>)}
                        </select>
                    </Field>

                    <div className="choice-list">
                        {canCopy && (
                            <label className={`choice${fromLanguage ? ' selected' : ''}`}>
                                <input
                                    type="radio" name="fill-source" checked={fromLanguage}
                                    onChange={() => setFillSource('language')}
                                />
                                <span className="choice-label">From another language</span>
                                <span className="choice-detail">this file's own text</span>
                            </label>
                        )}
                        {props.onFillFromBaseline !== undefined && (
                            <label className={`choice${fromGame ? ' selected' : ''}`}>
                                <input
                                    type="radio" name="fill-source" checked={fromGame}
                                    onChange={() => setFillSource('baseline')}
                                />
                                <span className="choice-label">From the game</span>
                                <span className="choice-detail">what Empire at War already ships</span>
                            </label>
                        )}
                        {canSeed && (
                            <label className={`choice${fromFile ? ' selected' : ''}`}>
                                <input
                                    type="radio" name="fill-source" checked={fromFile}
                                    onChange={() => {
                                        setFillSource('file');
                                        // Pre-select, so the choice is complete the moment it is made.
                                        setSeedFrom(current =>
                                            current || seedSources[0]?.filePath || '');
                                    }}
                                />
                                <span className="choice-label">From another file</span>
                                <span className="choice-detail">
                                    this project&apos;s own text, ready to translate
                                </span>
                            </label>
                        )}
                    </div>

                    {fromFile && (
                        <Field label="Copy from" as="label">
                            <select value={seedFrom} onChange={e => setSeedFrom(e.target.value)}>
                                {seedSources.map(s => (
                                    <option key={s.filePath} value={s.filePath}>
                                        {s.language ? `${s.language} - ${s.label}` : s.label}
                                    </option>
                                ))}
                            </select>
                        </Field>
                    )}

                    {fromLanguage && (
                        <Field label="Copy from" as="label">
                            {/* The language being filled in is left out: copying it into itself
                                fills nothing, so offering it can only produce a disabled Fill. */}
                            <select value={copyFrom} onChange={e => setCopyFrom(e.target.value)}>
                                {copyFromLanguages.map(l => <option key={l} value={l}>{l}</option>)}
                            </select>
                        </Field>
                    )}

                    <p className="modal-note">
                        {fromFile
                            ? `${copyCount} line(s) would be copied in, in that file's order, `
                              + `as ${copyTo} - ready to translate over.`
                            : copyCount === 0
                                ? `There is nothing left to fill in for ${copyTo}.`
                                : props.fillsByAdding && fromGame
                                    ? `${copyCount} line(s) would be added, in the order the game `
                                      + 'plays them. Edit or delete what you do not want afterwards.'
                                    : `${copyCount} empty cell(s) would be filled. Anything `
                                      + `${copyTo} already says is left alone.`}
                    </p>
                    {fromLanguage && (
                        <p className="modal-note">
                            Most of a credits list is names, and a name is usually the same in every
                            language. Only do this where that holds: a cell left empty is dropped
                            from that language on export, which is how one file carries lists of
                            different lengths - a dub recorded with a smaller cast, for instance - so
                            filling those in would credit the wrong people.
                        </p>
                    )}
                </LocModal>
            )}

            {dialog === 'convert' && (
                <LocModal
                    dialogId={DIALOG_IDS.convertFormat}
                    footnote={'The new file is written beside this one and the original is kept.'}
                    title="Convert to another format"
                    confirmLabel="Convert"
                    onCancel={close}
                    onConfirm={() => { props.onConvertFormat(format); close(); }}
                >
                    <div className="choice-list">
                        {CONVERTIBLE_FORMATS.map(f => (
                            <label key={f} className={`choice${format === f ? ' selected' : ''}`}>
                                <input
                                    type="radio"
                                    name="convert-format"
                                    checked={format === f}
                                    onChange={() => setFormat(f)}
                                />
                                <span className="choice-label">{f}</span>
                                <span className="choice-detail">{FORMAT_DESCRIPTIONS[f]}</span>
                            </label>
                        ))}
                    </div>
                </LocModal>
            )}

            {dialog === 'export' && (
                <LocModal
                    dialogId={DIALOG_IDS.exportDat}
                    title="Export to DAT"
                    confirmLabel="Export"
                    onCancel={close}
                    onConfirm={() => { props.onExportDat(); close(); }}
                >
                    <p className="modal-note">
                        Writes the binary files the game loads, one per language, beside this file.
                        Existing .dat files with those names are overwritten.
                    </p>
                    <p className="modal-note">
                        This exports what is saved on disk, not what is staged - save first if you
                        want your pending edits included.
                    </p>
                </LocModal>
            )}
        </>
    );
}
