# Runs a non-interactive Codex task and logs JSONL events + final Markdown.
# Priority for CODEX_HOME:
#   1) Nearest ancestor with a ".codex" directory (starting at $PWD)
#   2) $env:CODEX_HOME if it points to an existing directory
#   3) $HOME/.codex (created if -CreateMissing is supplied)

[CmdletBinding()]
param(
  [Parameter(Mandatory=$false)][string]$Prompt,
  [string]$Profile,  # optional; example: "unattended" / "autopilot"
  [ValidateSet("read-only","workspace-write","danger-full-access")]
  [string]$Sandbox = "danger-full-access",
  [switch]$CreateMissing  # create fallback $HOME/.codex if it doesn't exist
)

if(-not $Prompt){
	$Prompt = @"
You have ~60 minutes. Work autonomously:
- Plan -> implement -> run tests -> iterate until tests pass
- Only stop early if blocked by missing credentials or destructive ops
- Avoid asking questions; make reasonable assumptions and document them
- Produce a concise summary at the end; no intermediate status reports
- Keep "plans/unity-explorer-mcp-plan.md" and "plans/unity-explorer-mcp-todo.md" updated
- Read instructions from INSTRUCTIONS.MD. When finishing turn: Write new instructions for the next iteration into INSTRUCTIONS.MD (replace old instructions) then GIT commit changes
"@
}
#Write-Host "Prompt: $Prompt"

function Resolve-CodexHome {
  param(
    [Parameter(Mandatory)][string]$StartDir,
    [switch]$CreateIfMissing
  )

  # 1) Nearest ".codex" upward from StartDir
  try { $cur = (Resolve-Path -LiteralPath $StartDir -ErrorAction Stop).Path }
  catch { $cur = $StartDir }

  while ($true) {
    $candidate = Join-Path $cur ".codex"
    if (Test-Path -LiteralPath $candidate -PathType Container) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
    $parent = Split-Path -Path $cur -Parent
    if ([string]::IsNullOrEmpty($parent) -or $parent -eq $cur) { break }
    $cur = $parent
  }

  # 2) Use existing $env:CODEX_HOME if it exists
  if ($env:CODEX_HOME -and (Test-Path -LiteralPath $env:CODEX_HOME -PathType Container)) {
    return (Resolve-Path -LiteralPath $env:CODEX_HOME).Path
  }

  # 3) Default to $HOME/.codex
  $default = Join-Path $HOME ".codex"
  if ($CreateIfMissing -and -not (Test-Path -LiteralPath $default)) {
    New-Item -ItemType Directory -Force -Path $default | Out-Null
  }
  return $default
}

# Resolve CODEX_HOME with your requested precedence.
$startDir   = (Get-Location).Path
$codexHome  = Resolve-CodexHome -StartDir $startDir -CreateIfMissing:$CreateMissing.IsPresent
$env:CODEX_HOME = $codexHome  # expose to codex and child processes

# Logs
$logDir  = Join-Path $codexHome "exec-logs"
$ts      = Get-Date -Format 'yyyyMMdd-HHmmss'
$logJson = Join-Path $logDir "exec-$ts.jsonl"
$finalMd = Join-Path $logDir "exec-$ts.final.md"

# Ensure logs dir exists (even if .codex already existed)
if (-not (Test-Path -LiteralPath $logDir)) {
  New-Item -ItemType Directory -Force -Path $logDir | Out-Null
}

# Build args (remove the invalid --search)
$globalArgs = @()
if ($Profile) { $globalArgs += @("--profile", $Profile) }

$subcmdArgs = @(
  "exec",
  "-m", "gpt-5.1-codex-max",
  "-c", "model_reasoning_effort=medium",
  "--yolo",
  "--sandbox", $Sandbox,
  "--json",
  "--output-last-message", $finalMd
)

Write-Host "Using CODEX_HOME = $codexHome"
Write-Host "Log dir         = $logDir"
Write-Host "Running: codex $($globalArgs -join ' ') $($subcmdArgs -join ' ')"

# Invoke codex and tee the JSONL stream to file
codex @globalArgs @subcmdArgs "$Prompt" | Tee-Object -FilePath $logJson

Write-Host "Final summary: $finalMd"
Write-Host "Event log:     $logJson"
