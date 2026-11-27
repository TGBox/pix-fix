param(
    [string]$CurrentTag = '',
    [string[]]$CommitMessages = @(),
    [string]$PropsFile = 'Directory.Build.props'
)

$ErrorActionPreference = 'Stop'

$hasExplicitInput = -not [string]::IsNullOrWhiteSpace($CurrentTag) -or $CommitMessages.Count -gt 0

if (-not $hasExplicitInput) {
    $headTags = @(& git tag --points-at HEAD "v*.*.*" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0) {
        throw "git tag --points-at HEAD failed with exit code $LASTEXITCODE."
    }

    if ($headTags.Count -gt 0) {
        Write-Output "ALREADY_TAGGED=$($headTags[0].Trim())"
        exit 0
    }

    $latestTag = (& git tag -l "v*.*.*" --sort=-v:refname | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0) {
        throw "git tag -l failed with exit code $LASTEXITCODE."
    }

    if ([string]::IsNullOrWhiteSpace($latestTag)) {
        $initialVersion = '1.0.0'
        if (Test-Path -LiteralPath $PropsFile) {
            $propsContent = Get-Content -LiteralPath $PropsFile -Raw
            if ($propsContent -match '<Version>(\d+\.\d+\.\d+)(-[^<]*)?</Version>') {
                $initialVersion = $Matches[1]
            }
        }

        Write-Output $initialVersion
        exit 0
    }

    $CurrentTag = $latestTag.Trim()
    $rawLog = (& git log "$CurrentTag..HEAD" --format="%s%n%b%n---COMMIT---") -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "git log failed with exit code $LASTEXITCODE."
    }

    $CommitMessages = @(
        $rawLog -split "`r?`n---COMMIT---`r?`n?" |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

if ($CurrentTag -notmatch '^v?(\d+)\.(\d+)\.(\d+)$') {
    throw "Invalid SemVer tag '$CurrentTag'. Expected format vX.Y.Z."
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]

$bump = 'patch'
foreach ($message in $CommitMessages) {
    $lines = @($message -split "`r?`n")
    $subject = if ($lines.Count -gt 0) { $lines[0].Trim() } else { '' }
    $hasBreakingBody = ($lines | Where-Object { $_ -match '^BREAKING[ -]CHANGE:' }).Count -gt 0

    if ($hasBreakingBody -or $subject -match '^[a-z]+(\([^)]+\))?!:') {
        $bump = 'major'
        break
    }

    if ($subject -match '^feat(\([^)]+\))?:') {
        $bump = 'minor'
    }
}

switch ($bump) {
    'major' {
        $major++
        $minor = 0
        $patch = 0
    }
    'minor' {
        $minor++
        $patch = 0
    }
    default {
        $patch++
    }
}

Write-Output "$major.$minor.$patch"
