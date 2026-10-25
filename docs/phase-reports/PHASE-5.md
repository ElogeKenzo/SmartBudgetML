# Rapport d'Exécution — Phase 5 : Localisation Cameroun (FR/EN, XAF/FCFA, Catégories, Ergonomie)

## 1. Résumé des changements réalisés
La Phase 5 adapte intégralement l'interface, les devises, les catégories par défaut et l'ergonomie de **SmartBudget ML** aux réalités des utilisateurs camerounais (francophones et anglophones) sur smartphones Android d'entrée de gamme :

1. **Internationalisation bilingue FR/EN (Français par défaut)** :
   - Création des fichiers de ressources [`AppStrings.fr.resx`](../../SmartBudgetML/Resources/Strings/AppStrings.fr.resx) et [`AppStrings.en.resx`](../../SmartBudgetML/Resources/Strings/AppStrings.en.resx) avec **117 clés** (parité stricte 100 % FR ↔ EN, aucune valeur vide).
   - Implémentation de [`IStringLocalizer<T>`](../../SmartBudgetML/Services/Localization/IStringLocalizer.cs) et [`ResxStringLocalizer<T>`](../../SmartBudgetML/Services/Localization/ResxStringLocalizer.cs) (langue par défaut `"fr"`, changement de langue à chaud via `SetLanguage("fr" | "en")` et événement `OnLanguageChanged`, persisté dans `AppSettings.Language`).
   - Remplacement de toutes les chaînes anglaises codées en dur dans `MainLayout.razor`, `NavMenu.razor`, `Home.razor`, `Analytics.razor`, `AddExpense.razor`, `Profile.razor`, `Settings.razor`, `Unlock.razor`, `Onboarding.razor`, `TransactionItem.razor` et `NumericKeypad.razor`.
2. **Devise par défaut XAF (`FCFA`)** :
   - `AppSettings.CurrencyCode` par défaut = `"XAF"` ; `AppSettings.Language` par défaut = `"fr"`.
   - `UserProfile.CurrencySymbol` par défaut = `"FCFA"`.
   - `UiFormat.Money` délègue le formatage XAF/FCFA à [`CurrencyFormatter`](../../SmartBudgetML/Services/Formatting/CurrencyFormatter.cs) (`"25 000 FCFA"`, 0 décimale, séparateur de milliers espace).
   - Sélecteurs de devises dans `Onboarding.razor` et `Profile.razor` : `FCFA` en premier choix, suivi de `€`, `$`, `£`, `₦`.
3. **17 Catégories camerounaises par défaut & Migration non destructive (`A12`)** :
   - Ajout de la propriété stable `Key` sur [`Category`](../../SmartBudgetML/Models/Category.cs).
   - Création de [`CategoryCatalog`](../../SmartBudgetML/Services/Database/CategoryCatalog.cs) définissant **14 catégories de dépenses** (`food_market`, `transport`, `housing_rent`, `utilities`, `telecom_data`, `healthcare`, `education`, `tontine`, `family_solidarity`, `events`, `church_donations`, `entertainment`, `savings`, `other`) et **3 catégories de revenus** (`salary`, `business_hustle`, `transfers_received`), avec libellés FR/EN, budgets suggérés en FCFA, et plafonds initiaux à `0m`.
   - Migration automatique `MigrateLegacyCategoriesAsync` dans [`DatabaseService`](../../SmartBudgetML/Services/Database/DatabaseService.cs) : associe les clés stables aux catégories existantes sans écraser les catégories personnalisées ni casser les clés étrangères `Expense.CategoryId`, et insère les nouvelles catégories camerounaises manquantes.
4. **Ergonomie bas de gamme & Taille APK** :
   - Suppression du dossier inutilisé `wwwroot/css/bootstrap/` (~600 Ko) et des polices externes Google Fonts bloquantes dans `wwwroot/index.html` (100 % hors-ligne).
   - Cibles tactiles `min-height: 48px; min-width: 48px;` et support `@media (prefers-reduced-motion: reduce)` dans `wwwroot/css/app.css`.
   - Configuration du linker/trimming Android Release (`AndroidLinkMode=SdkOnly`, `PublishTrimmed=true`, `TrimMode=partial`) dans `SmartBudgetML.csproj`.

## 2. Fichiers créés et modifiés
- `SmartBudgetML/Resources/Strings/AppStrings.cs` *(créé)*
- `SmartBudgetML/Resources/Strings/AppStrings.fr.resx` *(créé)*
- `SmartBudgetML/Resources/Strings/AppStrings.en.resx` *(créé)*
- `SmartBudgetML/Services/Localization/IStringLocalizer.cs` *(créé)*
- `SmartBudgetML/Services/Localization/ResxStringLocalizer.cs` *(créé)*
- `SmartBudgetML/Services/Database/CategoryCatalog.cs` *(créé)*
- `SmartBudgetML/Models/Category.cs` *(modifié — ajout `Key`)*
- `SmartBudgetML/Models/AppSettings.cs` *(modifié — `CurrencyCode = "XAF"`, `Language = "fr"`)*
- `SmartBudgetML/Models/UserProfile.cs` *(modifié — `CurrencySymbol = "FCFA"`)*
- `SmartBudgetML/Services/Auth/LocalAuthService.cs` *(modifié — défauts XAF/FCFA/fr)*
- `SmartBudgetML/Services/Database/DatabaseService.cs` *(modifié — `SeedDefaultDataAsync` + `MigrateLegacyCategoriesAsync`)*
- `SmartBudgetML/Components/Shared/UiFormat.cs` *(modifié — icônes camerounaises + formatage FCFA)*
- `SmartBudgetML/Components/_Imports.razor`, `Layout/*.razor`, `Pages/*.razor`, `Shared/*.razor` *(modifiés — localisation complète `IStringLocalizer<AppStrings>`)*
- `SmartBudgetML/wwwroot/index.html`, `SmartBudgetML/wwwroot/css/app.css` *(modifiés — suppression Bootstrap/Google Fonts, cibles 48px, `prefers-reduced-motion`)*
- `SmartBudgetML.Tests/Localization/Phase5CameroonLocalizationTests.cs` *(créé)*
- `SmartBudgetML.Tests/Database/SqliteRepositoryTests.cs` *(modifié — assertions mises à jour pour les 17 catégories camerounaises et XAF/fr par défaut)*

## 3. Commandes exécutées et résultats
| Commande | Résultat |
|---|---|
| `dotnet test SmartBudgetML.Tests/SmartBudgetML.Tests.csproj --no-restore` | **Réussi : 126/126 tests passent, 0 échec, 0 warning** |

## 4. État du Registre d'Audit (`docs/AUDIT_REGISTER.md`)
- **Corrigé en Phase 5 (`FIXED`)** : `A12` (UI et catégories FR/EN, devise par défaut XAF/FCFA, 17 catégories camerounaises avec plafonds initiaux à 0 et suggestions en FCFA).
- **Restent ouverts pour les phases suivantes** : `A16` (Phase 7 CI émulateur Android), `A18` (Phase 8 rapport final).
