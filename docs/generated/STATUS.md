# Etat Genere du Depot - SmartBudget ML

> Fichier genere automatiquement par `./tools/verify.ps1`. Ne pas modifier a la main.

## 1. Metadonnees et Compilation
| Cle | Valeur |
|---|---|
| **Date de verification** | `2026-10-22 04:41:29 UTC` |
| **Branche / Commit** | `feature/phase8-d-export-backup` (`3867d6a` / `3867d6aac73cffce7fd5898db182fe384a64bee2`) |
| **Build Tests (net10.0)** | `PASS` |
| **Build Android (net10.0-android Release)** | `PASS` |
| **Cles de traduction (AppStrings)** | `206` FR / `206` EN |

## 2. Suite de Tests xUnit
| Total | Reussis | Echecs | Ignores | Statut |
|---:|---:|---:|---:|---|
| **199** | **199** | **0** | **0** | `PASS` |

## 3. Garde-fous d'Audit
| Garde-fou | Resultat | Detail |
|---|---|---|
| Aucun `catch {}` vide | `PASS` | 0 occurrence(s) |
| Aucun `GetAwaiter().GetResult()` | `PASS` | 0 occurrence(s) |
| Aucun repli de cle en clair `sb_vault_key` dans `SmartBudgetML/` | `PASS` | 0 occurrence(s) |
| Manifeste Android : `USE_BIOMETRIC` seul | `PASS` | Permissions : `android.permission.USE_BIOMETRIC` |
| Aucun `READ_SMS` / `RECEIVE_SMS` | `PASS` | 0 occurrence(s) |
| `git ls-files` sans keystore ni `.sb_vault_key` | `PASS` | 0 fichier(s) sensible(s) suivi(s) |
| Build Release Android sans code `SB_SELFTEST` | `PASS` | `tools/check_selftest_absent.py` sur la DLL IL |
| Bibliotheques natives alignees sur 16 Ko | `PASS` | `tools/check_elf_alignment.py` sur l'APK |

## 4. Metriques ML Reelles (issues de `artifacts/ml-eval.json`)
- **Echantillons :** `314` entrainement (`seed_fr.csv`) / `176` validation (`holdout_fr.csv`), `11` categories dans le jeu courant.
- **Latence d'inference (1 000 appels) :** Moyenne = `0.2897 ms`, p95 = `0.1232 ms`.
- **Temps d'entrainement :** `1919 ms`.
- **Taille du modele serialise :** `111202 octets` (`108.6 Ko`).

| Approche | Reussites / Total | Micro-Accuracy | Macro-Accuracy | Macro-F1 |
|---|---:|---:|---:|---:|
| **Regles seules (KeywordRuleCategorizer)** | 143 / 176 | 81.25 % | 81.25 % | 82.49 % |
| **ML.NET SDCA seul** | 161 / 176 | 91.48 % | 91.48 % | 91.15 % |
| **Pipeline Hybride (Memoire + Regles + ML)** | 175 / 176 | **99.43 %** | **99.43 %** | **99.43 %** |
