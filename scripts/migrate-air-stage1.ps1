<#
  AIR Migration — Stage 1: Extraktion von AAIA.Air + AAIA.Air.Mcp
  ---------------------------------------------------------------
  Mechanischer Teil: verschiebt die Runtime- und MCP-Dateien aus dem Module Manager
  in zwei eigene Projekte und zieht die Namespaces nach. Stage 2 (Contracts + SDK
  abspalten) ist im Guide dokumentiert.

  Voraussetzungen: git, dotnet SDK 8, Ausführung aus dem Repo-Root (aaia-module-manager).
  Sicher: läuft in einem neuen Branch; alles ist per `git` reversibel.

  WICHTIG: Danach `dotnet build` ausführen und die im Guide genannten manuellen
  Fixups erledigen (v. a. AppConfig.McpBridge using, ggf. AiRuntimeEvent-Referenzen).
#>

param(
    # Wenn gesetzt: nach der Migration direkt `dotnet build` ausführen.
    [switch]$BuildAfterMigration
)

$ErrorActionPreference = "Stop"

$root      = (Get-Location).Path
$mmDir     = "src/AAIA.ModuleManager"
$mmProj    = "$mmDir/AAIA.ModuleManager.csproj"
$testProj  = "src/AAIA.ModuleManager.Tests"
$sln       = "AAIA.ModuleManager.sln"
$branch    = "air-extraction"

if (-not (Test-Path $sln)) { throw "Bitte aus dem Repo-Root (aaia-module-manager) ausführen." }

# ── Sicherheitscheck 1: sauberer Arbeitsstand ──────────────────────────────
# Migration darf nie in einen schmutzigen Working Tree verschieben.
if ((git status --porcelain)) {
    throw "Arbeitsstand ist nicht sauber. Bitte committen/stashen, dann erneut ausführen."
}

# ── Sicherheitscheck 2: Branch existiert nicht blind überschreiben ──────────
Write-Host "== Branch '$branch' ==" -ForegroundColor Cyan
if (git branch --list $branch) {
    throw "Branch '$branch' existiert bereits. Bitte löschen/umbenennen oder darauf wechseln, dann erneut ausführen."
}
git checkout -b $branch

# ── 1. Zielprojekte anlegen ────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path "src/AAIA.Air"     | Out-Null
New-Item -ItemType Directory -Force -Path "src/AAIA.Air.Mcp" | Out-Null

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>AAIA.Air</RootNamespace>
    <AssemblyName>AAIA.Air</AssemblyName>
  </PropertyGroup>
</Project>
'@ | Set-Content "src/AAIA.Air/AAIA.Air.csproj" -Encoding UTF8

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>AAIA.Air.Mcp</RootNamespace>
    <AssemblyName>AAIA.Air.Mcp</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.4.0" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.4.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AAIA.Air\AAIA.Air.csproj" />
  </ItemGroup>
</Project>
'@ | Set-Content "src/AAIA.Air.Mcp/AAIA.Air.Mcp.csproj" -Encoding UTF8

# AiRuntimeComposition wandert nach Air.Mcp (verdrahtet den Server).
# Alles andere aus Services/Ai/Runtime wandert nach Air.

Write-Host "== Dateien verschieben (git mv) ==" -ForegroundColor Cyan
# Composition zuerst zu Mcp markieren (vor dem Pauschal-Move).
git mv "$mmDir/Services/Ai/Runtime/AiRuntimeComposition.cs" "src/AAIA.Air.Mcp/AiRuntimeComposition.cs"

# Restliche Runtime-Dateien (inkl. Unterordner) nach AAIA.Air.
Get-ChildItem "$mmDir/Services/Ai/Runtime" -Recurse -File -Filter *.cs | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path "$mmDir/Services/Ai/Runtime").Path.Length + 1)
    $dest = Join-Path "src/AAIA.Air" $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    git mv $_.FullName $dest
}

# MCP-Adapterdateien nach AAIA.Air.Mcp.
Get-ChildItem "$mmDir/Services/Ai/Mcp" -Recurse -File -Filter *.cs | ForEach-Object {
    git mv $_.FullName (Join-Path "src/AAIA.Air.Mcp" $_.Name)
}

# ── 2. Namespaces nachziehen ───────────────────────────────────────────────
Write-Host "== Namespaces ersetzen ==" -ForegroundColor Cyan
$replacements = @(
    @{ from = "AAIA.ModuleManager.Services.Ai.Runtime"; to = "AAIA.Air" },
    @{ from = "AAIA.ModuleManager.Services.Ai.Mcp";     to = "AAIA.Air.Mcp" }
)
# In allen .cs der neuen Projekte, der verbleibenden Integration und der Tests.
$targets = @("src/AAIA.Air", "src/AAIA.Air.Mcp", "$mmDir/Services/Ai/Integration", $testProj, $mmDir)
foreach ($t in $targets) {
    Get-ChildItem $t -Recurse -File -Filter *.cs | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        foreach ($r in $replacements) { $content = $content -replace [regex]::Escape($r.from), $r.to }
        Set-Content $_.FullName $content -Encoding UTF8
    }
}

# ── 3. Module-Manager-csproj: SDK-Refs raus, ProjectRefs rein ───────────────
Write-Host "== Module-Manager-Referenzen anpassen ==" -ForegroundColor Cyan
$mm = Get-Content $mmProj -Raw
$mm = $mm -replace '(?m)^\s*<PackageReference Include="ModelContextProtocol(\.AspNetCore)?" Version="[^"]*" />\s*$', ''
if ($mm -notmatch 'AAIA\.Air\.csproj') {
    $mm = $mm -replace '(?s)(</Project>\s*)$', @"
  <ItemGroup>
    <ProjectReference Include="..\AAIA.Air\AAIA.Air.csproj" />
    <ProjectReference Include="..\AAIA.Air.Mcp\AAIA.Air.Mcp.csproj" />
  </ItemGroup>
</Project>
"@
}
Set-Content $mmProj $mm -Encoding UTF8

# ── 4. Solution aktualisieren ──────────────────────────────────────────────
Write-Host "== dotnet sln add ==" -ForegroundColor Cyan
dotnet sln $sln add "src/AAIA.Air/AAIA.Air.csproj"
dotnet sln $sln add "src/AAIA.Air.Mcp/AAIA.Air.Mcp.csproj"

# ── Sicherheitscheck 3: optionaler Build direkt nach der Migration ──────────
if ($BuildAfterMigration) {
    Write-Host "== dotnet build (BuildAfterMigration) ==" -ForegroundColor Cyan
    dotnet build $sln
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Build fehlgeschlagen — das ist nach Stage 1 erwartbar (manuelle Fixups offen)." -ForegroundColor Yellow
        Write-Host "Siehe Fixup-Liste unten. Branch '$branch' behalten, Fehler abarbeiten, erneut bauen." -ForegroundColor Yellow
    } else {
        Write-Host "Build erfolgreich." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "FERTIG (Stage 1). Jetzt:" -ForegroundColor Green
if (-not $BuildAfterMigration) { Write-Host "  1) dotnet build $sln   (oder Script mit -BuildAfterMigration erneut nutzen)" }
Write-Host "  2) Manuelle Fixups laut docs/air/air-platform-split.md:"
Write-Host "     - AppConfig.cs: 'using AAIA.Air.Mcp;' ergaenzen, Typ 'Ai.Mcp.AaiaMcpBridgeOptions' -> 'AaiaMcpBridgeOptions'"
Write-Host "     - SDK-Naht in AAIA.Air.Mcp/AaiaMcpServer.cs gegen installierte MCP-SDK-Version pruefen"
Write-Host "  3) dotnet test"
Write-Host "  4) Stage 2 (Contracts + SDK) laut Guide."
