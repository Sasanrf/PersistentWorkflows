param([string]$PackageDirectory = 'artifacts/packages', [string]$Version = '1.0.0', [string]$ReportDirectory = 'artifacts/stable-release')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ids = 'PersistentWorkflows.Abstractions', 'PersistentWorkflows.Core', 'PersistentWorkflows.EntityFrameworkCore', 'PersistentWorkflows.EntityFrameworkCore.SqlServer'
$manifest = foreach ($id in $ids) {
    $path = Join-Path $PackageDirectory "$id.$Version.nupkg"
    $symbols = Join-Path $PackageDirectory "$id.$Version.snupkg"
    foreach ($file in $path, $symbols) { if (-not (Test-Path -LiteralPath $file)) { throw "Missing package: $file" } }
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        foreach ($required in "lib/net10.0/$id.dll", "lib/net10.0/$id.xml", 'README.md', "$id.nuspec") {
            if (-not $zip.GetEntry($required)) { throw "Missing $required in $id" }
        }
        $reader = [IO.StreamReader]::new($zip.GetEntry("$id.nuspec").Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $spec.package.metadata
        if ($metadata.id -ne $id -or $metadata.version -ne $Version -or $metadata.license.InnerText -ne 'MIT') { throw "Invalid identity/version/license for $id" }
        if ($metadata.readme -ne 'README.md') { throw "Missing README metadata for $id" }
        $deps = @($metadata.dependencies.group.dependency | Where-Object id -like 'PersistentWorkflows.*')
        foreach ($dep in $deps) { if ($dep.version -notin @($Version, "[$Version, )", "[$Version]")) { throw "Incorrect internal dependency: $($dep.id) $($dep.version)" } }
    } finally { $zip.Dispose() }
    $symbolZip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $symbols).Path)
    try { if (-not @($symbolZip.Entries | Where-Object FullName -like '*.pdb').Count) { throw "No PDB in $symbols" } } finally { $symbolZip.Dispose() }
    [pscustomobject]@{ Id=$id; Version=$Version; PackageSHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; SymbolsSHA256=(Get-FileHash -LiteralPath $symbols -Algorithm SHA256).Hash }
}
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$manifest | ConvertTo-Json | Set-Content (Join-Path $ReportDirectory 'package-manifest.json')
$manifest | Format-Table Id,Version
