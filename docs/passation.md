# Passation

> Fichier de reprise entre deux sessions. Écrit par l'agent en fin de lot (ou quand l'utilisateur le demande), lu au début de la session suivante.

## 1. Objectif
Phase 8 : corriger les écarts relevés par l'audit du code `develop` (voir `docs/phase8/00-INDEX.md`).
- **Lot 8A terminé** : build réparé, vraies métriques ML, `./tools/verify.ps1` et `docs/generated/STATUS.md`, documents réalignés.
- **Lot 8B terminé** : CI réelle (5 jobs), auto-test `SB_SELFTEST` sur émulateur x86_64, runs verts notés dans `docs/CI_RUN_LOG.md`. `develop` et le tag `Phase8-8B` sont sur `origin`.
- **Lot 8C terminé** (`feature/phase8-c-securite`, fusionnée dans `develop`, tag `Phase8-8C`). Branche, `develop` et tag sont sur `origin` ; runs CI verts notés dans `docs/CI_RUN_LOG.md` (`SB_SELFTEST|DONE|11/11` sur l'émulateur).

## 2. Lot 8C : ce qui a été fait (un commit par étape)
1. **N3, verrouillage visible** (`b227c51`, `a04a9fd`) : événement `ILocalAuthService.LockStateChanged` (déclenché par `LockApp`, donc aussi par l'auto-verrouillage au retour). `MainLayout` s'abonne, se désabonne dans `Dispose` et navigue vers `/unlock` (`replace: true`) via `LockNavigationCoordinator.GetRedirect` (classe pure : pas de redirection si déjà sur `unlock`, `onboarding` ou `vault-error`). La même vérification tourne à chaque changement d'adresse, une fois l'utilisateur inscrit : le bouton Retour ne peut plus montrer une page pendant le verrouillage. Tests `LockStateTests` (9). Le branchement dans `MainLayout` n'est pas testé automatiquement (pas de bUnit) : point 14 de `docs/DEVICE_TEST_CHECKLIST.md`.
2. **N8, PBKDF2 hors du fil UI** (`9b46628`) : `HashPinAsync` (`Task.Run`) pour la pose, la vérification et la montée v1→v2. Clavier désactivé et « Vérification… » (`Unlock_Verifying`) pendant le calcul. Test `PinHashingThreadTests` : un contexte de synchronisation à fil unique sert de faux fil UI. Avant le correctif, il était bloqué 1 186 ms (un hachage = 1 228 ms sur le PC de dev) ; après, 6 ms. Itérations inchangées. Temps sur Redmi : point 13 de la checklist.
3. **N6, plus de repli en clair** (`a0cee85`) : `ISecureKeyStore` = `GetMasterKeyAsync` (null si absente, exception si le stockage échoue) + `CreateMasterKeyAsync`. `MauiSecureKeyStore` n'écrit plus jamais `.sb_vault_key`. `DatabaseService.VaultState` : `KeyUnavailable` (stockage en échec, rien n'est écrit ailleurs et aucune base n'est créée), `KeyLost` (base chiffrée sans clé ou avec une mauvaise clé : fichier intact, aucune clé créée), `Ready`. `InitializeAsync` lève alors `VaultUnavailableException`. `MainLayout` envoie vers `/vault-error` (FR/EN), avec Réessayer et la réinitialisation à double confirmation (`VaultResetConfirmation`). Tests `VaultStateTests` (a, b, c, plus mauvaise clé, migration et nouvel essai) et `VaultResetConfirmationTests`.
   - **Volontairement absent** : la phrase « une sauvegarde peut être restaurée » n'est pas affichée sur `/vault-error`, car la restauration n'a pas encore d'écran (N5, règle 4). **À ajouter au lot 8D** quand la restauration sera atteignable.
4. **Migration claire → chiffrée** (`9db5615`) : le test avec identifiants à trous a échoué avant le correctif (la catégorie 4 devenait 2, et les tables de tontines étaient perdues). La copie passe maintenant par `ATTACH DATABASE … KEY …; SELECT sqlcipher_export('encrypted'); DETACH`, accepté par `SQLitePCLRaw.bundle_e_sqlcipher 2.1.11`, avec un contrôle du nombre de lignes de chaque table. Note : le seeding remet ensuite les catégories par défaut supprimées, avec de nouveaux identifiants ; c'est le comportement existant de `MigrateLegacyCategoriesAsync`.
5. **N16, biométrie** (`92223aa`) : `BiometricAvailabilityPolicy` (faux sous API 28, sans capteur, sans empreinte ; `BiometricManager` dès API 29, `FingerprintManager` en API 28 : si un appel lève une exception, la biométrie reste cachée). Bouton du verrouillage, interrupteur et test du Profil, interrupteur de l'onboarding masqués sinon. NON-VÉRIFIÉ-APPAREIL (point 15 de la checklist).
6. **Textes Play Store** (`1831251`) : politique FR/EN et Data Safety disent « protégée par le Keystore Android lorsque l'appareil le permet ».
7. **Auto-test** (`8801341`) : étapes `reopen_encrypted_db` et `lock_app` (11 étapes au total, auparavant 9). Nouveau garde-fou dans `verify.ps1` : aucun `sb_vault_key` dans `SmartBudgetML/`. Le seul `sb_vault_key` restant dans le code est dans `VaultStateTests`, qui vérifie l'absence du fichier.

Preuves de cette session : `./tools/verify.ps1` **PASS** sur `a04a9fd` (183/183 tests, build Android PASS avec les 2 mêmes CS1570, tous les garde-fous PASS) ; `dotnet format --verify-no-changes` OK sur les deux projets. Statuts : VÉRIFIÉ-TESTS, VÉRIFIÉ-BUILD ; puis VÉRIFIÉ-CI et VÉRIFIÉ-ÉMULATEUR pour ce que couvre l'auto-test (runs 39718171107 et 39718171086, `SB_SELFTEST|DONE|11/11`) ; téléphone : NON-VÉRIFIÉ-APPAREIL.

## 3. Fichiers importants
- `docs/phase8/00-INDEX.md` et `docs/phase8/8D-export-backup-restore.md` (lot suivant)
- `SmartBudgetML/Services/Database/DatabaseService.cs` (état du coffre, migration), `Services/Security/*`, `Components/Pages/VaultError.razor`
- `tools/verify.ps1`, `docs/generated/STATUS.md`, `docs/AUDIT_REGISTER.md`, `docs/DEVICE_TEST_CHECKLIST.md` (points 13 à 15 ajoutés)

## 4. Ce qui a été essayé et qui a raté
- Éditer des `.resx` ou des `.razor` par script PowerShell 5.1 : `[IO.File]::WriteAllText` avec `UTF8Encoding($true)` ajoute un BOM absent de l'original ; un `Where-Object` sur une seule ligne renvoie une chaîne, et `[0]` donne alors un caractère. Utiliser l'outil Edit.
- `Get-Content` sous PowerShell 5.1 affiche en mojibake les `.md` UTF-8 sans BOM (le fichier n'est pas abîmé). Lire avec Read.
- Pièges hérités (lots 8A et 8B) : `tools/verify.ps1` doit rester en ASCII pur ; encadrer les appels `dotnet` avec `$ErrorActionPreference = "Continue"`.

## 5. Prochaine étape
1. Lot 8D (`docs/phase8/8D-export-backup-restore.md`). Il doit ajouter sur `/vault-error` le rappel de la restauration d'une sauvegarde.
2. Restent hors périmètre : `LocalAuthServiceTests` et `Phase5CameroonLocalizationTests` gardent le motif « connexion non fermée et `catch {}` vide » ; `Settings.razor` et `Profile.razor` affichent encore `ex.Message` (N10, lot 8H) ; avertissements Node.js 20 des actions `@v4`.

## Décisions prises
1. Pas de réécriture de l'historique Git pour retirer les anciens identifiants SMS (décision #3).
2. `docs/generated/STATUS.md` (généré par `./tools/verify.ps1`) est la source de vérité unique pour les comptes de tests et les métriques ML.
3. (8C) Aucune clé n'est jamais créée par-dessus une base chiffrée existante, et aucune base n'est réparée automatiquement : c'est l'utilisateur qui décide, depuis `/vault-error`.

## Commandes utiles
- Vérification complète :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1
  ```
- Vérification rapide (sans build Android) :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1 -SkipAndroid
  ```
