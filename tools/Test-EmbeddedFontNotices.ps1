param([Parameter(Mandatory = $true)][string]$AssemblyPath)
# Static byte/metadata inspection only. The font assembly is never loaded or executed.
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot '../docs/licenses/Inter-font-provenance.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path (Split-Path $manifestPath) $manifest.LicenseFile) -Algorithm SHA256).Hash -ne $manifest.LicenseSHA256) {
    throw 'The embedded font license notice differs from its reviewed source hash.'
}
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
if ((Get-Item -LiteralPath $resolvedAssembly).Length -gt 16MB -or $manifest.Schema -ne 1 -or @($manifest.Fonts).Count -ne 6) {
    throw 'Embedded font provenance inputs require review.'
}
$assembly = [IO.File]::ReadAllBytes($resolvedAssembly)
if (-not ('EmbeddedFontNoticeInspector' -as [type])) {
    Add-Type @'
using System;
using System.Security.Cryptography;
using System.Text;
public static class EmbeddedFontNoticeInspector {
    public static int Find(byte[] assembly, byte[] prefix, int length, string expected) {
        if (prefix.Length != 16 || length < 16 || length > assembly.Length) return -1;
        using (var sha = SHA256.Create()) {
            for (var offset = 0; offset <= assembly.Length - length; offset++) {
                if (assembly[offset] != prefix[0]) continue;
                var matched = true;
                for (var index = 1; index < prefix.Length; index++)
                    if (assembly[offset + index] != prefix[index]) { matched = false; break; }
                if (matched && string.Equals(BitConverter.ToString(sha.ComputeHash(assembly, offset, length)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase)) return offset;
            }
        }
        return -1;
    }
    private static int U16(byte[] bytes, int index) { return bytes[index] * 256 + bytes[index + 1]; }
    private static int U32(byte[] bytes, int index) { return checked(bytes[index] * 16777216 + bytes[index + 1] * 65536 + bytes[index + 2] * 256 + bytes[index + 3]); }
    public static string Version(byte[] bytes, int start) {
        for (var table = 0; table < U16(bytes, start + 4); table++) {
            var entry = start + 12 + table * 16;
            if (Encoding.ASCII.GetString(bytes, entry, 4) != "name") continue;
            var names = start + U32(bytes, entry + 8);
            var strings = names + U16(bytes, names + 4);
            for (var record = 0; record < U16(bytes, names + 2); record++) {
                var index = names + 6 + record * 12;
                if (U16(bytes, index) == 3 && U16(bytes, index + 6) == 5)
                    return Encoding.BigEndianUnicode.GetString(bytes, strings + U16(bytes, index + 10), U16(bytes, index + 8));
            }
        }
        return null;
    }
}
'@
}
foreach ($font in $manifest.Fonts) {
    $offset = [EmbeddedFontNoticeInspector]::Find($assembly, [Convert]::FromBase64String($font.PrefixBase64), $font.Bytes, $font.SHA256)
    if ($offset -lt 0) { throw 'An embedded font does not match its reviewed source hash.' }
    $version = [EmbeddedFontNoticeInspector]::Version($assembly, $offset)
    if ($version -ne ('Version ' + $manifest.FontVersion + ';git-' + $manifest.FontSourceCommit)) {
        throw 'An embedded font version differs from the reviewed notice provenance.'
    }
}
[pscustomobject]@{ Result = 'PASS'; VerifiedFonts = $manifest.Fonts.Count; FontVersion = $manifest.FontVersion; FontSourceCommit = $manifest.FontSourceCommit }
