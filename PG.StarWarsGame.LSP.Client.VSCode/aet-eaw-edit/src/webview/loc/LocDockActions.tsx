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

import { LocModal } from './LocModal';
import { LocTile, LocTileGrid } from './LocTile';

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
    const [fillSource, setFillSource] = useState<'language' | 'baseline'>('language');

    const close = (): void => setDialog(null);

    // What the engine supports and this file does not have yet. Nothing else is offerable, so
    // there is no free text to validate - picking from a list cannot produce a bad identifier.
    const have = new Set(props.languages.map(l => l.toUpperCase()));
    const addable = props.supportedLanguages.filter(l => !have.has(l.toUpperCase()));
    const fillCount = language === '' ? undefined : props.baselineFillCount?.(language);

    const fromGame = fillSource === 'baseline';
    const copyCount = copyTo === ''
        ? 0
        : fromGame
            ? props.baselineFillCount?.(copyTo) ?? 0
            : copyFrom === '' ? 0 : props.copyLanguageCount?.(copyFrom, copyTo) ?? 0;

    const addLanguage = (): void => {
        if (language === '') { return; }
        props.onAddLanguage(language, fillFromBaseline && (fillCount ?? 0) > 0);
        close();
    };

    return (
        <>
            <div className="dock-section">
                <div className="dock-section-title">File</div>
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
                    {(props.onFillFromBaseline !== undefined
                        || (props.onCopyLanguage !== undefined && props.languages.length > 1)) && (
                        <LocTile
                            icon="copy"
                            label="Fill language"
                            title="Copy one language into the cells another has left empty"
                            onClick={() => {
                                setCopyFrom(props.languages[0] ?? '');
                                setCopyTo(props.languages[1] ?? props.languages[0] ?? '');
                                setFillSource(props.languages.length > 1 ? 'language' : 'baseline');
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
            </div>

            {dialog === 'language' && (
                <LocModal
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

                            {props.baselineFillCount !== undefined && (
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

                            <p className="modal-note">
                                The column is added to every row. Entries the game does not translate
                                are left empty. Nothing reaches the file until you save.
                            </p>
                        </>
                    )}
                </LocModal>
            )}

            {dialog === 'copy' && (
                <LocModal
                    title="Fill in a language"
                    confirmLabel="Fill"
                    canConfirm={copyCount > 0 && (fromGame || copyFrom !== copyTo)}
                    onCancel={close}
                    onConfirm={() => {
                        if (fromGame) { props.onFillFromBaseline?.(copyTo); }
                        else { props.onCopyLanguage?.(copyFrom, copyTo); }
                        close();
                    }}
                >
                    <label className="field">
                        <span className="field-label">Fill in</span>
                        <select value={copyTo} onChange={e => setCopyTo(e.target.value)}>
                            {props.languages.map(l => <option key={l} value={l}>{l}</option>)}
                        </select>
                    </label>

                    <div className="choice-list">
                        {props.onCopyLanguage !== undefined && (
                            <label className={`choice${!fromGame ? ' selected' : ''}`}>
                                <input
                                    type="radio" name="fill-source" checked={!fromGame}
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
                    </div>

                    {!fromGame && (
                        <label className="field">
                            <span className="field-label">Copy from</span>
                            <select value={copyFrom} onChange={e => setCopyFrom(e.target.value)}>
                                {props.languages.map(l => <option key={l} value={l}>{l}</option>)}
                            </select>
                        </label>
                    )}

                    <p className="modal-note">
                        {!fromGame && copyFrom === copyTo
                            ? 'Pick two different languages.'
                            : copyCount === 0
                                ? `There is nothing left to fill in for ${copyTo}.`
                                : `${copyCount} empty cell(s) would be filled. Anything ${copyTo} `
                                  + 'already says is left alone.'}
                    </p>
                    {!fromGame && (
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
                    <p className="modal-note">
                        The new file is written beside this one and the project is repointed at it.
                        The original file is kept.
                    </p>
                </LocModal>
            )}

            {dialog === 'export' && (
                <LocModal
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
