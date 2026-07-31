#!/usr/bin/env bash
# Launches Claude Code in a Docker container with this project mounted at /workspace.
# First run builds the image and prompts for Claude login (auth persists in the
# claude-home volume). Usage:
#   ./claude-docker.sh                     # interactive, permission prompts on
#   ./claude-docker.sh --yolo              # --dangerously-skip-permissions
#   ./claude-docker.sh --yolo "fix all compiler warnings"
#   ./claude-docker.sh --rebuild           # force image rebuild
#   ./claude-docker.sh --shell             # drop into bash instead of claude
# Runnable from any cwd; paths are derived from this script's own location.
set -euo pipefail

image=claude-code:latest
tools="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
proj="$(dirname "$tools")"

yolo=0; rebuild=0; shell=0
claude_args=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --yolo)    yolo=1 ;;
        --rebuild) rebuild=1 ;;
        --shell)   shell=1 ;;
        --)        shift; claude_args+=("$@"); break ;;
        *)         claude_args+=("$1") ;;
    esac
    shift
done
(( yolo )) && claude_args=(--dangerously-skip-permissions "${claude_args[@]+"${claude_args[@]}"}")

# Build context is Tools/, not the repo root: the Dockerfile has no COPY, so
# there is no reason to ship the whole Unity project over the 9p mount.
if (( rebuild )) || ! docker image inspect "$image" >/dev/null 2>&1; then
    docker build -f "$tools/Dockerfile.claude" -t "$image" "$tools"
fi

# host.docker.internal is not defined on the Linux docker engine; host-gateway
# maps it to the WSL host so the Unity MCP server on localhost:25723 is reachable.
# Allocate a TTY only when there is one; forcing -it breaks headless runs
# such as `claude-docker.sh -p "..."` from a script or CI.
tty_flags=(-i)
[[ -t 0 && -t 1 ]] && tty_flags=(-it)

run=(
    docker run "${tty_flags[@]}" --rm
    --add-host=host.docker.internal:host-gateway
    -v "$proj:/workspace"
    # .mcp.docker.json shadows the project's .mcp.json inside the container:
    # same MCPs, but with host URLs rewritten to host.docker.internal.
    -v "$tools/.mcp.docker.json:/workspace/.mcp.json:ro"
    -v claude-home:/home/dev/.claude
    -v claude-gh:/home/dev/.config/gh
    -e CLAUDE_CODE_DISABLE_AUTOUPDATE=1
)
[[ -n "${ANTHROPIC_API_KEY:-}" ]] && run+=(-e ANTHROPIC_API_KEY)

if (( shell )); then
    # Trailing args go to bash, so `--shell -c '...'` works for one-off commands.
    exec "${run[@]}" "$image" bash ${claude_args[@]+"${claude_args[@]}"}
else
    exec "${run[@]}" "$image" claude ${claude_args[@]+"${claude_args[@]}"}
fi
