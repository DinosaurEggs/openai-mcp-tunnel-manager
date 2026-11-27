# Verifies the public, local-only official CLI contract without a real API key.
param([string]$Version = "v0.0.21")
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()) {
    "X64" { "amd64" }
    "Arm64" { "arm64" }
    default { throw "Unsupported architecture" }
}
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("TunnelManagerContract-" + [Guid]::NewGuid().ToString("N"))
$oldProfileDir = $env:TUNNEL_CLIENT_PROFILE_DIR
$oldStateDir = $env:TUNNEL_CLIENT_STATE_DIR
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $env:TUNNEL_CLIENT_PROFILE_DIR = Join-Path $root "profiles"
    $env:TUNNEL_CLIENT_STATE_DIR = Join-Path $root "state"
    $assetName = "tunnel-client-$Version-windows-$architecture.zip"
    $release = Invoke-RestMethod "https://api.github.com/repos/openai/tunnel-client/releases/tags/$Version" -Headers @{ "User-Agent" = "OpenAITunnelManager-contract-tests" }
    $asset = @($release.assets | Where-Object name -eq $assetName)
    if ($asset.Count -ne 1) { throw "Expected one official full client asset: $assetName" }
    $manifestAsset = @($release.assets | Where-Object name -eq "SHA256SUMS.txt")
    if ($manifestAsset.Count -ne 1) { throw "Missing checksum manifest" }
    $archive = Join-Path $root $assetName
    $manifest = Join-Path $root "SHA256SUMS.txt"
    Invoke-WebRequest $asset[0].browser_download_url -OutFile $archive
    Invoke-WebRequest $manifestAsset[0].browser_download_url -OutFile $manifest
    $pattern = "^(?<hash>[a-fA-F0-9]{64})\s+\*?" + [regex]::Escape($assetName) + "$"
    $matches = @(Get-Content $manifest | ForEach-Object { [regex]::Match($_.Trim(), $pattern) } | Where-Object Success)
    if ($matches.Count -ne 1) { throw "Missing or ambiguous SHA256 entry" }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $matches[0].Groups["hash"].Value.ToLowerInvariant()) { throw "SHA256 mismatch" }
    if ($asset[0].digest -and $asset[0].digest -ne "sha256:$hash") { throw "Release digest mismatch" }
    Expand-Archive -Path $archive -DestinationPath (Join-Path $root "bin")
    $executables = @(Get-ChildItem (Join-Path $root "bin") -Recurse -File -Filter "tunnel-client.exe")
    if ($executables.Count -ne 1) { throw "Expected exactly one full tunnel-client executable" }
    $exe = $executables[0].FullName
    function Invoke-Client([string[]]$Arguments) {
        $output = & $exe @Arguments
        if ($LASTEXITCODE -ne 0) { throw ("Official CLI failed: " + ($Arguments -join " ")) }
        return ($output -join "`n")
    }
    $versionText = Invoke-Client @("--version")
    if ($versionText -notmatch [regex]::Escape($Version.TrimStart('v'))) { throw "Unexpected version $versionText" }
    foreach ($command in @("profiles", "runtimes", "doctor")) {
        Invoke-Client @($command, "--help") | Out-Null
    }
    $source = Join-Path $root "source.yaml"
    @'
config_version: 1
control_plane:
  tunnel_id: tunnel_ab12_0123456789abcdef0123456789abcdef
  api_key: env:MANAGER_CONTRACT_TEST_KEY
  residency: global
mcp:
  oauth_discovery: disabled
  server_urls:
    - channel: main
      url: http://127.0.0.1:8000/mcp
'@ | Set-Content $source -Encoding utf8
    Invoke-Client @("profiles", "add", "contract-test", "--from-file", $source) | Out-Null
    $profiles = @(Invoke-Client @("profiles", "list", "--json") | ConvertFrom-Json)
    if ($profiles.Count -ne 1 -or $profiles[0].name -ne "contract-test") { throw "Profile list contract mismatch" }
    $profilePath = $profiles[0].path
    $original = (Get-FileHash $profilePath -Algorithm SHA256).Hash
    $duplicate = & $exe profiles add contract-test --from-file $source 2>&1
    if ($LASTEXITCODE -eq 0) { throw "Duplicate profile creation unexpectedly succeeded" }
    if ((Get-FileHash $profilePath -Algorithm SHA256).Hash -ne $original) { throw "Duplicate profile creation modified the file" }
    Invoke-Client @("profiles", "samples", "list") | Out-Null
    $runtime = Invoke-Client @("runtimes", "list", "--json") | ConvertFrom-Json
    if ($null -eq $runtime.PSObject.Properties["aliases"] -or $runtime.aliases.Count -ne 0) { throw "Runtime inventory contract mismatch" }
    if ([System.IO.Path]::GetFullPath($runtime.state_root) -ne [System.IO.Path]::GetFullPath($env:TUNNEL_CLIENT_STATE_DIR)) { throw "State root mismatch" }
    $cleanup = Invoke-Client @("runtimes", "cleanup", "--json") | ConvertFrom-Json
    if ($null -eq $cleanup.PSObject.Properties["entries"]) { throw "Cleanup result contract mismatch" }
    Write-Host "Official $Version $architecture local CLI contract passed"
}
finally {
    $env:TUNNEL_CLIENT_PROFILE_DIR = $oldProfileDir
    $env:TUNNEL_CLIENT_STATE_DIR = $oldStateDir
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
