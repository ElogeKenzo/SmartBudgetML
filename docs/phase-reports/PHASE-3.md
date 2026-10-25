# Rapport de Phase 3 — Sécurité réelle (`feature/security-hardening`)

**Date :** 21 octobre 2026  
**Branche :** `feature/security-hardening` → fusionnée dans `develop` (`--no-ff`), tag `Phase-3`  
**Objectif :** Mettre en œuvre le chiffrement SQLCipher 256 bits au repos (`A1`), la limitation persistée des tentatives de PIN avec délai exponentiel et PBKDF2 à 600 000 itérations (`A9`), le verrouillage automatique lié au cycle de vie (`A8`), et l'hygiène de sécurité Android (`A23`).

---

## 1. Travaux réalisés

### 1.1 Chiffrement SQLCipher au repos et migration sans perte (A1)
- Remplacement de `sqlite-net-pcl` + `SQLitePCLRaw.bundle_green` par `sqlite-net-sqlcipher 1.9.172` (ce qui élimine également la vulnérabilité `NU1903` de `e_sqlite3`).
- Suppression de `SQLiteOpenFlags.SharedCache` dans `DatabaseConstants.Flags` afin qu'aucune connexion ne puisse partager un cache déchiffré sans présenter la clé SQLCipher.
- Création de `ISecureKeyStore` (`InMemorySecureKeyStore` pour les tests xUnit sur l'hôte ; `MauiSecureKeyStore` basé sur `Microsoft.Maui.Storage.SecureStorage` / Android Keystore sur appareil, étiqueté `NON-VÉRIFIÉ-APPAREIL`).
- Clé maîtresse de 32 octets (256 bits) générée via `RandomNumberGenerator.GetBytes(32)` — jamais dérivée du PIN ni stockée en clair dans la base.
- **Migration automatique des bases existantes :** Lorsqu'une base SQLite non chiffrée (`"SQLite format 3\0"`) est détectée alors qu'un `ISecureKeyStore` est fourni, `DatabaseService` crée une sauvegarde `.bak`, migre l'intégralité des tables vers SQLCipher, vérifie que le nombre de lignes avant/après est strictement identique à 100 %, et remplace atomiquement le fichier.
- **Purge sécurisée à 2 étapes :** `PurgeDatabaseAndKeyAsync()` supprime les fichiers `.db3`, `-wal`, `-shm`, `.bak` ainsi que la clé dans `ISecureKeyStore`, avec confirmation à 2 étapes dans `Settings.razor`.

### 1.2 Limitation des tentatives de PIN, délai exponentiel et PBKDF2 600 000 itérations (A9)
- Persistance dans `AppSettings` de `FailedPinAttempts`, `LockoutUntilUtc`, `LastFailedAttemptUtc`, `PinHashVersion` et `PinLength`.
- **Délai exponentiel (backoff) :**
  - 1 à 4 échecs : aucun délai
  - 5 échecs : 30 secondes
  - 6 échecs : 1 minute
  - 7 échecs : 5 minutes
  - 8+ échecs : 15 minutes
- **Protection contre le recul de l'horloge :** Si l'horloge système recule en deçà de `LastFailedAttemptUtc`, le verrouillage est maintenu.
- **Complexité et longueur du PIN :** PIN à 6 chiffres par défaut pour les nouveaux comptes (`Onboarding.razor`), rétrocompatibilité 4 chiffres, et rejet des PINs triviaux (`0000`, `1111`, `1234`, `123456`, `654321`, chiffres identiques ou suites consécutives) via `ValidatePinComplexity`.
- **Mise à niveau PBKDF2 (`v1` → `v2`) :** Passage de `10 000` à `600 000` itérations (`HashAlgorithmName.SHA256`). Lorsqu'un utilisateur disposant d'un hash `v1` saisit son PIN valide, son hash est immédiatement recalculé et persisté en `v2` de manière transparente.

### 1.3 Verrouillage automatique et protection d'écran (A8, A23)
- Création de `AutoLockPolicy.ShouldLock(DateTime? lastBackgroundUtc, DateTime nowUtc, int timeoutSeconds)` (pur et déterministe) et intégration dans `LocalAuthService.EvaluateAutoLockOnResumeAsync` et `App.xaml.cs` (`Window.Deactivated`, `Window.Stopped`, `Window.Resumed`).
- Activation par défaut de `WindowManagerFlags.Secure` (`FLAG_SECURE`) dans `Platforms/Android/MainActivity.cs` (`NON-VÉRIFIÉ-APPAREIL`) avec option dans `Settings.razor`.
- Mise à jour de `Platforms/Android/AndroidManifest.xml` : `android:allowBackup="false"` et suppression des permissions inutiles `INTERNET` et `ACCESS_NETWORK_STATE`.

---

## 2. Vérification du Gate P3

- Cycle TDD respecté : les 24 cas de test de `Phase3SecurityTests` ont d'abord échoué (`24 échecs / 24`), puis sont tous passés au vert après implémentation.
- Total de la suite de tests : **93 / 93 tests passants** (`0` échec, `0` avertissement).
- Éléments matériels Android (`MauiSecureKeyStore` via Keystore matériel, `FLAG_SECURE` sur l'aperçu des tâches récentes) étiquetés `NON-VÉRIFIÉ-APPAREIL`.
