// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The clip library, grouped by family and action, each action a tile that plays it.

import type { AnimationTransport } from "../useAnimationTransport";
import * as l10n from "@vscode/l10n";
import { DockSection } from "../../shared/DockSection";
import { Icon } from "../../shared/Icon";
import { AnimationTile } from "../AnimationTile";

export interface AnimationsSectionProps {
    player: AnimationTransport;
}

export function AnimationsSection({
    player,
}: AnimationsSectionProps): React.JSX.Element {
    return (
        <DockSection
            title={l10n.t("Animations")}
            count={player.animations.length}
        >
            {player.animations.length === 0 ? (
                <div className="field-note">
                    {l10n.t(
                        "This model carries no animations. An .ala beside it only counts when its skeleton matches.",
                    )}
                </div>
            ) : (
                <div className="anim-list">
                    {/* A dropdown made every clip but one invisible, and hid
                                            the one thing a modder wants from an animation list:
                                            what KINDS of motion this unit has. */}
                    <button
                        type="button"
                        className={
                            "anim-row" +
                            (player.animation === null ? " active" : "")
                        }
                        title={l10n.t("Stop - show the rest pose")}
                        onClick={() => player.setAnimation(null)}
                    >
                        <Icon name="stop" />
                        <span className="anim-name">{l10n.t("Rest pose")}</span>
                    </button>

                    {/* Family, then action, then its takes.

                                            Three levels because the names carry three facts and
                                            flattening them lost two: nine idle files read as nine
                                            idles rather than as ONE idle the engine has nine
                                            recordings of, and `crouchidle` read as its own kind of
                                            motion rather than as the idle held while the unit is
                                            spread out. See `animationNames.ts`.

                                            Tiles and a foldable heading, which is the shape the
                                            story graph's palette already uses for its events and
                                            rewards - the reader asked for that one by name. */}
                    {player.animationGroups.map((group) => {
                        const folded = player.foldedFamilies.has(group.family);

                        return (
                            <div
                                className="dock-section anim-family"
                                key={group.family}
                            >
                                <div
                                    className="dock-section-title as-fold"
                                    role="button"
                                    tabIndex={0}
                                    aria-expanded={!folded}
                                    title={
                                        folded
                                            ? l10n.t(
                                                  "Open {0}",
                                                  group.label.toLowerCase(),
                                              )
                                            : l10n.t(
                                                  "Fold {0} away",
                                                  group.label.toLowerCase(),
                                              )
                                    }
                                    onClick={() =>
                                        player.toggleFamily(group.family)
                                    }
                                    onKeyDown={(event) => {
                                        if (
                                            event.key === "Enter" ||
                                            event.key === " "
                                        ) {
                                            event.preventDefault();
                                            player.toggleFamily(group.family);
                                        }
                                    }}
                                >
                                    <Icon
                                        name={folded ? "collapsed" : "expanded"}
                                        size={13}
                                    />
                                    {group.label}
                                    <span className="title-end">
                                        <span className="section-count">
                                            {group.actions.length}
                                        </span>
                                    </span>
                                </div>

                                {!folded && (
                                    <div className="anim-list">
                                        {group.actions.map((action) => (
                                            <AnimationTile
                                                key={action.id}
                                                action={action}
                                                playing={player.animation}
                                                running={player.playing}
                                                onPress={player.pressAction}
                                                onPressTake={player.pressTake}
                                            />
                                        ))}
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            )}
        </DockSection>
    );
}
