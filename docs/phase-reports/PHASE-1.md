# Rapport de Phase 1 — « Honesty Fixes » (A2 → A7, A13, A17, A20 → A22)

**Date :** 20 octobre 2026  
**Branche :** `feature/honesty-fixes` → fusionnée dans `develop` (`--no-ff`), tag `Phase-1`  
**Objectif :** Éliminer tous les comportements simulés ou mensongers de l'application afin que chaque fonctionnalité affichée fasse réellement ce qu'elle annonce, avec preuves TDD.

---

## 1. Corrections réalisées

### 1.1 A2 — Suppression des métriques ML fictives (`95.8%`, `120` samples)
- Suppression intégrale de `95.8`, `99.0`, `0.15` et `120` dans `Settings.razor`.
- Création de `IModelEvaluator` / `ModelEvaluator` (`SmartBudgetML/Services/ML/ModelEvaluator.cs`) utilisant `MLContext.MulticlassClassification.Evaluate` sur un jeu de validation distinct du jeu d'entraînement.
- Calcul réel de `MicroAccuracy`, `MacroAccuracy`, `MacroF1` (via la matrice de confusion par classe) et du nombre exact d'échantillons d'entraînement et de validation.
- Avant toute évaluation, l'état retourné est `Unevaluated` (`StatusDisplay = "Non évalué"`, `IsEvaluated = false`).

### 1.2 A3 — Persistance et câblage de `MlConfidenceThreshold`
- Ajout dans `AppSettings` de `MlConfidenceThreshold` (défaut `0.70f`), `AutoCategorizeEnabled`, `OverBudgetAlertsEnabled` et `DailyRemindersEnabled`.
- Création de `IAppSettingsProvider` / `AppSettingsProvider` (`SmartBudgetML/Services/Database/AppSettingsProvider.cs`) gérant la lecture/sauvegarde SQLite et la règle `ShouldAutoSelectCategoryAsync(float predictionConfidence)`.
- Câblage dans `Settings.razor` et `AddExpense.razor` (suppression du `0.70f` codé en dur).

### 1.3 A4 — Export CSV réel conforme RFC 4180
- Création de `IFileExportService` / `LocalFileExportService` et `ICsvExpenseExporter` / `CsvExpenseExporter` (`SmartBudgetML/Services/Data/`).
- Échappement conforme RFC 4180 (guillemets doublés `""`, virgules, retours à la ligne `\r\n`, caractères accentués), encodage UTF-8 avec BOM (`0xEF 0xBB 0xBF`), dates ISO 8601 (`yyyy-MM-dd`), résolution du nom de catégorie (`Category.Name`), séparateur décimal invariant (`.`).
- Vérification sur disque (`FileInfo.Exists && FileInfo.Length > 0`) avant d'afficher le message de succès dans `Settings.razor`.

### 1.4 A5 & A6 — Sauvegarde/Restauration SQLite réelle (`VACUUM INTO`) et taille réelle sur disque
- Implémentation dans `DatabaseService` de :
  - `GetDatabaseSizeBytesAsync()` et `GetFormattedDatabaseSizeAsync()` basés sur `new FileInfo(_databasePath).Length` (suppression de `"240 KB"` et `"180 KB"`).
  - `BackupDatabaseAsync(string destinationPath)` exécutant `VACUUM INTO` et validant l'en-tête SQLite de 16 octets (`"SQLite format 3\0"`) ainsi que `PRAGMA integrity_check;`.
  - `RestoreDatabaseAsync(string backupFilePath)` vérifiant l'en-tête et `PRAGMA integrity_check;` avant de fermer proprement la connexion, remplacer le fichier et réinitialiser les repositories (un fichier corrompu lève `InvalidDataException` sans altérer la base active).

### 1.5 A7 — Persistance des corrections utilisateur dans SQLite (`CategoryFeedback`)
- Création de l'entité SQLite `CategoryFeedback` (`NormalizedDescription`, `RawDescription`, `CategoryName`, `Count`, `UpdatedAt`).
- Mise à jour de `ExpenseCategorizerService` pour persister chaque appel à `RetrainWithFeedbackAsync` dans la table `CategoryFeedback` et recharger les retours utilisateur lors de `TrainAsync()`.

### 1.6 A13 — Formatage monétaire déterministe (`ICurrencyFormatter`)
- Création de `ICurrencyFormatter` / `CurrencyFormatter` (`SmartBudgetML/Services/Formatting/CurrencyFormatter.cs`) indépendant de `Thread.CurrentThread.CurrentCulture`, supportant `XAF`/`FCFA` (0 décimale, séparateur de milliers espace insécable `\u00A0`), `EUR`, `USD`, `GBP`, `MAD`.
- Injection dans `BudgetForecastingService` et suppression de tous les `{overage:C0}`, `{cushion:C0}`, `{savings:C0}`.

### 1.7 A17, A20, A21, A22 — Suppression des `catch { }` vides et correctifs UI d'honnêteté
- Remplacement de tous les blocs `catch` vides par une journalisation structurée via `ILogger<T>` (`Settings.razor`, `AddExpense.razor`, `Profile.razor`, `Unlock.razor`, `MainLayout.razor`, `BiometricService.cs`, `DatabaseConstants.cs`).
- Injection de `ILocalAuthService` et affichage de `@FeedbackMessage` dans `Settings.razor` (`A20`).
- Suppression de la porte dérobée `if (pin == "1234")` dans `Unlock.razor` (`A21`).
- Suppression des montants fictifs (`1200m`, `1750m`, 4 fausses catégories) et calcul dynamique des coordonnées SVG dans `Analytics.razor` (`A22`).

---

## 2. Vérification du Gate P1

1. **Suite de tests xUnit :**
   - Cycle TDD respecté : les 16 nouveaux cas de test dans `Phase1HonestyTests` ont d'abord échoué avant implémentation (`16 échecs / 16`), puis sont passés au vert après implémentation.
   - Total actuel : **69 / 69 tests passants** (`0` échec, `0` avertissement).
2. **Vérification par expression régulière (Gate P1) :**
   - Commande : `Get-ChildItem -Path SmartBudgetML -Recurse -Include *.cs,*.razor | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | Select-String -Pattern '95\.8|"180 KB"|"240 KB"|\{.*:C0\}|catch\s*\{\s*\}'`
   - Résultat : **0 correspondance** (`GATE_P1_REGEX_CLEAN`).
