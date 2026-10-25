# Phase 0 : Base de référence (aucun changement fonctionnel)
Branche : `develop` | Commits : `40bdb91` (base) → commit Phase 0 | Date : 2026-10-20

## Objectif
Établir un état des lieux exact et vérifié du dépôt avant toute modification fonctionnelle : état Git, SDK et workloads installés, exécution réelle des tests existants, décompte réel des tests par fichier, vérification ligne par ligne des constats d'audit `A1` à `A19` (plus les constats nouveaux `A20` à `A24`), et mesure de la volumétrie du code.

## Changements réalisés (par item, avec fichiers)
- Aucun fichier de code modifié (conformément à la Porte de validation P0).
- Création de `docs/AUDIT_REGISTER.md` (registre complet des constats `A1`–`A24` avec fichiers et numéros de lignes).
- Création de `docs/phase-reports/PHASE-0.md` (ce rapport).

## Tests ajoutés (noms) et résultat global : 53 réussis / 53 total
Aucun nouveau test ajouté en Phase 0 (phase d'observation pure).

### Décompte réel des tests existants vs affirmations de `RAPPORT_PROJET.md`

| Fichier de test | `[Fact]` | `[Theory]` | `[InlineData]` (cas) | Total cas exécutés | Affirmé dans `RAPPORT_PROJET.md` | Écart |
|---|---|---|---|---|---|---|
| `Database/SqliteRepositoryTests.cs` | 5 | 0 | 0 | **5** | 12 | -7 (surestimé de +140 %) |
| `Auth/LocalAuthServiceTests.cs` | 9 | 1 | 3 | **12** (10 méthodes) | 14 | -2 |
| `ML/BudgetForecastingServiceTests.cs` | 5 | 0 | 0 | **5** | 11 | -6 (surestimé de +120 %) |
| `ML/ExpenseCategorizerServiceTests.cs` | 3 | 1 | 8 | **11** (4 méthodes) | 8 | +3 |
| `UI/UiFormatTests.cs` | 6 | 2 | 14 (6 + 8) | **20** (8 méthodes) | 8 | +12 |
| **TOTAL** | **28** | **4** | **25** | **53 cas** (32 méthodes) | **53** | Total identique mais répartition fictive dans le rapport |

## Preuves (sorties de commandes résumées)

### 1. État Git initial
```text
On branch develop
40bdb91 docs: add comprehensive project report and post-mortem
8679dc3 chore(gitflow): merge feature/integration-layer into develop
66b61de feat: wire Razor UI pages with SQLite repositories, PBKDF2 authentication, and ML.NET prediction engines
2ddc8cb chore(gitflow): merge feature/ui-blazor-components into develop
0ea2bc9 chore(gitflow): merge feature/ml-engine into develop
3b70f59 chore(gitflow): merge feature/data-auth-layer into develop
348baa1 ci: add GitHub Actions Android CI/CD pipeline and configure solution
15bd3ed chore: initialize SmartBudgetML MAUI Blazor Hybrid project scaffold
```

### 2. SDK, Runtimes et Workloads installés sur l'hôte
```text
$ dotnet --list-sdks
10.0.402 [C:\Program Files\dotnet\sdk]

$ dotnet --list-runtimes
Microsoft.AspNetCore.App 10.0.13 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.NETCore.App 8.0.14 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.NETCore.App 10.0.13 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.WindowsDesktop.App 10.0.13 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]

$ Push-Location ..; dotnet workload list; Pop-Location
Version de la charge de travail : 10.0.400-manifests.6055ec36
ID de la charge de travail installée      Version de manifeste      Source de l’installation
--------------------------------------------------------------------------------------------
android                                   36.1.118/10.0.100         VS 18.11.12306.390
ios                                       26.5.10315/10.0.100       VS 18.11.12306.390
maccatalyst                               26.5.10315/10.0.100       VS 18.11.12306.390
maui-windows                              10.0.110/10.0.100         VS 18.11.12306.390
```
*Note : Le SDK **.NET 10.0.402 stable** ainsi que le workload `android` pour .NET 10 sont installés sur le poste. Le fichier `global.json` actuel pointe vers `8.0.426` (`rollForward: latestPatch`), ce qui empêche l'invocation directe de `dotnet` depuis la racine du dépôt tant que `global.json` n'est pas migré en Phase 2.*

### 3. Build Release et exécution des tests
```text
SmartBudgetML.Tests -> C:\Users\abel kenzo\Desktop\App Mobile Smart budget\SmartBudgetML.Tests\bin\Release\net8.0\SmartBudgetML.Tests.dll
La génération a réussi.
    0 Avertissement(s)
    0 Erreur(s)
Temps écoulé 00:00:12.73

Série de tests réussie.
Nombre total de tests : 53
     Réussi(s) : 53
 Durée totale : 10,2349 Secondes
```

### 4. Taille de la base de départ (hors `bin/`, `obj/`, `.vs/`, `bootstrap*`)
```text
Extension  Fichiers  Lignes
---------  --------  ------
.razor           15    1859
.cs              40    2227
.xaml             3      38
.css              1    1559
.csproj           2      57
TOTAL            61    5740
```

## Décisions prises et justifications
- Exécution des commandes `dotnet` en Phase 0 et Phase 1 depuis le répertoire parent (`Push-Location ..`) afin de ne pas modifier `global.json` avant la Phase 2 tout en respectant strictement la Porte P0 (aucun fichier hors `docs/` modifié).

## Écarts par rapport à la spec / constats nouveaux
Tous les constats `A1` à `A19` ont été **confirmés** dans le code source. Cinq constats supplémentaires ont été découverts et ajoutés à `docs/AUDIT_REGISTER.md` :
- **A20** : `Settings.razor` (l.175) appelle `AuthService.GetUserProfileAsync()` alors que `@inject ILocalAuthService AuthService` est manquant dans le composant, et la variable `FeedbackMessage` n'est jamais affichée dans le markup HTML.
- **A21** : `Unlock.razor` (l.35) contient un fallback en dur `if (pin == "1234")` dans son bloc `catch` qui déverrouille l'application en cas d'exception.
- **A22** : `Analytics.razor` (l.78–88, l.225–238) affiche un tracé SVG statique codé en dur et injecte de fausses dépenses (`CurrentTotalSpend = 1200m`, `ProjectedEndMonthSpend = 1750m`) lorsqu'aucune dépense n'est enregistrée pour le mois.
- **A23** : `Platforms/Android/AndroidManifest.xml` déclare `android:allowBackup="true"` et la permission `android.permission.INTERNET`.
- **A24** : `global.json` verrouille le SDK sur `8.0.426` qui n'est plus installé sur la machine hôte (remplacé par `.NET 10.0.402`).

## Statut des fonctionnalités touchées
- Documentation d'audit : `VÉRIFIÉ-TESTS` (53/53 tests existants exécutés sur hôte).

## Ce qui reste ouvert / questions pour l'utilisateur
- Aucune question bloquante en Phase 0. Enchaînement immédiat vers la Phase 1 (`feature/honesty-fixes`).

## Porte de validation : ✅
- `dotnet test` vert (53/53 réussis, 0 warning).
- `docs/AUDIT_REGISTER.md` et `docs/phase-reports/PHASE-0.md` créés.
- Aucun fichier de code modifié (`git status` ne montre que `docs/` et `CLAUDE.md`).
