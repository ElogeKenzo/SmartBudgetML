# Rapport de Phase 2 — Migration vers .NET 10 LTS (`feature/dotnet10`)

**Date :** 20 octobre 2026  
**Branche :** `feature/dotnet10` → fusionnée dans `develop` (`--no-ff`), tag `Phase-2`  
**Objectif :** Migrer le dépôt hors de .NET 8 MAUI (fin de support mai 2025, `A14`), supprimer les cibles non-Android dans un commit dédié, éliminer la dépendance bêta `Plugin.Fingerprint 3.0.0-beta.1` (`A15`), et aligner `global.json` (`A24`) ainsi que la CI sur `.NET 10.0.402` LTS.

---

## 1. Travaux réalisés

1. **Suppression dédiée des cibles non-Android (`fdca5cc`) :**
   - Suppression des dossiers `SmartBudgetML/Platforms/iOS`, `SmartBudgetML/Platforms/MacCatalyst` et `SmartBudgetML/Platforms/Tizen` dans le commit atomique `chore: drop non-Android targets`.
2. **Migration vers .NET 10 LTS (`A14`, `A24`) :**
   - `global.json` mis à jour vers le SDK stable `10.0.402` (`rollForward: latestFeature`, `allowPrerelease: false`).
   - `SmartBudgetML/SmartBudgetML.csproj` migré vers `<TargetFrameworks>net10.0-android;net10.0-windows10.0.19041.0</TargetFrameworks>` avec `Microsoft.Maui.Controls` et `Microsoft.AspNetCore.Components.WebView.Maui` (`$(MauiVersion)` = `10.0.110`).
   - `SmartBudgetML.Tests/SmartBudgetML.Tests.csproj` migré vers `<TargetFramework>net10.0</TargetFramework>`.
   - Mise à jour de `SQLitePCLRaw.bundle_green` vers `2.1.11` pour corriger l'avertissement de vulnérabilité `NU1903` sur `2.1.9`.
   - Mise à jour de `.github/workflows/maui-android-ci.yml` (`DOTNET_VERSION: '10.0.x'`, `-f net10.0-android`).
3. **Remplacement de `Plugin.Fingerprint 3.0.0-beta.1` (`A15`) :**
   - Suppression complète du package bêta `Plugin.Fingerprint`.
   - Implémentation directe de `Android.Hardware.Biometrics.BiometricPrompt` et `BiometricManager` derrière l'interface `IBiometricService` dans `SmartBudgetML/Services/Auth/BiometricService.cs` (zéro dépendance bêta).
4. **Correction des dépréciations MAUI .NET 10 :**
   - Remplacement de `MainPage = new MainPage();` (obsolète `CS0618`) dans `SmartBudgetML/App.xaml.cs` par `protected override Window CreateWindow(IActivationState? activationState) => new Window(new MainPage());`.

---

## 2. Vérification du Gate P2

- **SDK .NET 10 stable :** `10.0.402` (vérifié via `dotnet --list-sdks`).
- **Zéro package `-beta` / `-preview` / `-rc` :** vérifié dans `SmartBudgetML.csproj` et `SmartBudgetML.Tests.csproj`.
- **Tests unitaires `net10.0` :** **69 / 69 tests passants** en Release, 0 avertissement.
- **Compilation `net10.0-android` Release :** réussie sans avertissement `NETSDK1202`.
