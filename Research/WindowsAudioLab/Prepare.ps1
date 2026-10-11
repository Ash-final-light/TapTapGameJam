param([string]$Workspace = 'D:\UnityProject\TapTap_GameJam')
$ErrorActionPreference = 'Stop'
$labRoot = Join-Path $Workspace 'tmp/windows-audio-lab'
foreach ($relative in @('Assets/Editor', 'Assets/Lab', 'Packages', 'ProjectSettings', 'inputs/外部 音频 #100%', 'results')) {
    New-Item -ItemType Directory -Path (Join-Path $labRoot $relative) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BuildLab.cs') -Destination (Join-Path $labRoot 'Assets/Editor/BuildLab.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioImportExperiment.cs') -Destination (Join-Path $labRoot 'Assets/Lab/AudioImportExperiment.cs')
Copy-Item -LiteralPath (Join-Path $Workspace 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $labRoot 'ProjectSettings/ProjectVersion.txt')
$labManifest = '{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.unitywebrequest":"1.0.0","com.unity.modules.unitywebrequestaudio":"1.0.0"}}'
[IO.File]::WriteAllText((Join-Path $labRoot 'Packages/manifest.json'), $labManifest, [Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath (Join-Path $Workspace 'Assets/AudioAsset') -Filter '*.mp3' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $labRoot 'inputs/外部 音频 #100%')
}
Write-Output $labRoot
