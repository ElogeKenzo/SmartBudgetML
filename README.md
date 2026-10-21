# SmartBudget ML v2.0 🇨🇲

**Application Mobile Local-First de Gestion Budgétaire, Tontines (Njangi) et Intelligence Artificielle Embarquée (.NET 10 LTS · SQLCipher 256-bit · ML.NET Hybride)**

[![Framework](https://img.shields.io/badge/.NET%20MAUI-10.0%20LTS-512BD4)](#)
[![Security](https://img.shields.io/badge/Database-SQLCipher%20256--bit-10B981)](#)
[![Auth](https://img.shields.io/badge/PIN-PBKDF2%20600k%20%2B%20BiometricPrompt-06B6D4)](#)
[![Status](https://img.shields.io/badge/Status-docs%2Fgenerated%2FSTATUS.md-success)](docs/generated/STATUS.md)

---

## 1. Présentation Générale
**SmartBudget ML v2.0** est une application mobile Android et Windows de gestion de budget personnel conçue pour les réalités économiques du **Cameroun et d'Afrique centrale (XAF / FCFA)**, fonctionnant **100 % hors-ligne** (aucune permission `INTERNET` ni `READ_SMS`).

> **État de vérification automatique :** Les comptes exacts de tests, le résultat des garde-fous et les métriques du modèle ML sont générés automatiquement par `./tools/verify.ps1` dans [`docs/generated/STATUS.md`](docs/generated/STATUS.md).

### Points Forts de la v2.0
- **Localisation Cameroun (FR/EN, Français par défaut) :** 17 catégories préconfigurées avec clés stables (`food_market`, `transport`, `housing_rent`, `utilities`, `telecom_data`, `healthcare`, `education`, `tontine`, `family_solidarity`, `events`, `church_donations`, `entertainment`, `savings`, `other`, `salary`, `business_hustle`, `transfers_received`), 155 clés de traduction bilingues (`AppStrings.fr.resx` / `AppStrings.en.resx`) et formatage natif en `FCFA`.
- **Pipeline ML.NET Hybride à 3 Étages (100 % sur appareil) :**
  1. **Étage 1 — Mémoire Utilisateur (`CategoryFeedback` SQLite) :** Priorité absolue aux corrections passées de l'utilisateur.
  2. **Étage 2 — Règles Mots-Clés Camerounais (`KeywordRuleCategorizer`) :** Expressions locales (*bendskin, moto, clando, ENEO, Camwater, MTN, Orange, Camtel, tontine, njangi, dîme, scolarité, marché Mokolo, Ndokoti, soya, ndolé...*).
  3. **Étage 3 — Modèle ML.NET SDCA (`SdcaMaximumEntropy`) :** Entraîné sur le corpus synthétique camerounais (`seed_fr.csv`) et évalué sur le jeu de validation disjoint (`holdout_fr.csv`). Voir les métriques et latences exactes dans [`docs/generated/STATUS.md`](docs/generated/STATUS.md) et [`docs/ML_EVALUATION.md`](docs/ML_EVALUATION.md).
- **Sécurité Cryptographique :**
  - Base SQLite chiffrée au repos via **SQLCipher 256 bits** (`sqlite-net-sqlcipher`) avec clé maître CSPRNG stockée via `ISecureKeyStore` et migration automatique non destructive des bases v1.0 en clair.
  - Code PIN à 6 chiffres haché via **PBKDF2-HMAC-SHA256 à 600 000 itérations** (recommandation OWASP), colonnes séparées `PinSalt` et `PinHash`, rejet des PIN triviaux, anti-bruteforce persistant avec backoff exponentiel et protection contre le recul de l'horloge système.
  - Biométrie native Android (`Android.Hardware.Biometrics.BiometricPrompt`), politique de verrouillage automatique en arrière-plan (`AutoLockPolicy`) et masquage dans le multitâche (`FLAG_SECURE`).
- **Tontines (Njangi) & Revenus Irréguliers :**
  - Calculateur de tontines (`TontineCalculator`) : pot par tour, calendrier des tours, détection des retards, gestion des retraits et solde net ($\text{Reçu} - \text{Cotisé}$).
  - Planificateur de revenus irréguliers (`IrregularIncomePlanner`) : moyenne glissante 3–6 mois, protection du minimum vital, découpage en enveloppes hebdomadaires et suivi du ratio d'aide familiale (`family_solidarity`).

---

## 2. Architecture Technique

```mermaid
flowchart TD
    UI["Couche UI : Blazor Hybrid (.razor) + IStringLocalizer FR/EN (155 clés)"]
    AUTH["Sécurité : LocalAuthService (PBKDF2 600k + AutoLock + BiometricPrompt)"]
    ML["Pipeline Hybride 3 Étages : UserMemory -> KeywordRuleCategorizer -> ML.NET SDCA"]
    FORECAST["Prévisions & Budget : BudgetForecastingService + TontineCalculator + IrregularIncomePlanner"]
    SMS["Import SMS Mobile Money : SmsParserRegistry (Orange Money CM + MTN MoMo CM, 0 permission SMS)"]
    DB["Persistance : DatabaseService (SQLCipher 256-bit + ISecureKeyStore + Backup VACUUM INTO)"]

    UI --> AUTH
    UI --> ML
    UI --> FORECAST
    UI --> SMS
    AUTH --> DB
    ML --> DB
    FORECAST --> DB
```

---

## 3. Prérequis et Commandes de Vérification / Build

### Prérequis
- **.NET 10.0 SDK** (`10.0.402` ou supérieur, configuré dans [`global.json`](global.json))
- Workload `.NET MAUI Android` (`dotnet workload install maui-android`) pour compiler l'APK/AAB Android.

### Exécuter la vérification complète et régénérer `docs/generated/STATUS.md`
```powershell
powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1
```

### Exécuter la suite de tests xUnit
```powershell
dotnet test SmartBudgetML.Tests/SmartBudgetML.Tests.csproj -c Release
```

### Générer l'APK et l'AAB Android Release
```powershell
dotnet publish SmartBudgetML/SmartBudgetML.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=apk -o ./artifacts/apk
dotnet publish SmartBudgetML/SmartBudgetML.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o ./artifacts/aab
```

---

## 4. Parseurs SMS Mobile Money (Orange Money CM / MTN MoMo CM)
Conformément à [`docs/DECISION_SMS.md`](docs/DECISION_SMS.md), SmartBudget ML ne demande **jamais** les permissions `READ_SMS` ou `RECEIVE_SMS`.  
Les deux parseurs opérateurs camerounais sont implémentés et vérifiés par tests unitaires sur des formats SMS anonymisés :
- [`OrangeMoneyCameroonParser.cs`](SmartBudgetML/Services/Sms/OrangeMoneyCameroonParser.cs) : transferts reçus/envoyés (`PP...` FR/EN), dépôts (`CI...`), paiements marchands et forfaits (`MP...`), frais décimaux et extraction d'horodatage.
- [`MtnMomoCameroonParser.cs`](SmartBudgetML/Services/Sms/MtnMomoCameroonParser.cs) : réceptions d'argent, achats de forfaits (`MTNC BUNDLES_FORFAITS`), paiements marchands, retraits d'espèces et filtrage des bannières promotionnelles.
- Tests associés : [`Phase6LocalValueTests.cs`](SmartBudgetML.Tests/LocalValue/Phase6LocalValueTests.cs) (avec identifiants et soldes fictifs anonymisés).

---

## 5. Documentation Complète du Projet
- [`docs/generated/STATUS.md`](docs/generated/STATUS.md) : État généré automatiquement (comptes de tests, garde-fous, métriques ML).
- [`RAPPORT_PROJET.md`](RAPPORT_PROJET.md) : Rapport d'ingénierie v2.0 et post-mortem technique.
- [`docs/AUDIT_REGISTER.md`](docs/AUDIT_REGISTER.md) : Registre de traçabilité des constats d'audit (`A1`–`A24` et `N1`–`N17`).
- [`docs/ML_EVALUATION.md`](docs/ML_EVALUATION.md) : Protocole et résultats d'évaluation du pipeline ML hybride.
- [`docs/DECISION_SMS.md`](docs/DECISION_SMS.md) : Justification de l'architecture d'import SMS sans permission sensible.
- [`docs/DEVICE_TEST_CHECKLIST.md`](docs/DEVICE_TEST_CHECKLIST.md) : Protocole de test sur smartphone physique (profil Redmi 3 Go de RAM).
- [`docs/play-store/`](docs/play-store/) : Dossier de publication Google Play (`PUBLICATION_GUIDE.md`, `PRIVACY_POLICY_FR.md`, `PRIVACY_POLICY_EN.md`, `DATA_SAFETY.md`, `STORE_LISTING.md`).
