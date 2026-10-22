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
   - [ ] Reste : script d'émulateur + workflow CI.

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
