$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repository = "humAIn-development/ApplejuiceControlCenter-CrossPlatform"
$IssueNumber = 3
$IssueApiUrl = "https://api.github.com/repos/$Repository/issues/$IssueNumber"
$ExpectedIssueOwner = "humAIn-development"
$ExpectedEmail = "ajcc-feedback@martin-bruenig.de"
$Solution = ".\ApplejuiceControlCenter-CrossPlatform.sln"
$SupportedSchemas = @("AJCC_REMOTE_PATCH_V2", "AJCC_REMOTE_PATCH_V3")

function Fail([string]$Message) {
    throw $Message
}

function Get-ChangedFiles {
    $lines = @(git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { Fail "git status failed." }

    $result = @()
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }
        $path = $line.Substring(3).Trim()
        if ($path.Contains(" -> ")) {
            $path = ($path -split " -> ")[-1]
        }
        $path = $path.Trim('"')
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $result += $path.Replace("\", "/")
        }
    }
    return @($result | Sort-Object -Unique)
}

function Assert-ExactFileSet([string[]]$Actual, [string[]]$Expected, [string]$Context) {
    $unexpected = @($Actual | Where-Object { $Expected -notcontains $_ })
    $missing = @($Expected | Where-Object { $Actual -notcontains $_ })
    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        Fail "$Context file set mismatch. Unexpected=[$($unexpected -join ', ')] Missing=[$($missing -join ', ')]"
    }
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($Bytes)
        return ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

if (-not (Test-Path $Solution)) {
    Fail "Run this command from the AJCC-X repository root."
}

$origin = (git remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0) { Fail "Cannot read git origin." }

$validOrigins = @(
    "https://github.com/humAIn-development/ApplejuiceControlCenter-CrossPlatform.git",
    "git@github.com:humAIn-development/ApplejuiceControlCenter-CrossPlatform.git"
)
if ($validOrigins -notcontains $origin) {
    Fail "Unexpected git origin: $origin"
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}
catch {
}

$headers = @{
    "User-Agent" = "AJCC-X-RemotePatchBridge"
    "Accept" = "application/vnd.github+json"
}

Write-Host "Reading AJCC-X patch channel..." -ForegroundColor Cyan
$issue = Invoke-RestMethod -Uri $IssueApiUrl -Headers $headers -Method Get

if ([string]$issue.user.login -ne $ExpectedIssueOwner) {
    Fail "Patch channel owner mismatch."
}

$payload = ([string]$issue.body) | ConvertFrom-Json
if ($SupportedSchemas -notcontains [string]$payload.schema) {
    Fail "Unsupported patch schema: $($payload.schema)"
}

if (-not [bool]$payload.active) {
    Write-Host "AJCC-X REMOTE PATCH: no active patch." -ForegroundColor Green
    exit 0
}

$patchId = [string]$payload.patch_id
$targetBranch = [string]$payload.target_branch
$baseSha = ([string]$payload.base_sha).ToLowerInvariant()
$description = [string]$payload.description
$commitMessage = [string]$payload.commit_message
$expectedFiles = @($payload.expected_files | ForEach-Object { ([string]$_).Replace("\", "/") })
$testProjects = @($payload.test_projects | ForEach-Object { [string]$_ })
$patchShaExpected = ([string]$payload.patch_sha256).ToLowerInvariant()
$patchBase64 = if ([string]$payload.schema -eq "AJCC_REMOTE_PATCH_V3") {
    [string]$payload.patch_gzip_base64
}
else {
    [string]$payload.patch_base64
}

if ($patchId -notmatch '^[A-Za-z0-9._-]+$') { Fail "Invalid patch_id." }
if ([string]::IsNullOrWhiteSpace($targetBranch)) { Fail "Missing target_branch." }
if ($baseSha -notmatch '^[0-9a-f]{40}$') { Fail "Invalid base_sha." }
if ([string]::IsNullOrWhiteSpace($commitMessage)) { Fail "Missing commit_message." }
if ($expectedFiles.Count -eq 0) { Fail "expected_files is empty." }
if ($patchShaExpected -notmatch '^[0-9a-f]{64}$') { Fail "Invalid patch_sha256." }
if ([string]::IsNullOrWhiteSpace($patchBase64)) { Fail "Encoded patch payload is empty." }

foreach ($file in $expectedFiles) {
    if ([string]::IsNullOrWhiteSpace($file)) { Fail "Empty expected file path." }
    if ([System.IO.Path]::IsPathRooted($file)) { Fail "Rooted expected file path is not allowed: $file" }
    if ($file -match '(^|/)\.\.(/|$)') { Fail "Parent traversal is not allowed: $file" }
    if ($file -eq ".git" -or $file.StartsWith(".git/")) { Fail ".git paths are not allowed." }
}

$gitDirRaw = (git rev-parse --git-dir).Trim()
if ($LASTEXITCODE -ne 0) { Fail "Cannot resolve .git directory." }
$gitDir = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $gitDirRaw))
$statePath = Join-Path $gitDir "ajcc-remote-patch-state.json"

if (Test-Path $statePath) {
    try {
        $state = (Get-Content -Raw $statePath) | ConvertFrom-Json
        if ([string]$state.patch_id -eq $patchId) {
            Write-Host "AJCC-X REMOTE PATCH: patch '$patchId' was already applied as $($state.applied_sha)." -ForegroundColor Green
            exit 0
        }
    }
    catch {
    }
}

$dirtyBefore = @(Get-ChangedFiles)
if ($dirtyBefore.Count -ne 0) {
    Fail "Working tree is not clean. STOP. Changed=[$($dirtyBefore -join ', ')]"
}

$currentBranch = (git branch --show-current).Trim()
if ($currentBranch -ne $targetBranch) {
    Fail "Wrong branch. Current='$currentBranch' Expected='$targetBranch'"
}

$email = (git config user.email).Trim()
if ($email -ne $ExpectedEmail) {
    Fail "Wrong git user.email: '$email'. Expected '$ExpectedEmail'."
}

git fetch origin
if ($LASTEXITCODE -ne 0) { Fail "git fetch failed." }

$localHead = (git rev-parse HEAD).Trim().ToLowerInvariant()
$remoteHead = (git rev-parse "origin/$targetBranch").Trim().ToLowerInvariant()
if ($localHead -ne $baseSha -or $remoteHead -ne $baseSha) {
    Fail "Patch base mismatch. Local=$localHead Remote=$remoteHead Expected=$baseSha"
}

Write-Host "Patch: $patchId" -ForegroundColor Cyan
if (-not [string]::IsNullOrWhiteSpace($description)) {
    Write-Host $description -ForegroundColor DarkGray
}

try {
    $encodedPatchBytes = [System.Convert]::FromBase64String($patchBase64)
    if ([string]$payload.schema -eq "AJCC_REMOTE_PATCH_V3") {
        $compressedStream = [System.IO.MemoryStream]::new($encodedPatchBytes)
        $gzipStream = [System.IO.Compression.GZipStream]::new(
            $compressedStream,
            [System.IO.Compression.CompressionMode]::Decompress)
        $decodedStream = [System.IO.MemoryStream]::new()
        try {
            $gzipStream.CopyTo($decodedStream)
            $patchBytes = $decodedStream.ToArray()
        }
        finally {
            $decodedStream.Dispose()
            $gzipStream.Dispose()
            $compressedStream.Dispose()
        }
    }
    else {
        $patchBytes = $encodedPatchBytes
    }
}
catch {
    Fail "Encoded patch payload is invalid: $($_.Exception.Message)"
}

$patchShaActual = Get-Sha256Hex $patchBytes
if ($patchShaActual -ne $patchShaExpected) {
    Fail "Patch SHA-256 mismatch. Actual=$patchShaActual Expected=$patchShaExpected"
}

$tempPatch = Join-Path $gitDir ("ajcc-remote-" + $patchId + ".patch")
[System.IO.File]::WriteAllBytes($tempPatch, $patchBytes)

$patchApplied = $false
$commitCreated = $false

try {
    Write-Host "Validating unified diff..." -ForegroundColor Cyan
    git apply --check "$tempPatch"
    if ($LASTEXITCODE -ne 0) { Fail "git apply --check failed." }

    $numstat = @(git apply --numstat "$tempPatch")
    if ($LASTEXITCODE -ne 0) { Fail "git apply --numstat failed." }

    $patchFiles = @()
    foreach ($line in $numstat) {
        $parts = $line -split "`t"
        if ($parts.Count -ge 3) {
            $patchFiles += ([string]$parts[$parts.Count - 1]).Replace("\", "/")
        }
    }
    $patchFiles = @($patchFiles | Sort-Object -Unique)
    Assert-ExactFileSet $patchFiles $expectedFiles "Patch"

    Write-Host "Applying unified diff..." -ForegroundColor Cyan
    git apply --whitespace=nowarn "$tempPatch"
    if ($LASTEXITCODE -ne 0) { Fail "git apply failed." }
    $patchApplied = $true

    $changed = @(Get-ChangedFiles)
    Assert-ExactFileSet $changed $expectedFiles "Working tree"

    git diff --check
    if ($LASTEXITCODE -ne 0) { Fail "git diff --check failed." }

    if ([bool]$payload.run_restore) {
        Write-Host "Restore..." -ForegroundColor Cyan
        dotnet restore $Solution
        if ($LASTEXITCODE -ne 0) { Fail "dotnet restore failed." }
    }

    if ([bool]$payload.run_build) {
        Write-Host "Release build..." -ForegroundColor Cyan
        dotnet build $Solution --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { Fail "Release build failed." }
    }

    foreach ($testProject in $testProjects) {
        if ([string]::IsNullOrWhiteSpace($testProject)) { continue }
        Write-Host "Test: $testProject" -ForegroundColor Cyan
        dotnet test $testProject --configuration Release --no-build --verbosity normal
        if ($LASTEXITCODE -ne 0) { Fail "Tests failed: $testProject" }
    }

    git add -- $expectedFiles
    if ($LASTEXITCODE -ne 0) { Fail "git add failed." }

    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { Fail "Staged diff check failed." }

    $staged = @(git diff --cached --name-only | ForEach-Object { ([string]$_).Replace("\", "/") } | Sort-Object -Unique)
    Assert-ExactFileSet $staged $expectedFiles "Staged"

    Write-Host "Creating local commit..." -ForegroundColor Cyan
    git commit -m "$commitMessage"
    if ($LASTEXITCODE -ne 0) { Fail "git commit failed." }
    $commitCreated = $true

    $identity = (git log -1 --format="%ae|%ce").Trim()
    if ($identity -ne "$ExpectedEmail|$ExpectedEmail") {
        Fail "Commit metadata mismatch: $identity. Commit was NOT pushed."
    }

    $newHead = (git rev-parse HEAD).Trim().ToLowerInvariant()
    Write-Host "Commit metadata OK: $identity" -ForegroundColor Green
    Write-Host "New commit: $newHead" -ForegroundColor Green

    git push origin $targetBranch
    if ($LASTEXITCODE -ne 0) {
        Fail "Push failed. The verified commit exists locally and was NOT rolled back."
    }

    $remoteLine = [string](git ls-remote origin "refs/heads/$targetBranch")
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteLine)) {
        Fail "Could not verify remote branch after push."
    }
    $remoteAfter = ($remoteLine -split "\s+")[0].ToLowerInvariant()
    if ($remoteAfter -ne $newHead) {
        Fail "Remote verification mismatch. Remote=$remoteAfter Local=$newHead"
    }

    $state = [pscustomobject]@{
        schema = [string]$payload.schema
        patch_id = $patchId
        base_sha = $baseSha
        applied_sha = $newHead
        applied_at_utc = [DateTime]::UtcNow.ToString("o")
    }
    [System.IO.File]::WriteAllText(
        $statePath,
        ($state | ConvertTo-Json -Compress),
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Host "AJCC-X REMOTE PATCH SUCCESS: $patchId -> $newHead" -ForegroundColor Green
}
catch {
    if ($patchApplied -and -not $commitCreated) {
        Write-Host "Validation failed before commit. Restoring exact base state..." -ForegroundColor Yellow
        git reset --hard $baseSha | Out-Host
        foreach ($file in $expectedFiles) {
            git clean -fd -- "$file" | Out-Host
        }
    }
    throw
}
finally {
    if (Test-Path $tempPatch) {
        Remove-Item -Force $tempPatch
    }
}
