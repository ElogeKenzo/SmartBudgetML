# Passation

> Fichier de reprise entre deux sessions. Écrit par l'agent en fin de lot (ou quand l'utilisateur le demande), lu au début de la session suivante.

## 1. Objectif
Phase 8 : corriger les écarts relevés par l'audit du code `develop` (voir `docs/phase8/00-INDEX.md`).
- **Lot 8A terminé** : build réparé, vraies métriques ML, `./tools/verify.ps1` et `docs/generated/STATUS.md`, documents réalignés.
- **Lot 8B terminé** : CI réelle (5 jobs), auto-test `SB_SELFTEST` sur émulateur x86_64, runs verts notés dans `docs/CI_RUN_LOG.md`. `develop` et le tag `Phase8-8B` sont sur `origin`.
- **Lot 8C terminé** (`feature/phase8-c-securite`, tag `Phase8-8C`, poussé, CI verte notée dans `docs/CI_RUN_LOG.md`).
- **Lot 8D terminé** (`feature/phase8-d-export-backup`, fusionnée dans `develop`, tag `Phase8-8D`). Branche, `develop` et tag sont sur `origin` ; runs CI verts notés dans `docs/CI_RUN_LOG.md` (`SB_SELFTEST|DONE|14/14` sur l'émulateur).

- **Lot 8E terminé** (`feature/phase8-e-tontines`, fusionnée dans `develop` en local, tag `Phase8-8E`). **Pas encore poussé** : CI non exécutée pour ce lot.

## 2bis. Lot 8E : ce qui a été fait
1. **Modèles** : `Expense` gagne `IsExceptional`, `SourceType`, `SourceId` (index unique `UX_Expenses_Source`, constantes `ExpenseSources`). Index uniques `UX_TontineContributions_Member` (tontine, tour, membre) et `UX_TontineRounds_Number`. Migration par `CreateTableAsync` (ajout de colonnes, sans perte : test `ExistingDatabase_WithoutSourceColumns_IsMigratedWithoutLoss`). `SchemaVersion` inchangé (1) : l'ancien code lit une base 8E en ignorant les colonnes nouvelles.
2. **`ITontineService` / `TontineService`** (`Services/Tontines/`) : créer (`TontineDraftValidator` : nom, montant > 0 et entier, au moins 2 membres, tours uniques et compris entre 1 et N, une ligne « Moi »), lire, modifier (nom et montant des tours non versés), supprimer (les dépenses déjà passées restent), membre qui quitte (tours non versés reconstruits), `RecordContributionAsync` idempotent (dépense `tontine` liée seulement pour l'utilisateur), `RecordPayoutAsync` (revenu `transfers_received`, `IsExceptional = true`, seulement si l'utilisateur est le bénéficiaire ; la tontine passe inactive au dernier tour), `GetOverviewAsync` (échéances à 7 jours et retards). Écritures dans `RunInTransactionAsync`.
3. **Écrans** : `/tontines` (liste ou invitation à créer), `/tontines/new`, `/tontines/{id}` (tour en cours, bénéficiaire, qui a payé, retards, mes cotisations, reçu, solde net, « J'ai cotisé », « J'ai reçu le pot » ou « Pot versé à … », membre qui quitte et suppression en deux appuis, modification). L'ancienne maquette (Paul, Chantal…) et la zone SMS sont retirées de `Tontines.razor` (l'import SMS revient en 8F ; les clés `Tontines_Sms*` sont gardées pour lui).
4. **Accès** : carte « Mes tontines » sur l'accueil (nombre en cours, prochaine cotisation), lien dans Profil. Garde `PageReachabilityTests.AllPagesAreReachable` (a échoué sur `/tontines` avant les liens, vérifié).
5. **Rappels (N13)** : `ILocalNotificationService` et `InMemoryLocalNotificationService` supprimés (aucun test ne les utilisait). Bandeau « À cotiser » sur l'accueil, aucune permission.
6. **Revenus irréguliers (N14)** : `BudgetPlanService` (injecté) appelle `IrregularIncomePlanner` (classe statique, donc non injectable elle-même). Fixe : mensuel (revenu − épargne, plus de 150 000 par défaut inventé) ; Irrégulier : `BudgetPeriodMode.Weekly`, enveloppe = (moyenne des revenus non exceptionnels des 3 derniers mois complets, jamais avant le premier enregistrement, sinon revenu du profil − minimum vital) / 4 ; Mixte : `Envelope`. Profil : type de revenu, minimum vital, objectif de solidarité. Accueil et Analyses : enveloppe, reste après minimum vital, solidarité, fiabilité (clés `Budget_Reliability_*`).
7. **Auto-test** : étapes `tontine_contribution` et `irregular_envelope` (16 étapes).
8. `UiFormat.Date` (jj/mm/aaaa). Exemple de nom de l'accueil remplacé par « votre nom complet » (porte « Paul »).

Tests : `TontineServiceTests` (18), `BudgetPlanServiceTests` (11), `PageReachabilityTests` (2), `UiFormatTests.Date_*`. Preuves de cette session : `./tools/verify.ps1` **PASS** (231/231, build Android PASS, garde-fous PASS). Statuts : VÉRIFIÉ-TESTS, VÉRIFIÉ-BUILD ; CI et émulateur : pas encore (branche non poussée) ; NON-VÉRIFIÉ-APPAREIL.

Démo Windows (présentation) : le paquet MSIX ne s'installe pas sans le mode développeur de Windows (non activé, réglage système laissé à l'utilisateur). Une copie non empaquetée est construite avec `dotnet build SmartBudgetML.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o <dossier>` (projet inchangé).

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

Preuves de cette session : `./tools/verify.ps1` **PASS** (199/199 tests, build Android PASS, tous les garde-fous PASS) ; `dotnet format --verify-no-changes` OK sur les deux projets. Statuts : VÉRIFIÉ-TESTS, VÉRIFIÉ-BUILD ; puis VÉRIFIÉ-CI et VÉRIFIÉ-ÉMULATEUR pour ce que couvre l'auto-test (runs 39725000964 et 39725001152, `SB_SELFTEST|DONE|14/14` ; le menu de partage et le sélecteur de fichier ne sont pas couverts). Téléphone : NON-VÉRIFIÉ-APPAREIL (points 13 à 18 de la checklist).

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
1. Pousser `develop`, `feature/phase8-e-tontines` et le tag `Phase8-8E` si l'utilisateur le demande, puis noter les runs CI dans `docs/CI_RUN_LOG.md`. Tester les points 13 à 18 de la checklist sur le téléphone, et les tontines.
2. Lot suivant : 8F (import SMS), selon `docs/phase8/00-INDEX.md`.
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
