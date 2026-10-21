# Lot 8D : export, sauvegarde et restauration réellement utilisables

Branche : `feature/phase8-d-export-backup`. Constats : N4, N5, A4, A5.
Début de session : lis `docs/passation.md` et ce fichier uniquement.

Pourquoi : aujourd'hui les fichiers restent dans le dossier privé de l'application, inaccessible à l'utilisateur, et rien ne permet de restaurer. De plus la clé de chiffrement est liée au téléphone : une sauvegarde chiffrée avec elle serait illisible sur un nouveau téléphone, alors que changer ou perdre son téléphone est courant.

## Étapes
1. **Partage de fichiers** : `IFileExportService` gagne `ShareFileAsync(path, titre)`. Implémentation MAUI : écrire dans `FileSystem.CacheDirectory`, puis `Share.Default.RequestAsync(new ShareFileRequest …)`. Fausse implémentation pour les tests. L'écran dit « Choisis une application pour enregistrer le fichier » (le partage ne renvoie pas de résultat) ; ne dit jamais « exporté » avant.
2. **Export CSV** : branché sur le partage. Garde le format actuel (RFC 4180, BOM UTF-8).
3. **Sauvegarde par mot de passe** : l'écran demande un mot de passe (8 caractères minimum, confirmation). Fabrique une copie chiffrée avec CE mot de passe, pas avec la clé du téléphone : `ATTACH DATABASE '<tmp>' AS sauvegarde KEY '<mot de passe>'; SELECT sqlcipher_export('sauvegarde'); DETACH`, avec échappement des guillemets, `PRAGMA user_version` renseigné, fichier nommé `smartbudget-AAAAMMJJ.sbbackup`, puis partage. Ne journalise jamais le mot de passe.
4. **Restauration** : écran dans Paramètres. `FilePicker` → mot de passe → validation (ouverture avec le mot de passe, `integrity_check`, tables attendues, `user_version` ≤ version actuelle) → double confirmation « Cette action remplace toutes les données actuelles » → copie de sécurité automatique `.pre-restore` → import dans la base courante, rechiffrée avec la clé actuelle du téléphone → ré-initialisation de l'état. Mauvais mot de passe : message clair, base actuelle intacte.
5. **Tests** (base SQLCipher temporaire) : sauvegarde avec mot de passe puis restauration dans une base ayant une AUTRE clé maître, données identiques ; mauvais mot de passe : erreur et base inchangée ; fichier corrompu ; version trop récente ; restauration annulée ne modifie rien.
6. Supprime l'ancien chemin `smartbudget_backup_*.db3` de `Settings.razor` (non partageable, non restaurable). Textes localisés, aucune exception brute affichée.
7. Documentation : le README, la politique de confidentialité et le guide utilisateur expliquent qu'un mot de passe perdu rend la sauvegarde illisible.
8. Ajoute l'export + sauvegarde + restauration à l'auto-test de la CI (hors partage). Passation.

## Porte de validation
Tests de restauration croisée verts ; `Select-String "backup_" SmartBudgetML/Components` ne trouve plus l'ancien chemin ; chaque message « réussi » dépend d'un résultat vérifié.

## Pièges
- Ne confonds pas `VACUUM INTO` (ancienne méthode) avec la copie chiffrée par mot de passe.
- Ne stocke jamais le mot de passe de sauvegarde.
