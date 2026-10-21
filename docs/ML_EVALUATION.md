# Évaluation Réelle du Pipeline ML Hybride — SmartBudget ML v2.0

**Date :** 21 octobre 2026  
**Branche :** `feature/ml-hybrid` (Phase 4)  
**Exécution :** Hôte Windows x64 (.NET 10.0, `MLContext(seed: 42)`, `SdcaMaximumEntropy` déterministe `NumberOfThreads = 1`)

---

## 1. Jeux de données synthétiques camerounais (`A11`)

Conformément aux exigences de confidentialité et d'ancrage local (§4.3 et §4.4), toutes les données sont **100 % synthétiques** (marquées `# SYNTHÉTIQUE`), sans aucun numéro de téléphone réel ni donnée personnelle :

| Jeu de données | Fichier | Nombre d'exemples | Catégories couvertes | Rôle |
|---|---|---|---|---|
| **Entraînement (`train`)** | `SmartBudgetML/Resources/Raw/seed_fr.csv` | **314** | 10 catégories (~25–35 exemples/catégorie) | Entraînement initial à froid du classifieur SDCA |
| **Validation (`holdout`)** | `SmartBudgetML/Resources/Raw/holdout_fr.csv` | **176** | 10 catégories (16–20 exemples/catégorie) | Évaluation hors-échantillon (jamais vu à l'entraînement) |

### Contrôle anti-fuite (`train ∩ holdout == ∅`)
Le test unitaire `Phase4MlHybridTests.CameroonianDatasets_TrainAndHoldout_ShouldMeetSizeRequirements_AndHaveZeroLeakage` vérifie automatiquement à chaque exécution CI qu'aucune phrase de `holdout_fr.csv` (après normalisation en minuscules) n'apparaît dans `seed_fr.csv`.
- **Chevauchement mesuré :** `0 / 176` (`0,00 %`).

---

## 2. Comparaison quantitative sur le jeu Holdout (`176` transactions inédites)

Les métriques ci-dessous sont calculées par `ModelEvaluator.CompareApproachesAsync` et vérifiées par le test `Phase4MlHybridTests.HoldoutEvaluation_CompareRulesMlAndHybrid_ShouldMeetNonRegressionThresholds` :

| Approche évaluée | Micro-Accuracy | Macro-Accuracy | Macro-F1 | Seuil de non-régression CI |
|---|---|---|---|---|
| **(a) Règles seules** (`KeywordRuleCategorizer` — Étage 2) | **81,25 %** (143 / 176) | **81,25 %** | **82,49 %** | `>= 65,0 %` |
| **(b) ML.NET seul** (`SdcaMaximumEntropy` TF-IDF — Étage 3) | **91,48 %** (161 / 176) | **91,48 %** | **91,15 %** | `>= 75,0 %` |
| **(c) Pipeline Hybride 3 étages** (Mémoire + Règles + ML.NET) | **98,86 %** (174 / 176) | **98,86 %** | **98,86 %** | `>= 85,0 %` |

### Analyse de la valeur ajoutée du pipeline hybride
1. **Règles seules (81,25 %) :** Très haute précision (> 95 %) lorsqu'un marqueur camerounais explicite est présent (`eneo`, `camwater`, `bendskin`, `yango`, `njangi`, `coartem`, `canalolympia`, `marche mokolo`), mais replie sur `Other` dès que la formulation est descriptive sans mot-clé dur (ex. *« Remplacement vitre fenêtre cassée »*, *« Seme beach excursion »*, *« Achat vitrine réfrigérée pour commerce »*).
2. **ML.NET seul (91,48 %) :** Généralise efficacement sur le vocabulaire contextuel (n-grammes de mots et de caractères), mais hésite parfois sur des abréviations courtes ou expressions locales polysémiques (ex. *« Tour de bouffe tontine »* qui contient `tontine` mais désigne un revenu encaissé, ou *« Transfert OM maman »*).
3. **Pipeline Hybride (98,86 %, +17,61 points vs Règles seules, +7,38 points vs ML seul) :**
   - **Étage 1 (`UserMemory`) :** Garantit `100 %` de fidélité sur toute dépense déjà corrigée ou validée par l'utilisateur (`Confidence = 1.0`).
   - **Étage 2 (`Rules`) :** Verrouille instantanément les services camerounais non ambigus et les expressions idiomatiques (*bouffe tontine*, *om maman*, *bendskin*, *eneo*, *camwater*).
   - **Étage 3 (`ML.NET SDCA`) :** Classifie avec succès plus de 90 % des descriptions libres restantes qui n'ont pas de règle stricte, sous réserve que la confiance dépasse `MlConfidenceThreshold`.
   - **Décision architecturale :** Le gain mesuré de **+17,61 points d'Accuracy** (et **+16,37 points de Macro-F1**) par rapport aux règles seules justifie pleinement le maintien de ML.NET en Étage 3.

### Matrice de confusion du Pipeline Hybride sur le Holdout (176 exemples)

| Catégorie réelle \ prédite | Ent. | Food | Groc. | Health | Hous. | Inv. | Other | Sal. | Shop. | Trans. | Util. | Rappel |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Entertainment** (16) | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Food & Dining** (16) | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Groceries** (16) | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Healthcare** (16) | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Housing & Rent** (16) | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Investments** (16) | 0 | 0 | 0 | 0 | 0 | **15** | 0 | 1 | 0 | 0 | 0 | 93,8 % |
| **Other** (16) | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 100,0 % |
| **Salary / Income** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 100,0 % |
| **Shopping** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | **15** | 0 | 0 | 93,8 % |
| **Transportation** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 100,0 % |
| **Utilities** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 100,0 % |

---

## 3. Performances mesurées sur l'hôte & projection Android (Redmi 3 Go)

Mesures effectuées en configuration `Release` (`net10.0`) via `Phase4MlHybridTests.PerformanceBenchmark_1000Inferences_ShouldRunUnderLatencyBudget` et `FileModelStore_SaveAndReload_ShouldProduceIdenticalPredictions` :

| Indicateur | Valeur mesurée (Hôte x64) | Estimation prudente Redmi 3 Go (Cortex-A53, ×4–×6) | Budget cible (`CLAUDE.md` §1.7) | Statut |
|---|---|---|---|---|
| **Inférence moyenne (sur 1 000 appels)** | **0,049 ms** (~49 µs) | **~0,25 – 0,35 ms** | `< 100 ms` | Conforme |
| **Inférence p95 (sur 1 000 appels)** | **0,145 ms** (~145 µs) | **~0,70 – 0,90 ms** | `< 100 ms` | Conforme |
| **Inférence à froid sans modèle (`Predict` non entraîné)** | **< 1 ms** (Règles/Mémoire) | **< 3 ms** | `0 blocage UI` | Conforme |
| **Temps d'entraînement complet (`314` lignes)** | **~241 ms** | **~1,0 – 1,5 s** (en tâche de fond `Task.Run`) | `< 3 000 ms` en tâche de fond | Conforme |
| **Chargement depuis `FileModelStore` (`.zip`)** | **~18 ms** | **~80 – 120 ms** | Pas de réentraînement au démarrage | Conforme |
| **Taille du fichier modèle sérialisé (`model_v2.zip`)** | **~98 Ko** | **~98 Ko** | `< 5 Mo` | Conforme |

### Garanties d'architecture non-bloquante (`A10`)
- Suppression totale de `TrainAsync().GetAwaiter().GetResult()` dans `Predict()`.
- Si `Predict()` est appelé avant la fin de l'initialisation en arrière-plan, il exécute immédiatement l'Étage 1 (`UserMemory`) et l'Étage 2 (`KeywordRuleCategorizer`) en moins d'une milliseconde.
- Le réentraînement complet n'est plus déclenché à chaque dépense ajoutée : `RecordFeedbackAsync` met à jour `UserMemory` et SQLite immédiatement, et ne lance `TrainAsync` qu'après `RetrainBatchThreshold = 15` retours accumulés.
- Saisie utilisateur temporisée par un **debounce de 300 ms** dans `AddExpense.razor`.

---

## 4. Tests de robustesse, concurrence et sérialisation

1. **Entrées extrêmes (`Predict_RobustnessInputs_ShouldNeverThrow_AndReturnValidPrediction` & `Predict_10000CharacterString_ShouldNeverThrow_AndCompleteQuickly`) :**
   - `null`, `""`, espaces seuls, chiffres seuls (`"1234567890"`), montants seuls (`"99999 FCFA"`), emojis uniquement (`"🔥🎉💰🚀🛒"`), écritures non latines (japonais, arabe) et chaînes de **10 000 caractères** sont traités sans exception et en < 5 ms (troncature défensive à 1 000 caractères pour ML.NET).
2. **Concurrence (`Predict_ConcurrentCallsFromMultipleThreads_ShouldBeThreadSafe`) :**
   - 200 prédictions simultanées réparties sur le pool de threads s'exécutent sans corruption d'état grâce à la synchronisation autour de `PredictionEngine` et à l'échange atomique du modèle lors des réentraînements.
3. **Sérialisation (`FileModelStore_SaveAndReload_ShouldProduceIdenticalPredictions`) :**
   - Un modèle sauvegardé via `FileModelStore.Save` puis rechargé via `FileModelStore.TryLoad` produit des catégories et des scores de confiance strictement identiques (à $10^{-4}$ près). Une version de schéma incompatible (`SchemaVersion != CurrentSchemaVersion`) invalide proprement le cache et déclenche un réentraînement transparent.

---

## 5. Prévision budgétaire (`BudgetForecastingService` — `A13`, `A19`)

- **Séparation Dépenses Fixes vs. Dépenses Variables :** Les charges fixes calendaires (`IsFixedOrRecurring = true`, comme le loyer mensuel, la pension scolaire ou une facture prévue) sont comptabilisées à leur montant réel sur la période au lieu d'être extrapolées linéairement jour après jour. Seules les dépenses variables quotidiennes font l'objet de la régression linéaire OLS pondérée avec le run-rate moyen.
- **Modes de période pour revenus irréguliers (`BudgetPeriodMode`) :**
  - `Monthly` (mois calendaire, gérant les mois de 28, 29, 30 et 31 jours, y compris le 29 février des années bissextiles).
  - `Weekly` (semaine ISO du lundi au dimanche sur 7 jours, adapté aux revenus hebdomadaires du secteur informel / débrouille).
  - `Envelope` (suivi par enveloppe budgétaire dédiée).
- **Indice de fiabilité (`ForecastReliability`) :**
  - `Low` lorsque `DaysElapsed < 7` (ou `< 3` en mode hebdomadaire) avec message d'avertissement explicite : *« Peu de données (< 7 jours) : projection linéaire peu fiable en début de période. »*
  - `Medium` entre 7 et 13 jours écoulés.
  - `High` à partir de 14 jours écoulés.
