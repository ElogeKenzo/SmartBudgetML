# Guide utilisateur — SmartBudget ML

> Ce guide décrit uniquement ce qui est atteignable dans l'application. Il sera complété au lot 8H.

## Exporter, sauvegarder et restaurer (Paramètres → Données)

### Exporter les dépenses (CSV)
1. Ouvrez *Paramètres*, puis touchez *Exporter les dépenses (CSV)*.
2. Le menu de partage d'Android s'ouvre : choisissez où enregistrer le fichier (Drive, Fichiers, WhatsApp…).
3. Le fichier CSV n'est **pas chiffré** : toute personne qui y a accès peut le lire.

L'application ne peut pas savoir si vous avez enregistré le fichier : elle vous invite seulement à choisir une application.

### Créer une sauvegarde chiffrée
1. Ouvrez *Paramètres*, puis touchez *Sauvegarde chiffrée (mot de passe)*.
2. Choisissez un mot de passe d'au moins 8 caractères et confirmez-le.
3. Touchez *Créer et partager la sauvegarde*. Le fichier `smartbudget-AAAAMMJJ.sbbackup` est créé, vérifié, puis proposé au partage.

**Important : le mot de passe n'est enregistré nulle part. S'il est perdu, la sauvegarde est illisible et personne ne peut la récupérer.** Notez-le dans un endroit sûr.

La sauvegarde n'utilise pas la clé du téléphone : elle peut donc être restaurée sur un nouveau téléphone.

### Restaurer une sauvegarde
1. Ouvrez *Paramètres*, puis touchez *Restaurer une sauvegarde*.
2. Touchez *Choisir le fichier* et sélectionnez votre fichier `.sbbackup`.
3. Saisissez son mot de passe, puis touchez *Vérifier la sauvegarde*.
4. Confirmez deux fois : **la restauration remplace toutes les données actuelles.**
5. L'application se verrouille : déverrouillez-la avec le code PIN utilisé au moment de la sauvegarde.

Si le mot de passe est faux, si le fichier est endommagé ou s'il vient d'une version plus récente de l'application, un message l'indique et vos données actuelles ne sont pas modifiées. Avant chaque restauration, une copie des données précédentes est gardée sur le téléphone.

### Données inaccessibles après un changement de téléphone ou une réinitialisation
Si l'écran *Données inaccessibles* s'affiche, vous pouvez réinitialiser le coffre, recréer votre profil, puis restaurer votre sauvegarde depuis *Paramètres* si vous avez le fichier `.sbbackup` et son mot de passe.

---

# User guide — SmartBudget ML (EN)

## Export, back up and restore (Settings → Data)

- **CSV export:** *Settings → Export Expense Data (CSV)* opens the Android share sheet; choose where to save the file. The CSV file is **not encrypted**.
- **Encrypted backup:** *Settings → Encrypted backup (password)*, choose a password of at least 8 characters, then *Create and share the backup*. **The password is not stored anywhere: if it is lost, the backup cannot be read and nobody can recover it.** The backup does not use the phone's key, so it can be restored on a new phone.
- **Restore:** *Settings → Restore a backup*, choose the `.sbbackup` file, enter its password, *Check the backup*, then confirm twice: **restoring replaces all current data.** The app then locks; unlock it with the PIN used when the backup was made. A wrong password, a damaged file or a backup from a newer app version is refused and your current data is left unchanged.
