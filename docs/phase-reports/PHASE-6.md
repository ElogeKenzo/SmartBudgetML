# Rapport d'Exécution — Phase 6 : Fonctionnalités à Forte Valeur Locale (Tontines, Revenus Irréguliers, Architecture SMS Mobile Money)

## 1. Résumé des changements réalisés
La Phase 6 ancre les services métier de **SmartBudget ML** dans les usages économiques camerounais à travers trois blocs :

1. **Bloc 6.1 — Architecture et Parseurs d'Import SMS Mobile Money (Sans permission `READ_SMS` / `RECEIVE_SMS`)** :
   - Rédaction de [`docs/DECISION_SMS.md`](../DECISION_SMS.md) justifiant le choix de l'Option B (Copier-Coller & Partage Android `ACTION_SEND`) conformément à la politique Google Play sur les permissions SMS.
   - Création du modèle normalisé [`ParsedMobileMoneyTransaction`](../../SmartBudgetML/Models/ParsedMobileMoneyTransaction.cs) (`Operator`, `Type`, `Amount`, `Fee`, `BalanceAfter`, `Counterparty`, `Reference`, `Date`, `IsIncome`).
   - Implémentation de [`IMobileMoneySmsParser`](../../SmartBudgetML/Services/Sms/IMobileMoneySmsParser.cs), [`ISmsParserRegistry`](../../SmartBudgetML/Services/Sms/IMobileMoneySmsParser.cs), [`SmsParserRegistry`](../../SmartBudgetML/Services/Sms/SmsParserRegistry.cs) (analyse par lot, dédoublonnage strict par `Reference` / `TxID` ou par fenêtre temporelle `Date ±2 min` + `Amount` + `Operator`) et [`MobileMoneyAmountParser`](../../SmartBudgetML/Services/Sms/SmsParserRegistry.cs) (gestion des séparateurs `1.500`, `1 500`, `1,500`, `1500 FCFA`).
   - Implémentation de [`OrangeMoneyCameroonParser`](../../SmartBudgetML/Services/Sms/OrangeMoneyCameroonParser.cs) et [`MtnMomoCameroonParser`](../../SmartBudgetML/Services/Sms/MtnMomoCameroonParser.cs) calibrés sur les formats SMS camerounais et testés avec des identifiants/soldes anonymisés dans [`Phase6LocalValueTests.cs`](../../SmartBudgetML.Tests/LocalValue/Phase6LocalValueTests.cs).
2. **Bloc 6.2 — Gestion des Tontines / Njangi (Couche Service & Modèles)** :
   - Création des entités SQLite [`Tontine`](../../SmartBudgetML/Models/TontineModels.cs), [`TontineMember`](../../SmartBudgetML/Models/TontineModels.cs), [`TontineRound`](../../SmartBudgetML/Models/TontineModels.cs), et [`TontineContribution`](../../SmartBudgetML/Models/TontineModels.cs) provisionnées dans [`DatabaseService`](../../SmartBudgetML/Services/Database/DatabaseService.cs).
   - Implémentation de [`TontineCalculator`](../../SmartBudgetML/Services/Tontines/TontineCalculator.cs) : calcul du pot ($\text{Cotisation} \times \text{Membres actifs}$), calendrier des tours (hebdomadaire, bimensuel, mensuel avec passage d'année novembre → mars), gestion du retrait d'un membre en cours de cycle (`IsWithdrawn`), détection des retards (`DetectLateContributions`), et calcul du solde net ($\text{Total Reçu} - \text{Total Cotisé}$).
   - *Note d'audit Phase 8 (`N1`, `N2`, `N13`) :* La page `Tontines.razor` créée en Phase 6 utilisait des données en mémoire et n'était pas reliée à la barre de navigation ; la persistance SQLite complète et l'écran d'import SMS dédié sont traités dans les lots 8E et 8F.
3. **Bloc 6.3 — Revenus Irréguliers et Solidarité Familiale** :
   - Ajout de [`IncomeProfileType`](../../SmartBudgetML/Models/IncomeProfileType.cs) (`Fixed`, `Irregular`, `Mixed`), `VitalMinimumSpend` et `MonthlySolidarityTarget` sur [`UserProfile`](../../SmartBudgetML/Models/UserProfile.cs).
   - Implémentation de [`IrregularIncomePlanner`](../../SmartBudgetML/Services/Budget/IrregularIncomePlanner.cs) : moyenne glissante sur 3 à 6 mois (`ComputeRollingAverageIncome`), amortissement sur réserve lors d'un mois à revenu nul (sans division par zéro), découpage en enveloppes hebdomadaires (`BuildWeeklyEnvelopePlan`), et suivi du ratio « Aide familiale / Solidarité » (`AnalyzeFamilySolidarity`).
   - *Note d'audit Phase 8 (`N14`) :* Le branchement de `IrregularIncomePlanner` dans l'interface utilisateur est traité en Lot 8E.

## 2. Vérification de la Porte P6 (Gate P6)
- Résultats des garde-fous et des tests : voir [`docs/generated/STATUS.md`](../generated/STATUS.md) (généré par `./tools/verify.ps1`).
- `docs/DECISION_SMS.md` : **présent et documenté**.
- Statut des parseurs `OrangeMoneyCameroonParser` et `MtnMomoCameroonParser` : **IMPLÉMENTÉS & VÉRIFIÉS-TESTS** dans `Phase6LocalValueTests.cs` (intégration UI complète en Lot 8F).
