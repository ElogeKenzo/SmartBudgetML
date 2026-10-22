# Passation

> Fichier de reprise entre deux sessions. Écrit par l'agent en fin de lot (ou quand l'utilisateur le demande), lu au début de la session suivante.

## 1. Objectif
Phase 8 : corriger les écarts relevés par l'audit du code `develop` (voir `docs/phase8/00-INDEX.md`).
- **Lot 8A terminé** : build réparé, vraies métriques ML, `./tools/verify.ps1` et `docs/generated/STATUS.md`, documents réalignés.
- **Lot 8B terminé** : CI réelle (5 jobs), auto-test `SB_SELFTEST` sur émulateur x86_64, runs verts notés dans `docs/CI_RUN_LOG.md`. `develop` et le tag `Phase8-8B` sont sur `origin`.
- **Lot 8C terminé** (`feature/phase8-c-securite`, tag `Phase8-8C`, poussé, CI verte notée dans `docs/CI_RUN_LOG.md`).
- **Lot 8D terminé** (`feature/phase8-d-export-backup`, fusionnée dans `develop` en local, tag `Phase8-8D`). **Rien n'est poussé** : la CI n'a pas encore tourné sur 8D.

## 2. Lot 8D : ce qui a été fait
1. **Partage** (`65a7b0f`) : `IFileExportService` gagne `ExportDirectory`, `PrepareExportPath` (ne garde que le dernier fichier exporté : données personnelles) et `ShareFileAsync`. `MauiFileExportService` (exclu du projet de tests) écrit dans `FileSystem.CacheDirectory/exports` puis appelle `Share.Default.RequestAsync(ShareFileRequest)`. Aucune permission. L'écran dit « Choisissez une application pour enregistrer le fichier », jamais « exporté ».
2. **Export CSV** : format inchangé (RFC 4180, BOM UTF-8), branché sur le partage. `ex.Message` retiré des deux gestionnaires concernés de `Settings.razor` (les autres restent pour 8H).
3. **Sauvegarde par mot de passe** : `DatabaseService.CreatePasswordBackupAsync` (`ATTACH DATABASE ? AS backup KEY ?` avec paramètres liés, donc aucun échappement à faire ; `sqlcipher_export` ; `PRAGMA backup.user_version = DatabaseConstants.SchemaVersion` (= 1) ; fichier `.tmp` validé puis renommé). `BackupPasswordPolicy` (8 caractères, confirmation), `BackupFileNames.ForDate` → `smartbudget-AAAAMMJJ.sbbackup`. Écran `/backup`. Le mot de passe n'est ni stocké ni journalisé, et il est effacé des champs après usage.
4. **Restauration** : `ValidatePasswordBackupAsync` (lecture seule, dans `Task.Run` car PBKDF2) → `Valid`, `Unreadable` (mauvais mot de passe ou fichier abîmé : SQLCipher ne sait pas les distinguer), `NotABackup` (intégrité ou tables manquantes), `TooNew` (`user_version` > `SchemaVersion`). `RestorePasswordBackupAsync` : nouvelle validation, import dans `.restore_tmp` rechiffré avec la clé du téléphone (contrôle du nombre de lignes par table), fermeture, copie `.pre-restore`, remplacement, réouverture et seeding ; en cas d'échec après remplacement, retour à `.pre-restore`. La purge efface aussi `.pre-restore` et `.restore_tmp`. Écran `/restore` : `FilePicker` (`ACTION_OPEN_DOCUMENT`, sans permission : vérifié dans le source MAUI), mot de passe, vérification, double confirmation (réutilise `VaultResetConfirmation`), puis `LockApp` et `/unlock` : le PIN valable est celui de la sauvegarde (dit à l'écran).
5. **Ancien chemin supprimé** : `BackupDatabaseAsync` / `RestoreDatabaseAsync` (`VACUUM INTO`, `smartbudget_backup_*.db3`) retirés de `IDatabaseService` et de `Settings.razor`. `Select-String "backup_" SmartBudgetML/Components` : vide.
6. **`/vault-error`** : phrase `VaultError_RestoreHint` (restaurer depuis Paramètres après la réinitialisation), désormais vraie.
7. **Docs** : README (et nombre de clés de traduction remplacé par un lien vers `STATUS.md`, il était déjà faux), politiques FR/EN, nouveau `docs/GUIDE_UTILISATEUR.md` (limité à l'export, la sauvegarde et la restauration), checklist appareil (point 11 revu, points 16 à 18 ajoutés).
8. **Auto-test** (`a583086`) : étapes `export_csv`, `password_backup`, `password_restore` (14 étapes au total, partage exclu).

Tests : `PasswordBackupTests` (13 : restauration croisée vers une autre clé maître, sauvegarde illisible avec la clé du téléphone, `user_version`, mauvais mot de passe, fichier corrompu, base étrangère, version trop récente, validation puis annulation avec SHA-256 inchangé, `.pre-restore`, purge, politique du mot de passe, nom de fichier, dossier d'export), `Phase1HonestyTests.DatabaseService_BackupAndRestore_*` réécrit.

Preuves de cette session : `./tools/verify.ps1` **PASS** (199/199 tests, build Android PASS, tous les garde-fous PASS) ; `dotnet format --verify-no-changes` OK sur les deux projets. Statuts : VÉRIFIÉ-TESTS, VÉRIFIÉ-BUILD. Pas encore VÉRIFIÉ-CI ni VÉRIFIÉ-ÉMULATEUR (non poussé). Téléphone : NON-VÉRIFIÉ-APPAREIL (points 13 à 18 de la checklist).

Limite connue : la mémoire des corrections ML (`ExpenseCategorizerService._userMemory`) n'est rechargée qu'au prochain démarrage de l'application après une restauration (même chose après une purge, comportement antérieur).

## 3. Fichiers importants
- `docs/phase8/00-INDEX.md` (lot suivant : 8E)
- `SmartBudgetML/Services/Database/DatabaseService.cs` (sauvegarde, restauration), `Services/Data/*FileExportService.cs`, `Components/Pages/Backup.razor`, `Restore.razor`, `Settings.razor`, `VaultError.razor`
- `tools/verify.ps1`, `docs/generated/STATUS.md`, `docs/AUDIT_REGISTER.md`, `docs/DEVICE_TEST_CHECKLIST.md`

## 4. Ce qui a été essayé et qui a raté
- Éditer des `.resx` ou des `.razor` par script PowerShell 5.1 : `[IO.File]::WriteAllText` avec `UTF8Encoding($true)` ajoute un BOM absent de l'original ; un `Where-Object` sur une seule ligne renvoie une chaîne, et `[0]` donne alors un caractère. Utiliser l'outil Edit.
- `Get-Content` sous PowerShell 5.1 affiche en mojibake les `.md` UTF-8 sans BOM (le fichier n'est pas abîmé). Lire avec Read.
- Pièges hérités (lots 8A et 8B) : `tools/verify.ps1` doit rester en ASCII pur ; encadrer les appels `dotnet` avec `$ErrorActionPreference = "Continue"`.

## 5. Prochaine étape
1. Demander à l'utilisateur l'autorisation de pousser `develop`, la branche `feature/phase8-d-export-backup` et le tag `Phase8-8D`, puis noter les runs CI dans `docs/CI_RUN_LOG.md` (attendu : `SB_SELFTEST|DONE|14/14`).
2. Lot suivant selon `docs/phase8/00-INDEX.md`.
3. Restent hors périmètre : `LocalAuthServiceTests` et `Phase5CameroonLocalizationTests` gardent le motif « connexion non fermée et `catch {}` vide » ; `Settings.razor` (chargement, enregistrement, réentraînement, purge) et `Profile.razor` affichent encore `ex.Message` (N10, lot 8H) ; avertissements Node.js 20 des actions `@v4`.

## Décisions prises
1. Pas de réécriture de l'historique Git pour retirer les anciens identifiants SMS (décision #3).
2. `docs/generated/STATUS.md` (généré par `./tools/verify.ps1`) est la source de vérité unique pour les comptes de tests et les métriques ML.
3. (8C) Aucune clé n'est jamais créée par-dessus une base chiffrée existante, et aucune base n'est réparée automatiquement : c'est l'utilisateur qui décide, depuis `/vault-error`.
4. (8D) La sauvegarde est chiffrée par un mot de passe choisi par l'utilisateur (décision par défaut n° 2), jamais stocké ; la restauration réinstalle le profil et le PIN de la sauvegarde.

## Commandes utiles
- Vérification complète :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1
  ```
- Vérification rapide (sans build Android) :
  ```powershell
  powershell -ExecutionPolicy Bypass -File ./tools/verify.ps1 -SkipAndroid
  ```
