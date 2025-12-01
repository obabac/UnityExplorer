$deadline = (Get-Date).AddMinutes(60)
pwsh ./codex-exec.ps1 -Prompt 'Implement the plan end-to-end'  # first turn

while ((Get-Date) -lt $deadline) {
  codex exec --profile autopilot --full-auto --sandbox workspace-write `
    --json --output-last-message "$HOME/.codex/exec-logs/exec-resume-$((Get-Date).ToString('yyyyMMdd-HHmmss')).final.md" `
    resume --last "Continue the plan. Only stop if blocked or done." | `
    Tee-Object -FilePath "$HOME/.codex/exec-logs/exec-resume-$((Get-Date).ToString('yyyyMMdd-HHmmss')).jsonl"
}