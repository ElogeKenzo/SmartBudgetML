# Etat Genere du Depot - SmartBudget ML

> Fichier genere automatiquement par `./tools/verify.ps1`. Ne pas modifier a la main.

## 1. Metadonnees et Compilation
| Cle | Valeur |
|---|---|
| **Date de verification** | `2026-10-21 03:55:49 UTC` |
| **Branche / Commit** | `feature/phase8-a-verite` (`bc7bea4` / `bc7bea42d2765d876f341e14ca48f19773e0bf80`) |
| **Build Tests (net10.0)** | `PASS` |
| **Build Android (net10.0-android Release)** | `PASS` |
| **Cles de traduction (AppStrings)** | `155` FR / `155` EN |

## 2. Suite de Tests xUnit
| Total | Reussis | Echecs | Ignores | Statut |
|---:|---:|---:|---:|---|
| **139** | **139** | **0** | **0** | `PASS` |

## 3. Garde-fous d'Audit
| Garde-fou | Resultat | Detail |
|---|---|---|
| Aucun `catch {}` vide | `PASS` | 0 occurrence(s) |
| Aucun `GetAwaiter().GetResult()` | `PASS` | 0 occurrence(s) |
| Manifeste Android : `USE_BIOMETRIC` seul | `PASS` | Permissions : `android.permission.USE_BIOMETRIC` |
| Aucun `READ_SMS` / `RECEIVE_SMS` | `PASS` | 0 occurrence(s) |
| `git ls-files` sans keystore ni `.sb_vault_key` | `PASS` | 0 fichier(s) sensible(s) suivi(s) |

## 4. Metriques ML Reelles (issues de `artifacts/ml-eval.json`)
- **Echantillons :** `314` entrainement (`seed_fr.csv`) / `176` validation (`holdout_fr.csv`), `11` categories dans le jeu courant.
- **Latence d'inference (1 000 appels) :** Moyenne = `0.0722 ms`, p95 = `0.1495 ms`.
- **Temps d'entrainement :** `1046 ms`.
- **Taille du modele serialise :** `111183 octets` (`108.6 Ko`).

| Approche | Reussites / Total | Micro-Accuracy | Macro-Accuracy | Macro-F1 |
|---|---:|---:|---:|---:|
| **Regles seules (KeywordRuleCategorizer)** | 143 / 176 | 81.25 % | 81.25 % | 82.49 % |
| **ML.NET SDCA seul** | 161 / 176 | 91.48 % | 91.48 % | 91.15 % |
| **Pipeline Hybride (Memoire + Regles + ML)** | 175 / 176 | **99.43 %** | **99.43 %** | **99.43 %** |
