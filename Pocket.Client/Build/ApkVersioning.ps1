<#
.SYNOPSIS
	Versioning and distribution for signed Pocket APKs. State lives in Pocket.Client/Version.props.

.DESCRIPTION
	-Mode Resolve     Prints "<version>|<build>|<fingerprint>" for the APK about to be built (writes nothing).
	                  Patch is bumped when client/shared sources differ from the stored fingerprint.
	-Mode Export      Copies the signed APK to Desktop\Pocket and to /sdcard/Download/Pocket on every
	                  connected physical Android device, then persists version/build/fingerprint.
	-Mode MinorBump   Called by the post-merge hook on main: Minor+1, Patch=0, fingerprint reset.
	-Mode Fingerprint Prints the current source fingerprint.
#>
param(
	[Parameter(Mandatory)][ValidateSet('Resolve', 'Export', 'MinorBump', 'Fingerprint')][string]$Mode,
	[string]$ApkPath,
	[string]$Version,
	[int]$Build,
	[string]$SourceFingerprint
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$propsPath = Join-Path $repoRoot 'Pocket.Client\Version.props'
$versionedDirs = @('Pocket.Client', 'Pocket.Shared')

function Read-State {
	$xml = [xml](Get-Content -Raw -LiteralPath $propsPath)
	$pg = $xml.Project.PropertyGroup
	[pscustomobject]@{
		Version     = [version]$pg.PocketVersion
		Build       = [int]$pg.PocketBuildNumber
		Fingerprint = [string]$pg.PocketSourceFingerprint
	}
}

function Write-State([string]$version, [int]$build, [string]$fingerprint) {
	$text = Get-Content -Raw -LiteralPath $propsPath
	$text = $text -replace '<PocketVersion>[^<]*</PocketVersion>', "<PocketVersion>$version</PocketVersion>"
	$text = $text -replace '<PocketBuildNumber>[^<]*</PocketBuildNumber>', "<PocketBuildNumber>$build</PocketBuildNumber>"
	$text = $text -replace '<PocketSourceFingerprint>[^<]*</PocketSourceFingerprint>', "<PocketSourceFingerprint>$fingerprint</PocketSourceFingerprint>"
	[IO.File]::WriteAllText($propsPath, $text, [Text.UTF8Encoding]::new($false))
}

# Content hash of the working tree (tracked + untracked, honoring .gitignore) of the versioned
# directories, excluding Version.props itself. Independent of commits: committing changes
# that were already built does not count as a new change.
function Get-SourceFingerprint {
	$tmpIndex = Join-Path ([IO.Path]::GetTempPath()) ("pocket-fp-" + [guid]::NewGuid() + ".idx")
	$prevIndex = $env:GIT_INDEX_FILE
	# git prints CRLF warnings etc. on stderr; in Windows PowerShell that must not become a terminating error.
	$ErrorActionPreference = 'Continue'
	try {
		$env:GIT_INDEX_FILE = $tmpIndex
		Push-Location $repoRoot
		git read-tree HEAD 2>&1 | Out-Null
		git add -A -- @versionedDirs 2>&1 | Out-Null
		git rm --cached -q --ignore-unmatch -- 'Pocket.Client/Version.props' 2>&1 | Out-Null
		$tree = (git write-tree).Trim()
		$parts = foreach ($d in $versionedDirs) { git rev-parse --verify -q "${tree}:$d" }
		$joined = ($parts -join ':')
		$sha = [Security.Cryptography.SHA1]::Create()
		-join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($joined)) | ForEach-Object { $_.ToString('x2') })
	}
	finally {
		Pop-Location
		$env:GIT_INDEX_FILE = $prevIndex
		Remove-Item -LiteralPath $tmpIndex -ErrorAction SilentlyContinue
	}
}

function Get-PhysicalDevices {
	$adb = Get-Command adb -ErrorAction SilentlyContinue
	if (-not $adb) { return @() }
	$lines = & adb devices 2>$null | Select-Object -Skip 1
	foreach ($line in $lines) {
		if ($line -match '^(\S+)\s+device$') {
			$serial = $Matches[1]
			if ($serial -like 'emulator-*') { continue }
			$qemu = (& adb -s $serial shell getprop ro.kernel.qemu 2>$null | Out-String).Trim()
			if ($qemu -eq '1') { continue }
			$serial
		}
	}
}

switch ($Mode) {
	'Fingerprint' { Get-SourceFingerprint }

	'Resolve' {
		$s = Read-State
		$fp = Get-SourceFingerprint
		$v = $s.Version
		if ($s.Fingerprint -and $s.Fingerprint -ne $fp) {
			$v = [version]::new($v.Major, $v.Minor, $v.Build + 1)
		}
		"{0}|{1}|{2}" -f $v.ToString(3), ($s.Build + 1), $fp
	}

	'Export' {
		if (-not (Test-Path -LiteralPath $ApkPath)) { throw "Signed APK not found: $ApkPath" }
		$name = 'Pocket v{0}-b{1} ({2}).apk' -f $Version, $Build, (Get-Date -Format 'MM-dd')

		$desktopDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pocket'
		New-Item -ItemType Directory -Force -Path $desktopDir | Out-Null
		$desktopApk = Join-Path $desktopDir $name
		Copy-Item -LiteralPath $ApkPath -Destination $desktopApk -Force
		Write-Host "Pocket APK -> $desktopApk"

		foreach ($serial in Get-PhysicalDevices) {
			& adb -s $serial shell mkdir -p /sdcard/Download/Pocket | Out-Null
			& adb -s $serial push $desktopApk "/sdcard/Download/Pocket/$name" | Out-Null
			if ($LASTEXITCODE -eq 0) { Write-Host "Pocket APK -> ${serial}:/sdcard/Download/Pocket/$name" }
			else { Write-Warning "Could not copy APK to device $serial" }
		}

		Write-State $Version $Build $SourceFingerprint
	}

	'MinorBump' {
		$s = Read-State
		$v = [version]::new($s.Version.Major, $s.Version.Minor + 1, 0)
		Write-State $v.ToString(3) $s.Build (Get-SourceFingerprint)
		$v.ToString(3)
	}
}
