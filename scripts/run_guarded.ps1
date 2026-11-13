param([Parameter(Mandatory)][string]$File, [Parameter(Mandatory)][string]$Entry)
# Runs an AgentScripts entry point against MainMRScene atomically. The Unity Editor is shared with
# another tool that switches scenes, so "open the scene" and "run the builder" must be one call on
# the Editor's main thread: this merges SceneGuard.cs and the builder file into one script whose
# entry opens MainMRScene (only if the open scene has no unsaved changes) and then calls the builder.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$guard = Get-Content (Join-Path $root 'AgentScripts\SceneGuard.cs') -Raw
$body = Get-Content (Join-Path $root $File) -Raw
$usings = New-Object System.Collections.Generic.List[string]
$strip = {
    param($text)
    $out = New-Object System.Text.StringBuilder
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^\s*using\s+[A-Za-z_][\w.]*\s*(=\s*[\w.<>,\s]+)?;\s*$' -and $line -notmatch '\(') { if (-not $usings.Contains($line.Trim())) { $usings.Add($line.Trim()) } }
        else { [void]$out.AppendLine($line) }
    }
    $out.ToString()
}
$g = & $strip $guard; $b = & $strip $body
$cls, $method = $Entry -split '\.', 2
$wrapper = @"
public static class GuardedRun
{
    public static string Go()
    {
        string g = SceneGuard.Ensure();
        if (!g.StartsWith("OK")) return "GUARD " + g + " : builder not run";
        object r = $cls.$method();
        return "GUARD " + g + "\n" + (r == null ? "" : r.ToString());
    }
}
"@
$tmp = Join-Path $root 'AgentScripts\_GuardedRun.cs'
(($usings -join "`n") + "`n" + $g + "`n" + $b + "`n" + $wrapper) | Set-Content -Encoding utf8 $tmp
unity command --project-path $root script run --file AgentScripts/_GuardedRun.cs --entry GuardedRun.Go
Remove-Item $tmp -ErrorAction SilentlyContinue
