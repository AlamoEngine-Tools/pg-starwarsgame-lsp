// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { ReactNode, useEffect } from 'react';

import { ResizeHandles } from '../shared/ResizeHandles';
import { useMovableDialog } from '../shared/useMovableDialog';
import { Button } from '../shared/Button';

export interface LocModalProps {
    /** Identifies the dialog across sessions, so it reopens where it was last put. */
    dialogId: string;
    title: string;
    /** Label for the confirming button. Disabled while {@link canConfirm} is false. */
    confirmLabel: string;
    canConfirm?: boolean;
    onConfirm: () => void;
    onCancel: () => void;
    children: ReactNode;
    /**
     * A one-line hint shown on the button row, where it stays put however the body is scrolled.
     *
     * For a note about what confirming does. A note explaining a particular control belongs beside
     * that control, in {@link children} - this row has space for a line, not a paragraph.
     */
    footnote?: ReactNode;
}

/**
 * A modal for the file-level actions.
 *
 * These change the file as a whole - another language column, a different format on disk, a set of
 * .dat files written beside it - so each one gets a moment to read what it is about to do and a way
 * to back out, rather than happening the instant a control is touched.
 *
 * Movable by its title bar and resizable from any edge or corner, so a dialog listing a long set of
 * languages or files can be enlarged and pushed aside to read the grid underneath it.
 */
export function LocModal(props: LocModalProps): React.JSX.Element {
    const { onCancel } = props;
    const { dialogProps, dragHandleProps, resizeHandleProps } = useMovableDialog(props.dialogId);

    // Escape closes from anywhere in the dialog, including the buttons.
    useEffect(() => {
        const onKey = (e: KeyboardEvent): void => {
            if (e.key === 'Escape') { onCancel(); }
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onCancel]);

    return (
        <div className="modal-backdrop" onMouseDown={e => {
            // Only a click that both starts and ends on the backdrop dismisses - dragging a
            // selection out of the input must not close the dialog under the pointer.
            if (e.target === e.currentTarget) { onCancel(); }
        }}>
            <div
                className="modal"
                role="dialog"
                aria-modal="true"
                aria-label={props.title}
                {...dialogProps}
            >
                <div className="modal-title drag-handle" {...dragHandleProps}>{props.title}</div>
                <div className="modal-body">{props.children}</div>
                <div className="modal-buttons">
                    {props.footnote !== undefined && (
                        <p className="modal-footnote">
                            <span className="codicon codicon-info" aria-hidden="true" />
                            {props.footnote}
                        </p>
                    )}
                    <Button onClick={onCancel}>Cancel</Button>
                    <Button
                        className="primary"
                        disabled={props.canConfirm === false}
                        disabledReason="Fill the fields above first"
                        onClick={props.onConfirm}
                    >
                        {props.confirmLabel}
                    </Button>
                </div>
                <ResizeHandles handleProps={resizeHandleProps} />
            </div>
        </div>
    );
}
