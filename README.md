# RealisticDocking v0.1.0-beta

Experimental docking-realism plugin for Kerbal Space Program 1.12.5, designed with RSS/Realism Overhaul use in mind.

## Beta 0.1

- Keeps stock `ModuleDockingNode` for save/mod compatibility.
- Adds capture limits for closing speed, angular error, and lateral offset.
- Suppresses stock magnetic acquisition outside the permitted envelope.
- Stages low-force soft capture before stronger hard capture.
- Shows capture state, closing rate, angular error, and lateral offset in the PAW.
- Includes a ModuleManager patch for existing docking ports.

## Installation

Copy `GameData/RealisticDocking` into your KSP `GameData` folder. ModuleManager is required.

## Automatic GitHub build

The repository builds on GitHub Actions against stripped KSP 1.12.5 reference assemblies from KSPModdingLibs/KSPLibs. No proprietary KSP game DLLs are committed here.

A successful run produces an artifact named `RealisticDocking-v0.1.0-beta` containing an installable `GameData` folder.

## Local build

```bat
msbuild Source\RealisticDocking\RealisticDocking.csproj /t:Rebuild /p:Configuration=Release /p:KSPManaged="D:\KSP\KSP_x64_Data\Managed"
```

## Current limitation

This first beta controls the capture envelope and acquisition forces. Individual IDSS/APAS/Soyuz capture-ring travel, hooks/latches, petals, rebound energy and structural sequencing are planned for later versions.
