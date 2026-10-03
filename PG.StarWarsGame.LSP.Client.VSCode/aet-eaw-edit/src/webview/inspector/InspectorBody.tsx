// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What one tree row IS, laid out as a page.
//
// Takes plain props and posts nothing: the tab around it owns the messaging, so everything here can
// be read - and later driven by the harness - without a webview. The sections come from
// `inspectPanels`, the same function the preview's flyout used, so moving the inspector into its
// own tab did not fork how a mesh's facts are described.

import { Fragment } from 'react';

import { inspectPanels } from '../preview/inspector';
import { geometryKeyOf, type InspectorSubject } from '../preview/inspectorSubject';
import { type GeometryRows } from '../preview/geometryRows';
import { GeometryRowsTable, GeometryTabs } from './GeometryTables';
import { type GeometryTable } from '../../protocol/modelPreview';

export function InspectorBody(
    { subject, geometry, geometryError, onGeometry, onOpenTable }: {
        /** The row being described, or null before the preview has sent one. */
        subject: InspectorSubject | null;
        /** Everything gathered for the open table, or null before one is opened. */
        geometry: GeometryRows | null;
        geometryError: string | null;
        /** Fetches one more page of the open table. */
        onGeometry: (table: GeometryTable, offset: number) => void;
        /** Switches tab, which starts that table again from row zero. */
        onOpenTable: (table: GeometryTable) => void;
    },
): React.JSX.Element {
    if (subject === null) {
        return (
            <div className="inspect-empty">
                <p>No selection.</p>
                <p className="field-note">
                    Inspect a bone, mesh or effect from the model preview tree.
                </p>
            </div>
        );
    }

    const panels = subject.sources.length === 0
        ? null
        : inspectPanels(subject.sources, subject.modelDetail ?? undefined);

    if (panels === null) {
        return (
            <div className="inspect-empty">
                <p>This item is no longer in the model.</p>
            </div>
        );
    }

    const key = geometryKeyOf(subject);

    return (
        <>
            <div className="inspect-page-head">
                <h1 className="inspect-page-title">{panels.title}</h1>
                {panels.subtitle !== undefined && (
                    <span className="inspect-page-subtitle">{panels.subtitle}</span>
                )}
            </div>

            {/* Two columns where there is room for them. The sections are short and independent -
                Mesh, Material, Bone - and stacking them down a full-width tab left the reader
                scrolling past a column of whitespace to reach the geometry. */}
            <div className="inspect-columns">
                {panels.sections.map(section => (
                    <div className="inspect-group" key={section.title}>
                        <div className="inspect-title">{section.title}</div>

                        {section.note !== undefined && (
                            <div className="field-note">{section.note}</div>
                        )}

                        {/* dt and dd stay DIRECT children of the list: `.inspect-rows` is a
                            two-column grid and a wrapper element around each pair would become the
                            grid item instead, collapsing both into one column. */}
                        <dl className="inspect-rows">
                            {section.rows.map((row, at) => (
                                <Fragment key={`${section.title}:${at}`}>
                                    <dt title={row.hint ?? row.label}>{row.label}</dt>
                                    <dd className={row.kind} title={row.hint ?? row.value}>
                                        {row.swatch !== undefined && (
                                            <span
                                                className="inspect-swatch"
                                                style={{ background: row.swatch }}
                                            />
                                        )}
                                        {row.value}
                                    </dd>
                                </Fragment>
                            ))}
                        </dl>
                    </div>
                ))}
            </div>

            {/* The bulk tables, as a tabbed box.

                Still a deliberate press rather than something the tab loads with: this is the only
                genuinely large thing the inspector can ask for. A row with no geometry - a bone, a
                particle system - shows the tabs inert rather than dropping them, so the reader can
                see the tables exist and that this row has none. */}
            <div className="inspect-group inspect-geometry">
                <div className="inspect-title">Geometry</div>

                <GeometryTabs rows={geometry} disabled={key === null} onSelect={onOpenTable} />

                {key === null && (
                    <div className="field-note">
                        This item has no geometry.
                    </div>
                )}

                {geometryError !== null && <div className="field-note">{geometryError}</div>}

                {geometry !== null && <GeometryRowsTable rows={geometry} onMore={onGeometry} />}
            </div>
        </>
    );
}
