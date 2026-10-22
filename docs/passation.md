# Passation

> Fichier de reprise entre deux sessions. Écrit par l'agent en fin de lot (ou quand l'utilisateur le demande), lu au début de la session suivante.

## 1. Objectif
Phase 8 : corriger les écarts relevés par l'audit du code `develop` (voir `docs/phase8/00-INDEX.md`).
- **Lot 8A terminé** (`feature/phase8-a-verite` fusionné dans `develop`) : build MAUI Android/Windows réparé, vraies métriques ML exportées (`175/176` soit `99,43 %`), script `./tools/verify.ps1` et `docs/generated/STATUS.md` en place, documents (`README.md`, `RAPPORT_PROJET.md`, `ML_EVALUATION.md`, `PHASE-6.md`, `PHASE-7.md`) et registre (`docs/AUDIT_REGISTER.md`) réalignés, identifiants SMS anonymisés (`N12`), `.gitignore` durci (`N15`).

## 2. Problématique en cours
**Lot 8B en cours** sur `feature/phase8-b-ci`. Push de `develop` et `feature/phase8-b-ci` autorisé par l'utilisateur, uniquement quand `./tools/verify.ps1` est vert en local (annoncer « je pousse maintenant » avant).

Ordre de travail demandé (un commit par étape) :
1. [x] **XA0141 / 16 Ko** : la cause était `sqlite-net-sqlcipher 1.9.172`, qui tire `SQLitePCLRaw.lib.e_sqlcipher 2.1.2` : ses `libe_sqlcipher.so` ont `p_align = 4096`. `SQLitePCLRaw.bundle_e_sqlcipher 2.1.11` (existe sur nuget.org, dernière version stable au 22/10/2026) est référencé directement dans les deux `.csproj` : ses `.so` ont `p_align = 16384`. Preuves de cette session : rebuild `--no-incremental` Android Release = 2 avertissements (2 × CS1570), plus aucun XA0141 ; `python tools/check_elf_alignment.py <apk>` = 24/24 alignées ; `zipalign -c -P 16 -v 4` = « Verification successful » ; tests 139/139. Exigence Google : https://developer.android.com/guide/practices/page-sizes (apps ciblant Android 15+, 64 bits ; mises à jour bloquées à partir du 1er février 2027).
2. [x] **Test instable `SqliteRepositoryTests`** : le chemin était déjà unique par test, mais la connexion sqlite-net n'était jamais fermée. Sous Windows, `File.Delete` échouait donc à chaque test (« being used by another process »), et un `catch {}` vide masquait l'échec (145 bases `smartbudget_test_*` laissées dans `%TEMP%`). Le verrou « en parallèle » d'origine n'a pas été reproduit (0 échec sur 12 exécutions des classes SQLite). Correctif : `IAsyncLifetime`, `CloseAsync()` de la connexion du test, suppression de `.db3/-wal/-shm/-journal`, puis assertion que le fichier a disparu (aucun `catch`). Rouge vérifié avant correctif (5/5 en échec, IOException), vert après. Suite complète 3 fois de suite, parallélisme xUnit par défaut : 139/139 à chaque fois (VÉRIFIÉ-TESTS). Hors périmètre, à traiter plus tard : le même motif (connexion non fermée et `catch {}` vide) existe dans `LocalAuthServiceTests` (224 fichiers `auth_test_*` restés dans `%TEMP%`) et dans `Phase5CameroonLocalizationTests`.
3. [x] **Seuil de fiabilité (point C)**, règle exacte de `BudgetForecastingService.EvaluateReliability`. `DaysElapsed` est le numéro du jour calendaire dans la période (jour du mois de 1 à 28-31 ; en hebdomadaire, jour de la semaine ISO avec lundi = 1). Ce n'est pas le nombre de jours qui ont des dépenses.
   - Mensuel et enveloppe : jours 1 à 6 → `Low` ; jours 7 à 13 → `Medium` ; jour 14 et au-delà → `High`.
   - Hebdomadaire : jours 1 et 2 → `Low` ; jours 3 à 7 → `High` (pas de `Medium`).
   - Aucun trou entre les seuils. Les bornes sont figées par le test `ForecastReliability_FollowsDocumentedDayThresholds` (13 cas, verts). `RAPPORT_PROJET.md` et `docs/ML_EVALUATION.md` disaient déjà la même chose. `PHASE-4.md` omettait le mode hebdomadaire : c'est complété. `README.md` ne donne aucun seuil.
   - Écart restant (pour le lot 8H, textes) : le message `High` du mode mensuel dit « plus de 14 jours », alors que le niveau commence au jour 14. Ces messages sont aussi en français codé en dur dans le service.
   **Point D** : `PHASE8_FICHIER_UNIQUE.md` (non suivi, paquet d'origine déjà éclaté dans `docs/` au commit `e24f683`) a été supprimé.
4. [~] **Auto-test embarqué (fait, commité)** :
   - Cœur `Services/SelfTest/SelfTestRunner.cs`, compilé aussi par les tests. Amorçage Android `SelfTest/SelfTestHost.cs` (MauiSecureKeyStore + logcat, tag `SB_SELFTEST`), lancé une seule fois depuis `App.CreateWindow`. Les deux fichiers sont entièrement sous `#if SB_SELFTEST` ; la constante n'existe que si `-p:EnableSelfTest=true`.
   - 9 étapes : base chiffrée, profil + PIN, dépense, entraînement ML, « Moto bendskin Akwa » → transport, « Facture ENEO » → utilities, vérification du PIN (temps de hachage), prévision, nettoyage. L'auto-test utilise sa propre base `selftest/selftest.db3` et ne purge jamais la clé maître.
   - Tests hôte `SelfTestRunnerTests` (parcours 9/9 et chemin d'échec). Suite : 154/154 (VÉRIFIÉ-TESTS).
   - **Preuve d'absence** : `python tools/check_selftest_absent.py SmartBudgetML/obj/Release/net10.0-android/SmartBudgetML.dll` donne ABSENT sur le build normal et PRESENT avec `--expect-present` sur le build `EnableSelfTest=true` (vérifié localement). Sur l'APK, le contrôle n'est PAS probant : `libassembly-store.so` est compressé, donc il répond ABSENT même sur l'APK d'auto-test. On contrôle donc la DLL IL, qui est l'entrée du linker.
   - Environnement local : pas d'émulateur ni de `gh` sur ce PC. L'émulateur ne tourne qu'en CI. Suivi des runs : API REST publique de GitHub (dépôt public `ElogeKenzo/SmartBudgetML`), ou journaux collés par l'utilisateur.
   - [~] **Workflow CI réécrit** (`.github/workflows/maui-android-ci.yml`) : 5 jobs (test, analyze, build-android, emulator-test, release), `concurrency`, cache NuGet, plus aucun `|| echo` ni `|| true`. Script `.github/scripts/emulator-smoke.sh` (LF, `set -euo pipefail`) : contrôlé localement par `bash -n` et trois simulations avec un faux `adb` (OK, FAIL, pas de DONE). Portes, toutes testées localement dans les deux sens :
     - `tools/check_coverage.py`. **La couverture de l'ancienne CI était vide (0/0 ligne)** : les services sont compilés dans l'assembly de test, que coverlet exclut. Correctif : `SmartBudgetML.Tests/coverage.runsettings` (`IncludeTestAssembly`, exclusion des fichiers de test). Mesure réelle : 88,71 % des lignes (1509/1701, 40 fichiers) et 71,82 % des branches. Seuil : 86,7 %.
     - `tools/inspect_apk.py` (aapt2) : targetSdk 36 ≥ 36, minSdk 24. Permissions : `USE_BIOMETRIC` + `com.smartbudgetml.app.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (déclarée par l'APK lui-même en `protectionLevel=signature`, injectée par AndroidX core). **En attente de la validation de l'utilisateur** (règle 7).
     - `dotnet format --verify-no-changes` sur les deux projets : 4 fichiers corrigés (espaces seulement : `CurrencyFormatter.cs`, `MainPage.xaml.cs`, `Platforms/Android/MainApplication.cs`, `Platforms/Windows/App.xaml.cs`), fins de ligne conservées.
     - Audit `--vulnerable --include-transitive --format json` sur les deux projets : 0 paquet vulnérable.
     - `tools/verify.ps1` : ajout des contrôles « pas de `SB_SELFTEST` » et « 16 Ko ».
   - [x] `./tools/verify.ps1` **PASS** le 22/10/2026 : 154/154 tests, build Android PASS, garde-fous PASS, dont « pas de SB_SELFTEST » et « 16 Ko ». STATUS.md régénéré (commit `58603b8`).
   - [ ] **Bloqué : push.** `git push -u origin feature/phase8-b-ci` échoue (« could not read Username », aucune authentification GitHub disponible dans le terminal de l'agent). C'est à l'utilisateur de pousser depuis son propre terminal. Plan prévu : pousser d'abord seulement `feature/phase8-b-ci`, pour ne pas lancer l'ancien workflow sur `develop` (`origin/develop` = `d220cc8`, `develop` local a 4 commits d'avance, avance rapide possible). Une fois le run vert : fusion `--no-ff` dans `develop`, puis push de `develop`.
   - [~] **Run 1** (https://github.com/ElogeKenzo/SmartBudgetML/actions/runs/39700367345, commit `c30dd1d`, poussé par l'utilisateur). Résultats : tests + couverture SUCCESS (Linux) ; format + audit SUCCESS ; build Android + portes (targetSdk, permissions, 16 Ko, absence SB_SELFTEST, témoin positif) SUCCESS ; **émulateur FAILURE** à l'étape « Run self-test on emulator ». Annotation publique : seulement « /usr/bin/sh failed with exit code 1 ». Les journaux exigent une authentification (403). Cause **inconnue** : il faut les journaux de l'étape (collés par l'utilisateur) ou l'artefact `emulator-results`. Instrumentation ajoutée (ce n'est pas une tentative de correction) : le script émet des annotations `::error::` (raison, étapes FAIL, plantage logcat) et un `trap ERR`. Tentatives de correction utilisées : 0/3. L'utilisateur a accepté la permission interne AndroidX (signature).
   - [ ] Reste : suivi du run (pas de `gh` : API REST publique `https://api.github.com/repos/ElogeKenzo/SmartBudgetML/actions/runs?branch=feature/phase8-b-ci`, ou journaux collés par l'utilisateur ; 3 tentatives de correction au maximum par échec), `docs/CI_RUN_LOG.md`, registre (N9, A16), fusion dans `develop`, tag `Phase8-8B`.
   - [ ] Question ouverte à l'utilisateur : accepte-t-il la permission interne `com.smartbudgetml.app.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (signature, AndroidX) ? Si non : la retirer du manifeste (`tools:node="remove"`) et vérifier les récepteurs AndroidX sur Android < 13.

Changements encore non commités laissés par la session précédente (à reprendre à l'étape 4) : `EnableSelfTest` → `SB_SELFTEST` dans `SmartBudgetML.csproj` (+ correction d'indentation), `SB_SELFTEST` et `coverlet.collector` dans `SmartBudgetML.Tests.csproj` (accepté par l'utilisateur). Fins de ligne : garder celles de chaque fichier (les `.csproj` sont en LF), pas de `.gitattributes`.

## 3. Fichiers importants
- `docs/phase8/00-INDEX.md` et `docs/phase8/8B-ci-reelle.md`
- `tools/verify.ps1` et `docs/generated/STATUS.md`
- `docs/AUDIT_REGISTER.md`
- `.github/workflows/maui-android-ci.yml`

## 4. Ce qui a été essayé et qui a raté
- **Build MAUI initial (`SmartBudgetML.csproj` & `AddExpense.razor`) :**
  1. `error NETSDK1022` sur `Resources\Strings\*.resx` inclus deux fois → corrigé dans `SmartBudgetML.csproj` avec `<EmbeddedResource Remove="Resources\Strings\*.resx" />` suivi de `<EmbeddedResource Include="Resources\Strings\*.resx" WithCulture="false" Type="Non-Resx" />` (ce qui permet à `ResxStringLocalizer` de lire le flux XML `.resx` embarqué).
  2. `error CS1061` dans `AddExpense.razor(321,51)` qui appelait `SettingsProvider.GetSettingsAsync()` au lieu de `GetOrCreateSettingsAsync()` → corrigé.
  3. Avertissements constatés au build `Release` : **4 avertissements** sur `net10.0-android` (`2 × CS1570` commentaires XML dans `OrangeMoneyCameroonParser.cs` et `IrregularIncomePlanner.cs`, et `2 × XA0141` page 16 Ko sur `libe_sqlcipher.so`), **0 avertissement** en incrémental sur `net10.0-windows10.0.19041.0`.
- **Exécution de `tools/verify.ps1` sous Windows PowerShell 5.1 :**
  - Les tirets cadratins UTF-8 sans BOM provoquaient une erreur de parsing en Windows-1252, et `2>&1` avec `$ErrorActionPreference = "Stop"` interrompait `dotnet test` dès qu'un flux stderr était émis → corrigé en gardant `tools/verify.ps1` en ASCII pur, en encadrant les appels `dotnet` avec `$ErrorActionPreference = "Continue"`, et en passant `-- xUnit.MaxParallelThreads=1` pour éviter toute contention du pool SQLite.

## 5. Prochaine étape
Exécuter le lot **8B** (`docs/phase8/8B-ci-reelle.md`).

## Décisions prises
1. Pas de réécriture de l'historique Git pour retirer les anciens identifiants SMS (les originaux restent dans les commits antérieurs, décision #3).
2. `docs/generated/STATUS.md` (généré par `./tools/verify.ps1` à partir du TRX xUnit et de `artifacts/ml-eval.json`) est la source de vérité unique pour les comptes de tests et métriques ML.

## Commandes utiles
- Vérification complète (tests + build Android + garde-fous + génération de `docs/generated/STATUS.md`) :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1
  ```
- Vérification rapide (sans build Android) :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1 -SkipAndroid
  ```
- Build MAUI Release Android / Windows :
  ```powershell
  dotnet build SmartBudgetML -f net10.0-android -c Release
  dotnet build SmartBudgetML -f net10.0-windows10.0.19041.0 -c Release
  ```
