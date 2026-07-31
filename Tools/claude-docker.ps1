# Launches Claude Code in a Docker container with the repo root (this script's
# parent directory) mounted at /workspace.
# First run builds the image and prompts for Claude login (auth persists in the
# claude-home volume). Usage:
#   .\claude-docker.ps1                  # interactive, permission prompts on
#   .\claude-docker.ps1 -Yolo            # --dangerously-skip-permissions
#   .\claude-docker.ps1 -Yolo -Prompt "fix all compiler warnings"
param(
    [switch]$Yolo,
    [string]$Prompt,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$image = 'claude-code:latest'
$tools = $PSScriptRoot
$proj  = Split-Path $PSScriptRoot -Parent

# Build context is Tools/, not the repo root: the Dockerfile has no COPY, so
# there is no reason to ship the whole Unity project as build context.
$haveImage = docker image inspect $image 2>$null
if ($Rebuild -or -not $haveImage) {
    docker build -f "$tools\Dockerfile.claude" -t $image $tools
    if ($LASTEXITCODE -ne 0) { throw "docker build failed" }
}

$claudeArgs = @()
if ($Yolo)   { $claudeArgs += '--dangerously-skip-permissions' }
if ($Prompt) { $claudeArgs += $Prompt }

# .mcp.docker.json shadows the project's .mcp.json inside the container:
# same MCPs, but with host URLs rewritten to host.docker.internal.
# Allocate a TTY only when there is one; forcing -it breaks headless runs
# such as `claude-docker.ps1 -Prompt "..."` from a script or CI.
$ttyFlags = if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { '-i' } else { '-it' }

docker run $ttyFlags --rm `
    --add-host=host.docker.internal:host-gateway `
    -v "${proj}:/workspace" `
    -v "${tools}\.mcp.docker.json:/workspace/.mcp.json:ro" `
    -v claude-home:/home/dev/.claude `
    -v claude-gh:/home/dev/.config/gh `
    -e CLAUDE_CODE_DISABLE_AUTOUPDATE=1 `
    $image claude @claudeArgs
