#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DevEcoRoot,
    [Parameter(Mandatory)][string]$OpenHarmonyNetRoot,
    [ValidateSet('x86_64', 'arm64-v8a')][string]$Abi = 'x86_64',
    [ValidateSet('debug', 'release')][string]$BuildMode = 'debug',
    [string]$NuGetRoot,
    [string]$HvigorHome,
    [string]$OhPmHome,
    [switch]$StageOnly,
    [switch]$UseEgl,
    [switch]$TraceImePreview,
    [switch]$Windowed
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$DevEcoRoot = (Resolve-Path -LiteralPath $DevEcoRoot).ProviderPath
$OpenHarmonyNetRoot = (Resolve-Path -LiteralPath $OpenHarmonyNetRoot).ProviderPath
$sdk = Join-Path $DevEcoRoot 'sdk/default'
$native = Join-Path $sdk 'openharmony/native'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $native 'oh-uni-package.json') | ConvertFrom-Json
if ($manifest.apiVersion -ne '26' -or $manifest.platformVersion -ne '26.0.0' -or $manifest.releaseType -ne 'Release') {
    throw "Use the API 26.0.0 Release native SDK. Found $($manifest.version) $($manifest.releaseType)."
}

$runtimeCommit = '9114affc54e9d7d94246708b841ec7f037119ea6'
$actualCommit = & git -C $OpenHarmonyNetRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $runtimeCommit) {
    throw "Check out ohos_net-opensource at $runtimeCommit."
}

$rid, $machine = if ($Abi -eq 'x86_64') { 'linux-musl-x64', 'X86-64' } else { 'linux-musl-arm64', 'AArch64' }
if ($UseEgl -and $Windowed) { throw 'The windowed Counter currently uses software rendering.' }
$renderingSuffix = if ($UseEgl) { '/egl' } elseif ($Windowed) { '/windowed' } else { '' }
$diagnosticsSuffix = if ($TraceImePreview) { '/ime-preview' } else { '' }
$useEglValue = $UseEgl.IsPresent.ToString().ToLowerInvariant()
$traceImeValue = $TraceImePreview.IsPresent.ToString().ToLowerInvariant()
$useWindowedValue = $Windowed.IsPresent.ToString().ToLowerInvariant()
$build = Join-Path $repository "artifacts/build/$Abi$renderingSuffix$diagnosticsSuffix"
$managed = Join-Path $build 'managed'
$hap = Join-Path $build 'Hap'
if (!$NuGetRoot) { $NuGetRoot = Join-Path $repository 'artifacts/cache/nuget' }
if (!$HvigorHome) { $HvigorHome = Join-Path $repository 'artifacts/cache/hvigor' }
if (!$OhPmHome) { $OhPmHome = Join-Path $repository 'artifacts/cache/ohpm' }
New-Item -ItemType Directory -Force -Path $build,$hap,$NuGetRoot,$HvigorHome,$OhPmHome | Out-Null
$NuGetRoot = (Resolve-Path -LiteralPath $NuGetRoot).ProviderPath
$sample = Join-Path $repository 'samples/Counter'

Push-Location $repository
try {
    & dotnet publish (Join-Path $sample 'Counter.csproj') -c Release -r $rid -o $managed `
        '-p:OpenHarmonyBuild=true' '-p:OpenHarmonyApiLevel=26' "-p:OpenHarmonyAbi=$Abi" "-p:AvaloniaUseEgl=$useEglValue" `
        "-p:AvaloniaTraceImePreview=$traceImeValue" `
        "-p:AvaloniaUseOwnedWindows=$useWindowedValue" `
        "-p:OpenHarmonyNetRoot=$OpenHarmonyNetRoot" "-p:OpenHarmonySdkRoot=$sdk" "-p:OpenHarmonyNativeRoot=$native" `
        "-p:ArtifactsPath=$build/dotnet" "-p:RestorePackagesPath=$NuGetRoot" -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'The Counter NativeAOT publish failed.' }
} finally {
    Pop-Location
}

$executableSuffix = if ($IsWindows) { '.exe' } else { '' }
$readElf = Join-Path $native "llvm/bin/llvm-readelf$executableSuffix"
$appLibrary = Join-Path $managed 'libavalonia_ohos_app.so'
$symbolOutput = & $readElf --file-header --dyn-syms --wide $appLibrary
$symbols = $symbolOutput -join "`n"
if ($LASTEXITCODE -ne 0 -or $symbols -notmatch "Machine:.*$machine") {
    throw "The managed application must be $Abi ELF."
}
$counterExports = @(
    'avaoh_resize', 'avaoh_destroy', 'avaoh_touch',
    'avaoh_key', 'avaoh_pointer', 'avaoh_input_blur', 'avaoh_paste', 'avaoh_drag',
    'avaoh_set_lifecycle', 'avaoh_set_theme', 'avaoh_set_displays', 'avaoh_set_window_position',
    'avaoh_set_surface_offset', 'avaoh_request_back',
    'avaoh_set_avoid_area', 'avaoh_set_window_inset_properties', 'avaoh_clear_window_insets',
    'avaoh_accessibility_configure', 'avaoh_accessibility_invoke', 'avaoh_accessibility_action',
    'avaoh_update_child_surface', 'avaoh_close_child_surface', 'avaoh_report_child_frame',
    'avaoh_report_child_event', 'avaoh_request_child_close'
)
foreach ($export in $counterExports) {
    if ($symbols -notmatch "\b$export\b") { throw "The NativeAOT application is missing $export." }
}

Copy-Item -Path (Join-Path $sample 'Hap/*') -Destination $hap -Recurse -Force
$entry = Join-Path $hap 'entry/src/main'
$arktsSource = Join-Path $repository 'src/Avalonia.OpenHarmony.ArkTS'
Copy-Item -Path (Join-Path $arktsSource '*') -Destination $entry -Recurse -Force
$cpp = Join-Path $entry 'cpp'
$nativeSource = Join-Path $repository 'src/Avalonia.OpenHarmony.Native'
Copy-Item -Path (Join-Path $nativeSource '*') -Destination $cpp -Force
$moduleProfilePath = Join-Path $hap 'entry/build-profile.json5'
$moduleProfile = Get-Content -LiteralPath $moduleProfilePath -Raw | ConvertFrom-Json
$moduleProfile.buildOption.externalNativeOptions.abiFilters = @($Abi)
$moduleProfile | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $moduleProfilePath -Encoding utf8

$libs = Join-Path $hap "entry/libs/$Abi"
New-Item -ItemType Directory -Force -Path $libs | Out-Null
$graphicsScript = Join-Path $PSScriptRoot 'PrepareOpenHarmonyGraphics.ps1'
& $graphicsScript -Destination $libs -NuGetRoot $NuGetRoot -OpenHarmonyNetRoot $OpenHarmonyNetRoot -NativeSdkRoot $native -Abi $Abi
Copy-Item -LiteralPath $appLibrary -Destination $libs -Force
$notices = Join-Path $hap 'entry/src/main/resources/rawfile/graphics-licenses'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
Copy-Item -Path (Join-Path $libs 'licenses/*') -Destination $notices -Force

$packageNotices = Join-Path $hap 'entry/src/main/resources/rawfile/licenses'
New-Item -ItemType Directory -Force -Path $packageNotices | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'counter-licenses/*.txt') -Destination $packageNotices -Force
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $packageNotices 'Avalonia.OpenHarmony-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repository 'THIRD-PARTY.md') -Destination (Join-Path $packageNotices 'THIRD-PARTY.txt') -Force
Copy-Item -LiteralPath (Join-Path $OpenHarmonyNetRoot 'LICENSE') -Destination (Join-Path $packageNotices 'OpenHarmonyDotNet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $native 'llvm/NOTICE') -Destination (Join-Path $packageNotices 'HarmonyOS-LLVM-NOTICES.txt') -Force

$depsPath = Join-Path $build "dotnet/bin/Counter/release_$rid/libavalonia_ohos_app.deps.json"
$deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json -AsHashtable
$runtimeName = "Microsoft.NETCore.App.Runtime.$rid"
$runtimePack = @($deps.libraries.Keys | Where-Object { $_ -like "runtimepack.$runtimeName/*" })
if ($runtimePack.Count -ne 1 -or $runtimePack[0] -notmatch '/(\d+\.\d+\.\d+(?:-[\w.-]+)?)$') {
    throw "Could not identify the restored .NET runtime pack from $depsPath."
}
$runtimeVersion = $Matches[1]
$runtimeDirectory = Join-Path $NuGetRoot "$($runtimeName.ToLowerInvariant())/$runtimeVersion"
# Keep the fork's 9.0.2 notices and include the Microsoft pack's originals if the contents differ.
$runtimeNotices = [ordered]@{ 'LICENSE.TXT' = 'LICENSE'; 'THIRD-PARTY-NOTICES.TXT' = 'NOTICES' }
foreach ($file in $runtimeNotices.Keys) {
    $source = Join-Path $runtimeDirectory $file
    $kind = $runtimeNotices[$file]
    $baseline = Join-Path $packageNotices "DotNet-9.0.2-$kind.txt"
    $additional = Join-Path $packageNotices "DotNet-MicrosoftPack-$kind.txt"
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $baseline).Hash) {
        Copy-Item -LiteralPath $source -Destination $additional -Force
    } elseif (Test-Path -LiteralPath $additional -PathType Leaf) {
        Remove-Item -LiteralPath $additional -Force
    }
}
Write-Host "Staged DevEco project at $hap"
if ($StageOnly) { return }

$commandSuffix = if ($IsWindows) { '.bat' } else { '' }
$ohpm = Join-Path $DevEcoRoot "tools/ohpm/bin/ohpm$commandSuffix"
$hvigor = Join-Path $DevEcoRoot "tools/hvigor/bin/hvigorw$commandSuffix"
$javaHome = Join-Path $DevEcoRoot 'jbr'
$buildEnvironment = @{
    DEVECO_SDK_HOME = Join-Path $DevEcoRoot 'sdk'
    JAVA_HOME = $javaHome
    HVIGOR_USER_HOME = $HvigorHome
    OHPM_HOME = $OhPmHome
    Path = ((Join-Path $DevEcoRoot 'tools/node'),(Join-Path $DevEcoRoot 'tools/ohpm/bin'),
        (Join-Path $javaHome 'bin'),$env:Path) -join [IO.Path]::PathSeparator
}
$previousEnvironment = @{}
Push-Location $hap
try {
    foreach ($name in $buildEnvironment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name], 'Process')
    }
    & $ohpm install --all
    if ($LASTEXITCODE -ne 0) { throw "ohpm install failed ($LASTEXITCODE)." }
    & $hvigor assembleHap --mode module -p product=default -p 'module=entry@default' -p "buildMode=$BuildMode" --no-daemon
    if ($LASTEXITCODE -ne 0) { throw "Hvigor assembleHap failed ($LASTEXITCODE)." }
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    Pop-Location
}

$builtHap = Join-Path $hap 'entry/build/default/outputs/default/entry-default-unsigned.hap'
$archive = [IO.Compression.ZipFile]::OpenRead($builtHap)
try {
    $required = @('module.json', 'resources.index', 'ets/modules.abc', "libs/$Abi/libavalonia_ohos_bridge.so")
    $required += Get-ChildItem -LiteralPath $libs -File | Where-Object Name -Match '\.so(\.\d+)*$' |
        ForEach-Object { "libs/$Abi/$($_.Name)" }
    foreach ($entry in $required) {
        if ($archive.GetEntry($entry).Length -le 0) { throw "HAP is missing $entry." }
    }
    foreach ($directory in @($notices, $packageNotices)) {
        $folder = Split-Path -Leaf $directory
        foreach ($file in Get-ChildItem -LiteralPath $directory -File) {
            $path = "resources/rawfile/$folder/$($file.Name)"
            $entry = $archive.GetEntry($path)
            if (!$entry -or $entry.Length -le 0) { throw "HAP is missing $path." }
            $stream = $entry.Open()
            try {
                if ((Get-FileHash -InputStream $stream).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                    throw "HAP contains altered license text at $path."
                }
            } finally {
                $stream.Dispose()
            }
        }
    }
    $profileReader = [IO.StreamReader]::new($archive.GetEntry('module.json').Open())
    try { $profile = $profileReader.ReadToEnd() | ConvertFrom-Json }
    finally { $profileReader.Dispose() }
    if ($profile.app.compileSdkType -ne 'HarmonyOS' -or $profile.app.apiReleaseType -ne 'Release' -or
        $profile.app.minAPIVersion -ne 260000026 -or $profile.app.targetAPIVersion -ne 260000026) {
        throw 'The packaged HarmonyOS profile does not target API 26.'
    }
} finally {
    $archive.Dispose()
}

$packageDirectory = Join-Path $repository "artifacts/packages/$Abi/$BuildMode$renderingSuffix$diagnosticsSuffix"
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
$package = Join-Path $packageDirectory 'AvaloniaCounter-unsigned.hap'
Copy-Item -LiteralPath $builtHap -Destination $package -Force
Get-FileHash -LiteralPath $package -Algorithm SHA256
