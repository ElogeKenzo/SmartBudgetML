# Évaluation Réelle du Pipeline ML Hybride — SmartBudget ML v2.0

**Exécution :** Hôte Windows x64 (`.NET 10.0`, `MLContext(seed: 42)`, `SdcaMaximumEntropy` déterministe `NumberOfThreads = 1`)  
**Source de vérité générée :** Voir [`docs/generated/STATUS.md`](generated/STATUS.md) (généré par `./tools/verify.ps1` à partir de `artifacts/ml-eval.json` et `artifacts/ml-eval-output.txt`).

---

## 1. Jeux de données synthétiques camerounais (`A11` — état courant avant Lot 8G `N7`)

Toutes les phrases d'entraînement et de validation sont **100 % synthétiques** (`# SYNTHÉTIQUE`), sans numéro de téléphone réel ni donnée personnelle :

| Jeu de données | Fichier | Nombre d'exemples | Catégories couvertes | Rôle |
|---|---|---|---|---|
| **Entraînement (`train`)** | `SmartBudgetML/Resources/Raw/seed_fr.csv` | **314** | 11 catégories historiques (`Entertainment`, `Food & Dining`, `Groceries`, `Healthcare`, `Housing & Rent`, `Investments`, `Other`, `Salary / Income`, `Shopping`, `Transportation`, `Utilities`) | Entraînement initial à froid du classifieur SDCA |
| **Validation (`holdout`)** | `SmartBudgetML/Resources/Raw/holdout_fr.csv` | **176** | 11 catégories historiques (16 exemples/catégorie) | Évaluation hors-échantillon (jamais vu à l'entraînement) |

> **Note d'honnêteté (`N7`) :** Les fichiers `seed_fr.csv` et `holdout_fr.csv` actuels utilisent encore les 11 noms de catégories historiques en anglais (mappés vers 8 des 17 clés camerounaises de `CategoryCatalog`). L'alignement complet sur les 17 catégories camerounaises (`Category.Key`) en FR et EN fait l'objet du **Lot 8G**.

### Contrôle anti-fuite (`train ∩ holdout == ∅`)
Le test unitaire `Phase4MlHybridTests.CameroonianDatasets_TrainAndHoldout_ShouldMeetSizeRequirements_AndHaveZeroLeakage` vérifie à chaque exécution qu'aucune phrase de `holdout_fr.csv` (après normalisation en minuscules) n'apparaît dans `seed_fr.csv` (`0 / 176`).

---

## 2. Comparaison quantitative sur le jeu Holdout (`176` transactions inédites)

Les métriques exactes sont produites par `ModelEvaluator.CompareApproachesAsync` lors du test `Phase4MlHybridTests.HoldoutEvaluation_CompareRulesMlAndHybrid_ShouldMeetNonRegressionThresholds` et exportées dans `artifacts/ml-eval-output.txt` et `artifacts/ml-eval.json` (reportées automatiquement dans [`docs/generated/STATUS.md`](generated/STATUS.md)) :

| Approche évaluée | Réussites / Total | Micro-Accuracy | Macro-Accuracy | Macro-F1 | Seuil de non-régression |
|---|---|---|---|---|---|
| **(a) Règles seules** (`KeywordRuleCategorizer` — Étage 2) | **143 / 176** | **81,25 %** (`0,812500`) | **81,25 %** (`0,812500`) | **82,49 %** (`0,824912`) | `>= 65,0 %` |
| **(b) ML.NET seul** (`SdcaMaximumEntropy` TF-IDF — Étage 3) | **161 / 176** | **91,48 %** (`0,914773`) | **91,48 %** (`0,914773`) | **91,15 %** (`0,911526`) | `>= 75,0 %` |
| **(c) Pipeline Hybride 3 étages** (Mémoire + Règles + ML.NET) | **175 / 176** | **99,43 %** (`0,994318`) | **99,43 %** (`0,994318`) | **99,43 %** (`0,994313`) | `>= 85,0 %` |

### Matrice de confusion réelle du Pipeline Hybride sur le Holdout (176 exemples, issue de `artifacts/ml-eval-output.txt`)

| Catégorie réelle \ prédite | Ent. | Food | Groc. | Health | Hous. | Inv. | Other | Sal. | Shop. | Trans. | Util. | Rappel |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Entertainment** (16) | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Food & Dining** (16) | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Groceries** (16) | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Healthcare** (16) | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Housing & Rent** (16) | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Investments** (16) | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 0 | 100,0 % |
| **Other** (16) | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 0 | 0 | 100,0 % |
| **Salary / Income** (16) | 0 | 0 | 1 | 0 | 0 | 0 | 0 | **15** | 0 | 0 | 0 | 93,8 % |
| **Shopping** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 0 | 100,0 % |
| **Transportation** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 0 | 100,0 % |
| **Utilities** (16) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **16** | 100,0 % |

---

## 3. Performances et taille du modèle

Les mesures de latence d'inférence (moyenne et p95 sur 1 000 appels), le temps d'entraînement et la taille exacte du modèle sérialisé (`111 197 octets`, soit `108,6 Ko`) sont enregistrés à chaque exécution dans `artifacts/ml-eval.json` et consultables dans [`docs/generated/STATUS.md`](generated/STATUS.md).

### Garanties d'architecture non-bloquante (`A10`)
- Aucun appel `TrainAsync().GetAwaiter().GetResult()` dans `Predict()`.
- Si `Predict()` est appelé avant la fin de l'initialisation en arrière-plan, il exécute immédiatement l'Étage 1 (`UserMemory`) et l'Étage 2 (`KeywordRuleCategorizer`).
- Le réentraînement complet est regroupé par lots (`RetrainBatchThreshold = 15`).
- Saisie utilisateur temporisée par un debounce de 300 ms dans `AddExpense.razor`.

---

## 4. Prévision budgétaire (`BudgetForecastingService` — `A13`, `A19`)

- **Séparation Dépenses Fixes vs. Dépenses Variables :** Les charges fixes calendaires (`IsFixedOrRecurring = true`) sont comptabilisées à leur montant réel sur la période au lieu d'être extrapolées linéairement jour après jour.
- **Modes de période (`BudgetPeriodMode`) :** `Monthly`, `Weekly` (semaine ISO de 7 jours), `Envelope`.
- **Indice de fiabilité (`ForecastReliability`) :**
  - En mode mensuel/enveloppe : `Low` lorsque `DaysElapsed < 7`, `Medium` entre 7 et 13 jours écoulés, `High` à partir de 14 jours écoulés.
  - En mode hebdomadaire : `Low` lorsque `DaysElapsed < 3`, `High` à partir de 3 jours écoulés.
