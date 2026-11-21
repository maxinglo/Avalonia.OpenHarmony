#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$NuGetRoot,
    [Parameter(Mandatory)][string]$OpenHarmonyNetRoot,
    [Parameter(Mandatory)][string]$NativeSdkRoot,
    [ValidateSet('x86_64', 'arm64-v8a')][string]$Abi = 'x86_64'
)

$ErrorActionPreference = 'Stop'
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'graphics-packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$sdk = Get-Content -LiteralPath (Join-Path $NativeSdkRoot 'oh-uni-package.json') -Raw | ConvertFrom-Json
if ($sdk.apiVersion -ne '26' -or $sdk.releaseType -ne 'Release') {
    throw 'Use the OpenHarmony API 26 Release Native SDK.'
}

if ($Abi -eq 'x86_64') {
    $rid = 'linux-musl-x64'
    $machine = 'X86-64'
    $target = 'x86_64-linux-ohos'
    $loaderName = 'libc.musl-x86_64.so.1'
} else {
    $rid = 'linux-musl-arm64'
    $machine = 'AArch64'
    $target = 'aarch64-linux-ohos'
    $loaderName = 'libc.musl-aarch64.so.1'
}

$readElf = Join-Path $NativeSdkRoot 'llvm/bin/llvm-readelf'
if ($IsWindows) { $readElf += '.exe' }
$sdkLibc = Join-Path $NativeSdkRoot "sysroot/usr/lib/$target/libc.so"
$prebuilt = Join-Path $OpenHarmonyNetRoot "prebuilt/native/$Abi"
$skia = Join-Path $NuGetRoot "$($lock.skiasharp.package)/$($lock.skiasharp.version)"
$harfBuzz = Join-Path $NuGetRoot "$($lock.harfbuzzsharp.package)/$($lock.harfbuzzsharp.version)"

function Get-ElfDetails([string]$Path) {
    $lines = & $readElf --wide --file-header --section-headers --dynamic-table --dyn-syms $Path
    if ($LASTEXITCODE -ne 0) { throw "llvm-readelf failed: $Path" }
    if (!(($lines -match "Machine:.*$machine") -and ($lines -match 'Class:\s+ELF64') -and ($lines -match 'Type:\s+DYN'))) {
        throw "Expected a shared ELF64 library for $Abi`: $Path"
    }

    $needed = [Collections.Generic.List[string]]::new()
    $imports = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $exports = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $soname = $null
    $stringTableOffset = $null
    $stringTableLength = $null
    foreach ($line in $lines) {
        if ($line -match '\(NEEDED\)\s+Shared library: \[(.+)\]') {
            $needed.Add($Matches[1])
        } elseif ($line -match '\(SONAME\)\s+Library soname: \[(.+)\]') {
            $soname = $Matches[1]
        } elseif ($line -match '\.dynstr\s+STRTAB\s+[\da-f]+\s+([\da-f]+)\s+([\da-f]+)') {
            $stringTableOffset = [Convert]::ToInt32($Matches[1], 16)
            $stringTableLength = [Convert]::ToInt32($Matches[2], 16)
        } elseif ($line -match '^\s*\d+:\s+\S+\s+\d+\s+\S+\s+(GLOBAL|WEAK|UNIQUE)\s+(DEFAULT|PROTECTED)\s+(\S+)\s+(\S+)') {
            $symbol = $Matches[4]
            if ($Matches[3] -eq 'UND') {
                if ($Matches[1] -eq 'GLOBAL') { $null = $imports.Add($symbol) }
            } else {
                $null = $exports.Add($symbol.Replace('@@', '@'))
                if ($symbol.Contains('@@')) { $null = $exports.Add($symbol.Split('@')[0]) }
            }
        }
    }

    [pscustomobject]@{
        Needed = $needed
        Soname = $soname
        StringTableOffset = $stringTableOffset
        StringTableLength = $stringTableLength
        Imports = $imports
        Exports = $exports
    }
}

function Write-OhosLibrary([string]$Path, [string]$OutputPath, [object]$Elf) {
    if (!($Elf.Needed -ccontains $loaderName) -or $null -eq $Elf.StringTableOffset) {
        throw "The pinned Linux/musl dependency was not found: $Path"
    }
    $image = [IO.File]::ReadAllBytes($Path)
    $table = [Text.Encoding]::GetEncoding('iso-8859-1').GetString($image, $Elf.StringTableOffset, $Elf.StringTableLength)
    $oldName = "$loaderName`0"
    $index = $table.IndexOf($oldName, [StringComparison]::Ordinal)
    if ($index -lt 0 -or $table.IndexOf($oldName, $index + 1, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected one exact musl dependency string in .dynstr: $Path"
    }
    # Keep .dynstr offsets intact, including ELF symbol-version records on dependent libraries.
    $patch = [Text.Encoding]::ASCII.GetBytes('libc.so')
    $offset = $Elf.StringTableOffset + $index
    [Array]::Clear($image, $offset, $oldName.Length)
    [Array]::Copy($patch, 0, $image, $offset, $patch.Length)
    [IO.File]::WriteAllBytes($OutputPath, $image)
}

$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$destinationParent = [IO.Path]::GetDirectoryName($destinationPath)
if (!$destinationParent) { throw 'The graphics output must be a child directory.' }
[IO.Directory]::CreateDirectory($destinationParent) | Out-Null
$stage = Join-Path $destinationParent ('.graphics-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    $sdkExports = (Get-ElfDetails $sdkLibc).Exports
    $staged = [ordered]@{}
    $hashes = $lock.hashes[$Abi]
    foreach ($file in $hashes.Keys) {
        $soname = $file
        switch ($file) {
            'libSkiaSharp.so' {
                $source = Join-Path $skia "runtimes/$rid/native/$file"
                $soname = $lock.skiasharp.soname
            }
            'libHarfBuzzSharp.so' {
                $source = Join-Path $harfBuzz "runtimes/$rid/native/$file"
                $soname = $lock.harfbuzzsharp.soname
            }
            default { $source = Join-Path $prebuilt $file }
        }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ($sourceHash -cne $hashes[$file]) { throw "SHA-256 mismatch for $Abi $file." }

        $before = Get-ElfDetails $source
        $output = Join-Path $stage $file
        Write-OhosLibrary $source $output $before
        $after = Get-ElfDetails $output
        $expectedNeeded = $before.Needed | ForEach-Object { if ($_ -ceq $loaderName) { 'libc.so' } else { $_ } }
        if ($after.Soname -cne $soname -or (Compare-Object $expectedNeeded $after.Needed -CaseSensitive) -or
            !$after.Imports.SetEquals($before.Imports) -or !$after.Exports.SetEquals($before.Exports)) {
            throw "Unexpected ELF metadata change in $file."
        }
        $staged[$file] = [ordered]@{
            sourceSha256 = $sourceHash
            sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
            soname = $after.Soname
            needed = @($after.Needed)
            imports = $after.Imports
            exports = $after.Exports
        }
    }

    foreach ($file in $staged.Keys) {
        $linked = [Collections.Generic.HashSet[string]]::new($sdkExports, [StringComparer]::Ordinal)
        $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $pending = [Collections.Generic.Queue[string]]::new([string[]]$staged[$file].needed)
        $dependency = ''
        while ($pending.TryDequeue([ref]$dependency)) {
            if ($dependency -ceq 'libc.so' -or !$visited.Add($dependency)) { continue }
            if (!$staged.Contains($dependency)) { throw "Missing $dependency in the dependencies of $file." }
            $linked.UnionWith($staged[$dependency].exports)
            foreach ($transitive in $staged[$dependency].needed) { $pending.Enqueue($transitive) }
        }
        $unknownSymbols = @($staged[$file].imports | Where-Object { !$linked.Contains($_) })
        if ($unknownSymbols.Count) {
            throw "Incomplete SDK symbol closure for $file`: $($unknownSymbols -join ', ')."
        }
    }
    foreach ($file in $staged.Keys) {
        $staged[$file].Remove('imports')
        $staged[$file].Remove('exports')
    }

    $licenses = Join-Path $stage 'licenses'
    [IO.Directory]::CreateDirectory($licenses) | Out-Null
    foreach ($component in @(@('SkiaSharp', $skia), @('HarfBuzzSharp', $harfBuzz))) {
        foreach ($name in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
            Copy-Item -LiteralPath (Join-Path $component[1] $name) -Destination (Join-Path $licenses "$($component[0])-$name")
        }
    }
    $upstreamNotices = @('Brotli-1.1.0.txt', 'bzip2-1.0.8.txt', 'Expat-COPYING.txt', 'Fontconfig-2.15.0.txt',
        'FreeType-2.13.3.txt', 'libpng-1.6.44.txt', 'zlib-1.3.1.txt', 'THIRD-PARTY.txt')
    foreach ($notice in $upstreamNotices) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "graphics-licenses/$notice") -Destination $licenses
    }
    $licenseHashes = [ordered]@{}
    Get-ChildItem -LiteralPath $licenses -File | ForEach-Object {
        $licenseHashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }

    [ordered]@{
        abi = $Abi
        api = $sdk.apiVersion
        sdkVersion = $sdk.version
        skiaSharp = $lock.skiasharp.version
        harfBuzzSharp = $lock.harfbuzzsharp.version
        openHarmonyNetRevision = $lock.openharmonyNetRevision
        libraries = $staged
        licenses = $licenseHashes
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'graphics-manifest.json') -Encoding utf8

    if (!(Test-Path -LiteralPath $destinationPath)) {
        [IO.Directory]::Move($stage, $destinationPath)
    } else {
        foreach ($file in $staged.Keys) {
            Copy-Item -LiteralPath (Join-Path $stage $file) -Destination (Join-Path $destinationPath $file) -Force
        }
        [IO.Directory]::CreateDirectory((Join-Path $destinationPath 'licenses')) | Out-Null
        Get-ChildItem -LiteralPath $licenses -File | Copy-Item -Destination (Join-Path $destinationPath 'licenses') -Force
        Copy-Item -LiteralPath (Join-Path $stage 'graphics-manifest.json') -Destination $destinationPath -Force
    }
    Write-Host "Prepared $($staged.Count) graphics libraries for $Abi at $destinationPath."
} finally {
    if (Test-Path -LiteralPath $stage) {
        $resolved = (Resolve-Path -LiteralPath $stage).ProviderPath
        if ($resolved -ne $stage -or [IO.Path]::GetDirectoryName($resolved) -ne $destinationParent) {
            throw "Unexpected staging location: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
