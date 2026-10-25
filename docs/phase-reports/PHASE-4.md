# Rapport de Phase 4 — Catégorisation Hybride 3 Étages, Corpus Camerounais & Prévisions (`A10`, `A11`, `A19`)

**Date :** 21 octobre 2026  
**Branche :** `feature/ml-hybrid` → fusionnée dans `develop` (`--no-ff`), tag `Phase-4`  
**Objectif :** Remplacer le classifieur fragile et bloquant par un pipeline hybride à 3 étages (Mémoire utilisateur → Règles de mots-clés → ML.NET SDCA), fournir un corpus synthétique camerounais versionné (`seed_fr.csv` + `holdout_fr.csv`), persister le modèle via `IModelStore`, éliminer `GetAwaiter().GetResult()`, et améliorer la prévision budgétaire pour les charges fixes et revenus irréguliers.

---

## 1. Réalisations techniques

### 1.1 Pipeline de catégorisation hybride à 3 étages (`A10`, `A11`)
- **Étage 1 (`PredictionSource.UserMemory`) :** Recherche exacte et normalisée dans la mémoire utilisateur (`CategoryFeedback` persistée en SQLite). Priorité maximale avec confiance `1.0`.
- **Étage 2 (`PredictionSource.Rules` — `KeywordRuleCategorizer`) :** Normalisation déterministe (suppression des accents Unicode `FormD`, minuscules, suppression des montants/chiffres et devises `FCFA`/`XAF`, filtrage des mots vides FR/EN) et dictionnaire hiérarchisé d'expressions et mots-clés camerounais (`eneo`, `camwater`, `bendskin`, `yango`, `kia taxi`, `njangi`, `tontine`, `coartem`, `canalolympia`, `om maman`, `marche mokolo`, etc.). Retourne le mot-clé déclencheur (`MatchedRuleKeyword`) pour affichage explicite dans l'UI.
- **Étage 3 (`PredictionSource.Model` — `ML.NET SdcaMaximumEntropy`) :** Classifieur multi-classe TF-IDF exécuté en secours lorsque ni la mémoire ni les règles ne correspondent, validé contre le seuil `MlConfidenceThreshold` (repli sur `Other` / `PredictionSource.Fallback` si la confiance est inférieure au seuil).

### 1.2 Suppression de `GetAwaiter().GetResult()`, `IModelStore` & Réentraînement par lots (`A10`)
- **`Predict()` 100 % synchrone et non-bloquant :** Suppression complète de `TrainAsync().GetAwaiter().GetResult()`. Si le modèle ML n'est pas encore chargé, `Predict()` s'appuie instantanément (< 1 ms) sur les Étages 1 et 2.
- **Persistance du modèle (`IModelStore` / `FileModelStore`) :** Sauvegarde atomique du modèle ML.NET (`.zip`) et de sa version de schéma (`.meta`, `CurrentSchemaVersion = 2`). Chargement direct au démarrage (~18 ms) sans réentraînement à froid si le schéma est compatible.
- **Réentraînement par lots (`RecordFeedbackAsync`) :** Ne réentraîne plus à chaque dépense enregistrée. La mémoire utilisateur (Étage 1) et SQLite sont mises à jour immédiatement, tandis que le réentraînement complet ML.NET n'est déclenché qu'après `RetrainBatchThreshold = 15` retours accumulés (avec échange atomique du `PredictionEngine` sous verrou pour que l'ancien modèle continue de servir pendant l'entraînement).
- **Débounce UI :** Temporisation de `300 ms` (`CancellationTokenSource`) dans `AddExpense.razor` lors de la saisie de la description.

### 1.3 Corpus synthétique camerounais & Évaluation Holdout (`A11`, `docs/ML_EVALUATION.md`)
- `SmartBudgetML/Resources/Raw/seed_fr.csv` : **314 exemples synthétiques** couvrant le contexte camerounais et la rétrocompatibilité des tests historiques.
- `SmartBudgetML/Resources/Raw/holdout_fr.csv` : **176 exemples synthétiques inédits** strictement disjoints de `seed_fr.csv` (`0` chevauchement vérifié par test unitaire).
- Résultats mesurés sur le holdout (`176` exemples) et documentés dans `docs/ML_EVALUATION.md` :
  - **Règles seules :** Micro-Accuracy = **81,25 %**, Macro-F1 = **82,49 %**
  - **ML.NET seul :** Micro-Accuracy = **91,48 %**, Macro-F1 = **91,15 %**
  - **Pipeline Hybride 3 étages :** Micro-Accuracy = **99,43 %**, Macro-F1 = **99,43 %**
- Latence d'inférence sur 1 000 prédictions : moyenne = **0,044 ms**, p95 = **0,114 ms**.

### 1.4 Prévision budgétaire : Charges fixes, Revenus irréguliers & Fiabilité (`A19`)
- Ajout de `IsFixedOrRecurring` sur `Expense` et prise en charge des dépenses fixes connues/calendaires (`FixedCommittedSpend`) dans `BudgetForecastingService.ForecastPeriod` : le loyer mensuel ou l'écolage payé le 1er du mois n'est plus extrapolé linéairement sur les 30 jours restants.
- Ajout des modes `BudgetPeriodMode.Monthly`, `BudgetPeriodMode.Weekly` (semaine ISO de 7 jours pour revenus hebdomadaires/informels) et `BudgetPeriodMode.Envelope`.
- Ajout de l'indice de fiabilité `ForecastReliability` (mensuel/enveloppe : `Low` lorsque `DaysElapsed < 7`, `Medium` de 7 à 13 jours, `High` ≥ 14 jours ; hebdomadaire : `Low` lorsque `DaysElapsed < 3`, `High` ≥ 3 jours) et gestion complète des cas limites (année bissextile 29 février, budget nul, dépense future ignorée).

---

## 2. Vérification de la Porte P4 (Gate P4)

- **Recherche `GetAwaiter().GetResult()` dans `SmartBudgetML/` :** `0` occurrence.
- **Documentation d'évaluation :** `docs/ML_EVALUATION.md` généré avec tableaux comparatifs, matrice de confusion et benchmarks réels.
- **Suite de tests unitaires (`SmartBudgetML.Tests`, `net10.0`) :** **123 / 123 tests passants** (dont 30 nouveaux cas de test dans `Phase4MlHybridTests.cs`), **0 échec, 0 avertissement**.
