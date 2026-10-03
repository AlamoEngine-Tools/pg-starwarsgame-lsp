#!/usr/bin/env bash
# Sets up the repositories the LSP builds against, as listed in workspace.deps.
#
#   ./setup-workspace.sh [sync|check|record] [options]
#
#   sync (default)  Clone what is missing, fast-forward owned repos on their default branch, move
#                   external repos to their pinned commit. Never touches a repo with local work.
#   check           Report the state of every repo without changing anything but remote refs.
#   record          Write the current commit of every external repo into workspace.deps.
#
#   --groups a,b           Groups to act on: build, e2e. Default: build
#   --match-branch <name>  Owned repos with a remote branch of this name use it instead of their
#                          default branch. CI passes the pull request's branch.
#   --manifest <path>      Read another manifest. Paths in it stay relative to this repo.
#   --summary <path>       Append the status lines to this file (GitHub step summary).
#   --no-self-update       Do not fast-forward this repo before syncing.
#
# Exit codes: 0 done, 1 a repo failed, 2 bad arguments or manifest.
#
# setup-workspace.ps1 is the PowerShell twin of this script. Both print the same status lines;
# scripts/workspace-setup.test.js runs them side by side and fails when they differ.

set -u

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
SCRIPT_PATH="$SCRIPT_DIR/$(basename "${BASH_SOURCE[0]}")"
ROOT=$SCRIPT_DIR

COMMAND=sync
GROUPS_ARG=build
MATCH=""
MANIFEST=""
SUMMARY=""
NO_SELF_UPDATE=0
ORIG_ARGS=("$@")

FAILED=0
OUT=()

R_NAME=()
R_PATH=()
R_GROUP=()
R_REF=()
R_URL=()
SELECTED=()

# ── Output ───────────────────────────────────────────────────────────────────

emit() {
  local line="[$1] $2 - $3"
  printf '%s\n' "$line"
  OUT+=("$line")
  if [ "$1" = fail ]; then FAILED=1; fi
}

usage_error() {
  printf '%s\n' "$1" >&2
  exit 2
}

manifest_error() {
  printf '%s:%s - %s\n' "$(basename "$MANIFEST")" "$1" "$2" >&2
  exit 2
}

# ── Arguments ────────────────────────────────────────────────────────────────

parse_args() {
  while [ $# -gt 0 ]; do
    case $1 in
      sync | check | record) COMMAND=$1 ;;
      --groups)
        [ $# -ge 2 ] || usage_error "--groups needs a value"
        GROUPS_ARG=$2
        shift
        ;;
      --match-branch)
        [ $# -ge 2 ] || usage_error "--match-branch needs a value"
        MATCH=$2
        shift
        ;;
      --manifest)
        [ $# -ge 2 ] || usage_error "--manifest needs a value"
        MANIFEST=$2
        shift
        ;;
      --summary)
        [ $# -ge 2 ] || usage_error "--summary needs a value"
        SUMMARY=$2
        shift
        ;;
      --no-self-update) NO_SELF_UPDATE=1 ;;
      -h | --help)
        sed -n '2,20p' "$SCRIPT_PATH" | sed 's/^# \{0,1\}//'
        exit 0
        ;;
      *) usage_error "Unknown argument: $1" ;;
    esac
    shift
  done

  local g
  local IFS=,
  for g in $GROUPS_ARG; do
    case $g in
      build | e2e) SELECTED+=("$g") ;;
      *) usage_error "Unknown group: $g" ;;
    esac
  done
  [ ${#SELECTED[@]} -gt 0 ] || usage_error "--groups needs at least one group"

  [ -n "$MANIFEST" ] || MANIFEST="$ROOT/workspace.deps"
}

is_selected() {
  local g
  for g in "${SELECTED[@]}"; do
    [ "$g" = "$1" ] && return 0
  done
  return 1
}

# ── Manifest ─────────────────────────────────────────────────────────────────

# A path stays inside the workspace: relative, forward slashes, at most one leading "..".
valid_path() {
  case $1 in
    /* | \\* | [A-Za-z]:* | *\\*) return 1 ;;
  esac
  local seg i=0
  local IFS=/
  set -f
  # shellcheck disable=SC2086
  set -- $1
  set +f
  [ $# -ge 1 ] || return 1
  for seg in "$@"; do
    i=$((i + 1))
    case $seg in
      '' | .) return 1 ;;
      ..) { [ $i -eq 1 ] && [ $# -ge 2 ]; } || return 1 ;;
    esac
  done
  return 0
}

read_manifest() {
  [ -f "$MANIFEST" ] || usage_error "Manifest not found: $MANIFEST"
  local raw line lineno=0 name path group ref url n
  while IFS= read -r raw || [ -n "$raw" ]; do
    lineno=$((lineno + 1))
    line=${raw%$'\r'}
    line=${line%%#*}
    set -f
    # shellcheck disable=SC2086
    set -- $line
    set +f
    [ $# -eq 0 ] && continue
    [ $# -eq 5 ] || manifest_error "$lineno" "Expected 5 columns, found $#"
    name=$1 path=$2 group=$3 ref=$4 url=$5

    [[ $name =~ ^[A-Za-z0-9._-]+$ ]] || manifest_error "$lineno" "Invalid name: $name"
    for n in ${R_NAME[@]+"${R_NAME[@]}"}; do
      [ "$n" = "$name" ] && manifest_error "$lineno" "Duplicate name: $name"
    done
    valid_path "$path" || manifest_error "$lineno" "Path must stay inside the workspace: $path"
    case $group in
      build | e2e) ;;
      *) manifest_error "$lineno" "Unknown group: $group" ;;
    esac
    [ "$ref" = default ] || [[ $ref =~ ^[0-9a-f]{40}$ ]] ||
      manifest_error "$lineno" "Ref must be \"default\" or a 40-character commit SHA: $ref"
    [[ $url =~ ^(https?|file|ssh)://[^[:space:]]+$ || $url =~ ^git@[^[:space:]]+$ ]] ||
      manifest_error "$lineno" "Unsupported URL: $url"

    R_NAME+=("$name")
    R_PATH+=("$path")
    R_GROUP+=("$group")
    R_REF+=("$ref")
    R_URL+=("$url")
  done <"$MANIFEST"
}

# ── Git helpers ──────────────────────────────────────────────────────────────

short() { printf '%s' "${1:0:7}"; }

normalize_url() {
  printf '%s' "$1" | tr 'A-Z' 'a-z' | sed -e 's#/*$##' -e 's#\.git$##'
}

# The remote of an existing clone whose URL is the manifest's, so a fork as origin still works.
find_remote() {
  local dir=$1 want r
  want=$(normalize_url "$2")
  for r in $(git -C "$dir" remote 2>/dev/null); do
    if [ "$(normalize_url "$(git -C "$dir" remote get-url "$r" 2>/dev/null)")" = "$want" ]; then
      printf '%s' "$r"
      return 0
    fi
  done
  return 1
}

remote_default() {
  git ls-remote --symref "$1" HEAD 2>/dev/null | awk '$1 == "ref:" { sub("refs/heads/", "", $2); print $2; exit }'
}

remote_has_branch() {
  [ -n "$(git ls-remote --heads "$1" "refs/heads/$2" 2>/dev/null)" ]
}

current_branch() { git -C "$1" symbolic-ref --short -q HEAD 2>/dev/null; }

head_of() { git -C "$1" rev-parse HEAD 2>/dev/null; }

# Untracked files never block a fast-forward that does not touch them, so they do not count.
is_dirty() { [ -n "$(git -C "$1" status --porcelain --untracked-files=no 2>/dev/null)" ]; }

has_local_only() { [ -n "$(git -C "$1" rev-list -n 1 HEAD --not --remotes 2>/dev/null)" ]; }

has_commit() { git -C "$1" cat-file -e "$2^{commit}" 2>/dev/null; }

on_remote() { [ -n "$(git -C "$1" for-each-ref --contains "$3" "refs/remotes/$2/" 2>/dev/null)" ]; }

# Makes sure a pinned commit is present, fetching it by SHA if no branch carried it.
ensure_commit() {
  has_commit "$1" "$3" && return 0
  git -C "$1" fetch --quiet "$2" "$3" 2>/dev/null
  has_commit "$1" "$3"
}

# Common opening for an existing repo: is it one, which remote, fetch. Sets REMOTE.
open_existing() {
  local name=$1 dir=$2 url=$3
  REMOTE=""
  if [ ! -e "$dir/.git" ]; then
    emit fail "$name" "Exists but is not a git repository"
    return 1
  fi
  if ! REMOTE=$(find_remote "$dir" "$url"); then
    emit fail "$name" "No remote points at $url"
    return 1
  fi
  if ! git -C "$dir" fetch --quiet "$REMOTE"; then
    emit fail "$name" "Fetch from $REMOTE failed"
    return 1
  fi
  return 0
}

# ── sync ─────────────────────────────────────────────────────────────────────

# Fast-forwards the checked-out branch $3 to $REMOTE/$3. $4 is the label used in messages.
fast_forward() {
  local name=$1 dir=$2 want=$3 label=$4 tracking="$REMOTE/$3" ahead behind
  if ! git -C "$dir" rev-parse -q --verify "refs/remotes/$tracking" >/dev/null; then
    emit note "$name" "$tracking not found, left alone"
    return
  fi
  behind=$(git -C "$dir" rev-list --count "HEAD..$tracking")
  ahead=$(git -C "$dir" rev-list --count "$tracking..HEAD")
  if [ "$behind" -eq 0 ] && [ "$ahead" -eq 0 ]; then
    emit ok "$name" "Up to date on $label"
  elif [ "$ahead" -eq 0 ]; then
    git -C "$dir" merge --ff-only --quiet "$tracking"
    emit ok "$name" "Fast-forwarded $label to $(short "$(head_of "$dir")")"
  elif [ "$behind" -eq 0 ]; then
    emit note "$name" "$want is ahead of $tracking by $ahead, left alone"
  else
    emit note "$name" "$want diverged from $tracking, not merged"
  fi
}

sync_owned() {
  local name=$1 dir=$2 url=$3 def want label matched=0 cur
  if ! def=$(remote_default "$url") || [ -z "$def" ]; then
    emit fail "$name" "Cannot reach $url"
    return
  fi
  want=$def
  if [ -n "$MATCH" ] && remote_has_branch "$url" "$MATCH"; then
    want=$MATCH
    matched=1
  fi
  label=$want
  [ $matched -eq 1 ] && label="$want (matched)"

  if [ ! -e "$dir" ]; then
    if git clone --quiet -c core.longpaths=true --branch "$want" "$url" "$dir"; then
      emit ok "$name" "Cloned on $label"
    else
      emit fail "$name" "Clone failed"
    fi
    return
  fi

  open_existing "$name" "$dir" "$url" || return
  if is_dirty "$dir"; then
    emit note "$name" "Uncommitted changes, left alone"
    return
  fi
  cur=$(current_branch "$dir")
  if [ "$cur" = "$want" ]; then
    fast_forward "$name" "$dir" "$want" "$label"
  elif [ $matched -eq 1 ]; then
    if has_local_only "$dir"; then
      emit note "$name" "Has commits on no remote, left alone"
      return
    fi
    if git -C "$dir" rev-parse -q --verify "refs/heads/$want" >/dev/null; then
      git -C "$dir" checkout --quiet "$want" &&
        git -C "$dir" merge --ff-only --quiet "$REMOTE/$want" 2>/dev/null
    else
      git -C "$dir" checkout --quiet -b "$want" --track "$REMOTE/$want"
    fi
    if [ "$(current_branch "$dir")" = "$want" ]; then
      emit ok "$name" "Switched to $label at $(short "$(head_of "$dir")")"
    else
      emit fail "$name" "Could not switch to $want"
    fi
  elif [ -z "$cur" ]; then
    emit note "$name" "Detached HEAD, left alone"
  else
    emit note "$name" "On branch $cur, left alone"
  fi
}

sync_pinned() {
  local name=$1 dir=$2 url=$3 pin=$4 s
  s=$(short "$pin")

  if [ ! -e "$dir" ]; then
    if ! git clone --quiet -c core.longpaths=true "$url" "$dir"; then
      emit fail "$name" "Clone failed"
      return
    fi
    if ensure_commit "$dir" origin "$pin" &&
      git -C "$dir" -c advice.detachedHead=false checkout --quiet --detach "$pin"; then
      emit ok "$name" "Cloned at pin $s"
    else
      emit fail "$name" "Pin $s not on $url"
    fi
    return
  fi

  open_existing "$name" "$dir" "$url" || return
  if [ "$(head_of "$dir")" = "$pin" ]; then
    emit ok "$name" "At pin $s"
  elif is_dirty "$dir"; then
    emit fail "$name" "Uncommitted changes, not moved to pin $s"
  elif has_local_only "$dir"; then
    emit fail "$name" "Has commits on no remote, not moved to pin $s"
  elif ! ensure_commit "$dir" "$REMOTE" "$pin"; then
    emit fail "$name" "Pin $s not on $url"
  elif git -C "$dir" -c advice.detachedHead=false checkout --quiet --detach "$pin"; then
    emit ok "$name" "Moved to pin $s"
  else
    emit fail "$name" "Could not check out pin $s"
  fi
}

# ── check ────────────────────────────────────────────────────────────────────

check_owned() {
  local name=$1 dir=$2 url=$3 def want label cur level msg ahead behind tracking
  if [ ! -e "$dir" ]; then
    emit fail "$name" "Missing"
    return
  fi
  open_existing "$name" "$dir" "$url" || return
  def=$(remote_default "$url")
  want=$def
  label=$want
  if [ -n "$MATCH" ] && remote_has_branch "$url" "$MATCH"; then
    want=$MATCH
    label="$want (matched)"
  fi
  tracking="$REMOTE/$want"
  cur=$(current_branch "$dir")
  level=ok
  if [ -z "$cur" ]; then
    level=note msg="Detached HEAD"
  elif [ "$cur" != "$want" ]; then
    level=note msg="On branch $cur"
  else
    behind=$(git -C "$dir" rev-list --count "HEAD..$tracking" 2>/dev/null || echo 0)
    ahead=$(git -C "$dir" rev-list --count "$tracking..HEAD" 2>/dev/null || echo 0)
    if [ "$behind" -eq 0 ] && [ "$ahead" -eq 0 ]; then
      msg="Up to date on $label"
    elif [ "$ahead" -eq 0 ]; then
      level=note msg="Behind $tracking by $behind"
    elif [ "$behind" -eq 0 ]; then
      level=note msg="Ahead of $tracking by $ahead"
    else
      level=note msg="Diverged from $tracking"
    fi
  fi
  if is_dirty "$dir"; then
    level=note msg="$msg, uncommitted changes"
  fi
  emit "$level" "$name" "$msg"
}

check_pinned() {
  local name=$1 dir=$2 url=$3 pin=$4 s msg
  s=$(short "$pin")
  if [ ! -e "$dir" ]; then
    emit fail "$name" "Missing"
    return
  fi
  open_existing "$name" "$dir" "$url" || return
  if ! ensure_commit "$dir" "$REMOTE" "$pin" || ! on_remote "$dir" "$REMOTE" "$pin"; then
    emit fail "$name" "Pin $s not on $url"
  elif [ "$(head_of "$dir")" = "$pin" ]; then
    if is_dirty "$dir"; then
      emit note "$name" "At pin $s, uncommitted changes"
    else
      emit ok "$name" "At pin $s"
    fi
  else
    msg="Not at pin $s"
    is_dirty "$dir" && msg="$msg, uncommitted changes"
    emit fail "$name" "$msg"
  fi
}

# ── record ───────────────────────────────────────────────────────────────────

REC_OLD=()
REC_NEW=()

record_pinned() {
  local name=$1 dir=$2 url=$3 pin=$4 head
  if [ ! -e "$dir" ]; then
    emit fail "$name" "Missing"
    return
  fi
  open_existing "$name" "$dir" "$url" || return
  head=$(head_of "$dir")
  if is_dirty "$dir"; then
    emit fail "$name" "Uncommitted changes, not recorded"
  elif ! on_remote "$dir" "$REMOTE" "$head"; then
    emit fail "$name" "$(short "$head") is on no remote branch, not recorded"
  elif [ "$head" = "$pin" ]; then
    emit ok "$name" "Unchanged at $(short "$head")"
  else
    REC_OLD+=("$pin")
    REC_NEW+=("$head")
    emit ok "$name" "Pinned $(short "$head"), was $(short "$pin")"
  fi
}

write_records() {
  local i tmp="$MANIFEST.tmp"
  for i in ${REC_OLD[@]+"${!REC_OLD[@]}"}; do
    sed "s/${REC_OLD[$i]}/${REC_NEW[$i]}/" "$MANIFEST" >"$tmp" && mv -f "$tmp" "$MANIFEST"
  done
}

# ── Self-update ──────────────────────────────────────────────────────────────

# Fast-forwards this repo when it is clean and on its default branch, then starts the updated
# script with the same arguments. bash reads a script while running it, so continuing in the old
# process after the file changed underneath it would run a mix of old and new code.
self_update() {
  [ -e "$ROOT/.git" ] || return 0
  local cur upstream remote def before
  cur=$(current_branch "$ROOT")
  if [ -z "$cur" ]; then
    emit note LSP "Detached HEAD, not updated"
    return 0
  fi
  upstream=$(git -C "$ROOT" rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null)
  remote=${upstream%%/*}
  [ -n "$remote" ] || remote=origin
  def=$(git -C "$ROOT" ls-remote --symref "$remote" HEAD 2>/dev/null | awk '$1 == "ref:" { sub("refs/heads/", "", $2); print $2; exit }')
  if [ -z "$def" ]; then
    emit note LSP "Cannot reach $remote, not updated"
    return 0
  fi
  if [ "$cur" != "$def" ]; then
    emit note LSP "On branch $cur, not updated"
    return 0
  fi
  if is_dirty "$ROOT"; then
    emit note LSP "Uncommitted changes, not updated"
    return 0
  fi
  if ! git -C "$ROOT" fetch --quiet "$remote"; then
    emit note LSP "Fetch from $remote failed, not updated"
    return 0
  fi
  before=$(head_of "$ROOT")
  if [ "$before" = "$(git -C "$ROOT" rev-parse "$remote/$def")" ]; then
    emit ok LSP "Up to date on $def"
  elif git -C "$ROOT" merge --ff-only --quiet "$remote/$def" 2>/dev/null; then
    emit ok LSP "Fast-forwarded $def to $(short "$(head_of "$ROOT")"), restarting"
    exec bash "$SCRIPT_PATH" ${ORIG_ARGS[@]+"${ORIG_ARGS[@]}"} --no-self-update
  else
    emit note LSP "$def diverged from $remote/$def, not merged"
  fi
}

# ── Main ─────────────────────────────────────────────────────────────────────

write_summary() {
  [ -n "$SUMMARY" ] || return 0
  {
    printf '### Workspace\n\n'
    printf '%s\n' '```text'
    printf '%s\n' ${OUT[@]+"${OUT[@]}"}
    printf '%s\n\n' '```'
  } >>"$SUMMARY"
}

main() {
  parse_args "$@"
  read_manifest

  if [ "$COMMAND" = sync ] && [ $NO_SELF_UPDATE -eq 0 ]; then
    self_update
  fi

  local i name dir url ref
  for i in ${R_NAME[@]+"${!R_NAME[@]}"}; do
    is_selected "${R_GROUP[$i]}" || continue
    name=${R_NAME[$i]}
    dir="$ROOT/${R_PATH[$i]}"
    url=${R_URL[$i]}
    ref=${R_REF[$i]}
    case $COMMAND in
      sync)
        if [ "$ref" = default ]; then sync_owned "$name" "$dir" "$url"; else sync_pinned "$name" "$dir" "$url" "$ref"; fi
        ;;
      check)
        if [ "$ref" = default ]; then check_owned "$name" "$dir" "$url"; else check_pinned "$name" "$dir" "$url" "$ref"; fi
        ;;
      record)
        [ "$ref" = default ] || record_pinned "$name" "$dir" "$url" "$ref"
        ;;
    esac
  done

  if [ "$COMMAND" = record ]; then
    if [ $FAILED -eq 0 ]; then
      write_records
    else
      printf '%s\n' "$(basename "$MANIFEST") not changed" >&2
    fi
  fi

  write_summary
  return $FAILED
}

# Everything above only defines functions, and this line ends the file: a self-update that rewrites
# the file while it runs cannot make bash read new lines after main returns.
main "$@"; exit $?
