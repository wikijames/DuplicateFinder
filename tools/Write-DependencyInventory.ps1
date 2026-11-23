param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$AssetsFile
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publish = [IO.Path]::GetFullPath($PublishDirectory)
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$deps = Get-Content -LiteralPath (Join-Path $publish 'DuplicateFinder.Desktop.deps.json') -Raw | ConvertFrom-Json
$cacheRoots = @($assets.packageFolders.PSObject.Properties.Name)
$noticeRoot = Join-Path $publish 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $noticeRoot | Out-Null
$inventory = New-Object System.Collections.Generic.List[object]
$runtimeKeys = @($deps.libraries.PSObject.Properties.Name)
$direct = @($assets.project.frameworks.PSObject.Properties.Value.dependencies.PSObject.Properties.Name)

foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | Sort-Object Name) {
    $source = $null
    foreach ($cacheRoot in $cacheRoots) {
        $candidate = Join-Path $cacheRoot $library.Value.path
        if (Test-Path -LiteralPath $candidate) { $source = $candidate; break }
    }
    if ($null -eq $source) { throw 'A restored dependency is missing from the package cache.' }
    $nuspec = Get-ChildItem -LiteralPath $source -File -Filter '*.nuspec' | Select-Object -First 1
    if ($null -eq $nuspec) { throw 'Dependency metadata is missing.' }
    [xml]$metadataXml = Get-Content -LiteralPath $nuspec.FullName
    $metadata = $metadataXml.SelectSingleNode("//*[local-name()='metadata']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $copyright = $metadata.SelectSingleNode("*[local-name()='copyright']")
    $identity = $library.Name.Split('/')
    $shipped = $runtimeKeys -contains $library.Name
    $inventory.Add([pscustomobject]@{
        Id = $identity[0]; Version = $identity[1]; Direct = ($direct -contains $identity[0])
        InPublishedDependencyGraph = $shipped
        LicenseType = if ($license) { $license.type } else { 'undeclared' }
        License = if ($license) { $license.InnerText } else { $null }
        DeclaredCopyright = if ($copyright) { $copyright.InnerText } else { $null }
        NuGetContentHash = $library.Value.sha512
    })
    if (!$shipped) { continue }
    if ($null -eq $license) { throw 'A published dependency has no declared NuGet license. Review it before packaging.' }
    $destination = Join-Path $noticeRoot ($identity[0] + '-' + $identity[1])
    New-Item -ItemType Directory -Path $destination | Out-Null
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    # Preserve available vendor notice files verbatim, including nested native notices.
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object { $_.Name -match '(?i)^(licen[cs]e|copying|notice|third.party.notices)' }) {
        $relative = $file.FullName.Substring($source.TrimEnd('\', '/').Length + 1)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    if ($license.type -eq 'file' -and !(Test-Path -LiteralPath (Join-Path $destination $license.InnerText))) {
        $declaredFile = [IO.Path]::GetFullPath((Join-Path $source $license.InnerText))
        if (!$declaredFile.StartsWith($source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A dependency license file points outside its package.'
        }
        Copy-Item -LiteralPath $declaredFile -Destination (Join-Path $destination 'DECLARED-LICENSE.txt')
    }
}

foreach ($runtime in $deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'runtimepack' }) {
    $identity = $runtime.Name.Substring('runtimepack.'.Length).Split('/')
    $runtimeSource = $null
    foreach ($cacheRoot in $cacheRoots) {
        $candidate = Join-Path $cacheRoot ($identity[0].ToLowerInvariant() + '/' + $identity[1])
        if (Test-Path -LiteralPath $candidate) { $runtimeSource = $candidate; break }
    }
    if ($null -eq $runtimeSource) { throw 'The bundled runtime notice source is missing.' }
    $destination = Join-Path $noticeRoot ($identity[0] + '-' + $identity[1])
    New-Item -ItemType Directory -Path $destination | Out-Null
    $files = @(Get-ChildItem -LiteralPath $runtimeSource -File | Where-Object { $_.Name -match '(?i)license|notice' })
    if ($files.Count -lt 2) { throw 'Bundled runtime license/third-party notices are missing.' }
    $files | Copy-Item -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/licenses') -Destination (Join-Path $noticeRoot 'Additional') -Recurse
$fontReview = & (Join-Path $PSScriptRoot 'Test-EmbeddedFontNotices.ps1') -AssemblyPath (Join-Path $publish 'Avalonia.Fonts.Inter.dll')
$noticeFiles = @(Get-ChildItem -LiteralPath $noticeRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{
        File = $_.FullName.Substring($publish.TrimEnd('\', '/').Length + 1).Replace('\', '/')
        Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
$report = [pscustomobject]@{
    Schema = 1; Scope = 'Release restore graph and published dependency graph; not a legal compliance certification'
    Packages = $inventory.ToArray()
    EmbeddedFontReview = $fontReview
    NoticeFiles = $noticeFiles
    OpenReviewItems = @('Application distribution/license choice', 'Native transitive attribution review', 'Debug-only diagnostics support license terms')
}
[IO.File]::WriteAllText((Join-Path $publish 'dependency-inventory.json'), ($report | ConvertTo-Json -Depth 8))
