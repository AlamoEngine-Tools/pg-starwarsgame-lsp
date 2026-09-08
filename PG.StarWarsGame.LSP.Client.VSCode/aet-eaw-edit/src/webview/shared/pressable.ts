// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The one rule every pressable control in this extension obeys, expressed as a type.
//
// Disable, don't hide, and say why. A control with nothing to act on stays on screen, because the
// reader needs to see that the choice exists - but a disabled control that gives no reason is worse
// than a missing one: the reader is left guessing what would make it work.
//
// It lives here rather than in Button.tsx because it is not a fact about buttons. An option in a
// one-of-N choice obeys it too, and stated as a doc comment on that component it was advice; a
// union is a rule.

/**
 * Either the control always acts, or it can be disabled AND says why.
 *
 * It discriminates on whether `disabled` is passed at all rather than on its value, because in
 * practice it is always a runtime expression - `disabled={animation === null}` - which TypeScript
 * cannot narrow to a literal. So the rule is: mention `disabled` and you owe a reason.
 *
 * A good reason still NAMES the thing - "Damage log - nothing fired yet", not "Nothing fired yet".
 * A tooltip giving only the reason leaves a reader who has never opened it none the wiser about
 * what it was going to be.
 */
export type Enablement =
    | { disabled?: undefined; disabledReason?: never }
    | { disabled: boolean; disabledReason: string };
