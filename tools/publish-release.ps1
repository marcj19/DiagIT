# Publica uma Release no GitHub usando o login que o Git ja guardou (Git Credential Manager).
# O token nunca e exibido. Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-release.ps1 -Tag v1.0.0 -Zip release\DiagIT-v1.0.0-win-x64.zip -Notes release\notes-v1.0.0.md
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Zip,
    [string]$Notes = "",
    [string]$Repo = "marcj19/DiagIT",
    [string]$Branch = "main"
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$git = (Get-Command git -ErrorAction SilentlyContinue)
$gitExe = if ($git) { $git.Source } else { 'C:\Program Files\Git\cmd\git.exe' }

# credencial guardada pelo Git (mesma usada no git push)
# Sem BOM na entrada do git: com o console em UTF-8 "com BOM" o Windows PowerShell poe EF BB BF antes do
# texto e o git recusa a linha "protocol=" ("credential missing protocol field").
$prevIn = [Console]::InputEncoding
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
$psi = New-Object System.Diagnostics.ProcessStartInfo $gitExe, 'credential fill'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.CreateNoWindow = $true
$psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
$proc = [Diagnostics.Process]::Start($psi)
$req = [Text.Encoding]::ASCII.GetBytes("protocol=https`nhost=github.com`n`n")
$proc.StandardInput.BaseStream.Write($req, 0, $req.Length)
$proc.StandardInput.Close()
$cred = $proc.StandardOutput.ReadToEnd() -split "`r?`n"
$proc.WaitForExit()
[Console]::InputEncoding = $prevIn
$line =$cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
if (-not $line) { throw "Nenhum login do GitHub guardado. Rode um 'git push' antes." }
$token = $line.Substring(9)

$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'DiagIT-release-script'
}

$body = ""
if ($Notes -and (Test-Path -LiteralPath $Notes)) { $body = [IO.File]::ReadAllText((Resolve-Path $Notes).Path, [Text.Encoding]::UTF8) }

# release ja existe para esta tag?
$release = $null
try { $release = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers } catch { }

if (-not $release) {
    $json = @{ tag_name = $Tag; target_commitish = $Branch; name = "DiagIT $Tag"; body = $body; draft = $false; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" -Headers $headers `
        -Body ([Text.Encoding]::UTF8.GetBytes($json)) -ContentType 'application/json; charset=utf-8'
    Write-Host "Release criada: $($release.html_url)" -ForegroundColor Green
} else {
    Write-Host "Release ja existia: $($release.html_url)" -ForegroundColor Yellow
}

# envia o zip (substitui se ja houver um com o mesmo nome)
$name = [IO.Path]::GetFileName($Zip)
foreach ($a in $release.assets) {
    if ($a.name -eq $name) { Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$Repo/releases/assets/$($a.id)" -Headers $headers | Out-Null }
}
$upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name"
$asset = Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -InFile (Resolve-Path $Zip).Path -ContentType 'application/zip'
Write-Host ("Arquivo anexado: {0} ({1:N0} bytes)" -f $asset.name, $asset.size) -ForegroundColor Green
Write-Host "Download: $($asset.browser_download_url)"
