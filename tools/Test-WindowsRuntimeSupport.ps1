param([Parameter(Mandatory = $true)][string]$RuntimeVersion)
$ErrorActionPreference = 'Stop'
if ($RuntimeVersion -notmatch '^10\.0\.\d+$') { throw 'The runtime review supports stable .NET 10 patches only.' }
$source = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$metadata = Invoke-RestMethod -Uri $source -TimeoutSec 30
$release = @($metadata.releases | Where-Object { $_.runtime.version -eq $RuntimeVersion })
if ($metadata.'channel-version' -ne '10.0' -or $release.Count -ne 1 -or
    $metadata.'support-phase' -notin @('active', 'maintenance') -or
    [string]::IsNullOrWhiteSpace($metadata.'eol-date') -or
    [DateTime]::Parse($metadata.'eol-date').Date -lt [DateTime]::UtcNow.Date) {
    throw 'Official runtime support metadata is incomplete or the channel is unsupported.'
}
if ($metadata.'latest-runtime' -ne $RuntimeVersion) {
    throw 'The pinned runtime is not the latest .NET 10 servicing patch. Review and update the profile before packaging.'
}
[pscustomobject]@{
    Schema = 1; CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o'); Source = $source
    Channel = '10.0'; PinnedRuntime = $RuntimeVersion; LatestRuntime = $metadata.'latest-runtime'
    SupportPhase = $metadata.'support-phase'; EndOfLife = $metadata.'eol-date'
    ReleaseDate = $release[0].'release-date'; SecurityRelease = $release[0].security
    IsLatestServicingPatch = $true
    Scope = 'Official servicing/support metadata only; not a full native/runtime security certification'
}
