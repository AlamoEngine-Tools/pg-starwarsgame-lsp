// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import {createContext, ReactNode, useContext, useEffect} from 'react';
import {createPortal} from 'react-dom';

import {ResizeHandles} from './ResizeHandles';
import {useMovableDialog} from './useMovableDialog';
import {Button} from './Button';

/**
 * Where a host wants its modals rendered. A modal opened from inside a positioned panel with its
 * own z-index would otherwise stack under everything outside that panel; the host puts one element
 * at the top of its own stacking order and provides it here. Without one, a modal renders in place.
 */
export const ModalLayerContext = createContext<HTMLElement | null>(null);

export interface ModalProps {
    /** Identifies the dialog across sessions, so it reopens where it was last put. */
    dialogId?: string;
    title: string;
    /** Label for the confirming button. Disabled while {@link canConfirm} is false. */
    confirmLabel?: string;
    canConfirm?: boolean;
    /** Why the confirming button is disabled, shown on it while {@link canConfirm} is false. */
    disabledReason?: string;
    /**
     * What confirming does. Omitted for a dialog with nothing to confirm - one that shows and acts
     * through its {@link actions} - which then closes with a single Close button.
     */
    onConfirm?: () => void;
    onCancel: () => void;
    children: ReactNode;
    /**
     * A short form - a field or two and OK. Not resizable, and its body does not clip, so a field's
     * dropdown can hang below it; Enter in a field confirms.
     */
    form?: boolean;
    /**
     * A one-line hint shown on the button row, where it stays put however the body is scrolled.
     *
     * For a note about what confirming does. A note explaining a particular control belongs beside
     * that control, in {@link children} - this row has space for a line, not a paragraph.
     */
    footnote?: ReactNode;
    /**
     * Answers that act at once rather than on OK, on the left of the button row - e.g. the
     * simulator's "fire it anyway" and "rule it out" beside the value a decision is waiting for.
     */
    actions?: ReactNode;
}

/**
 * A modal: a moment to read what is about to happen and a way to back out, rather than the change
 * happening the instant a control is touched.
 *
 * Movable by its title bar and, unless it is a {@link ModalProps.form}, resizable from any edge or
 * corner, so a dialog listing a long set of languages or files can be enlarged and pushed aside to
 * read what is underneath it.
 */
export function Modal(props: ModalProps): React.JSX.Element {
    const {onCancel} = props;
    const {dialogProps, dragHandleProps, resizeHandleProps} = useMovableDialog(props.dialogId);
    const layer = useContext(ModalLayerContext);
    const confirmable = props.canConfirm !== false;

    // Escape closes from anywhere in the dialog, including the buttons. A field whose own list is
    // open stops the Escape that closes the list, so that one never reaches here.
    useEffect(() => {
        const onKey = (e: KeyboardEvent): void => {
            if (e.key === 'Escape') {
                onCancel();
            }
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onCancel]);

    const dialog = (
        <div className="modal-backdrop" onMouseDown={e => {
            // Only a click that both starts and ends on the backdrop dismisses - dragging a
            // selection out of the input must not close the dialog under the pointer.
            if (e.target === e.currentTarget) {
                onCancel();
            }
        }}>
            <div
                className={props.form ? 'modal modal-form' : 'modal'}
                role="dialog"
                aria-modal="true"
                aria-label={props.title}
                {...dialogProps}
                onKeyDown={e => {
                    // Enter in a form's field confirms. A field that used the key for itself (a
                    // dropdown taking its highlighted option) has already prevented the default.
                    if (props.form && props.onConfirm && e.key === 'Enter' && !e.defaultPrevented
                        && (e.target as HTMLElement).tagName === 'INPUT' && confirmable) {
                        e.preventDefault();
                        props.onConfirm();
                    }
                }}
            >
                <div className="modal-title drag-handle" {...dragHandleProps}>{props.title}</div>
                <div className="modal-body">{props.children}</div>
                <div className="modal-buttons">
                    {props.actions !== undefined && <div className="modal-actions">{props.actions}</div>}
                    {props.footnote !== undefined && (
                        <p className="modal-footnote">
                            <span className="codicon codicon-info" aria-hidden="true"/>
                            {props.footnote}
                        </p>
                    )}
                    {props.onConfirm ? (
                        <>
                            <Button onClick={onCancel}>Cancel</Button>
                            <Button
                                className="primary"
                                disabled={!confirmable}
                                disabledReason={props.disabledReason ?? 'Fill the fields above first'}
                                onClick={props.onConfirm}
                            >
                                {props.confirmLabel ?? 'OK'}
                            </Button>
                        </>
                    ) : <Button onClick={onCancel}>Close</Button>}
                </div>
                {props.form ? null : <ResizeHandles handleProps={resizeHandleProps}/>}
            </div>
        </div>
    );
    return layer ? createPortal(dialog, layer) : dialog;
}
