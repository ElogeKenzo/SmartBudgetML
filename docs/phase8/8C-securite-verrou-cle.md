# Lot 8C : verrouillage visible, clé sans repli en clair, PIN hors du fil UI

Branche : `feature/phase8-c-securite`. Constats : N3, N6, N8, N16, migration des identifiants.
Début de session : lis `docs/passation.md` et ce fichier uniquement.

## Étapes
1. **Verrouillage visible (N3)** : `LocalAuthService` verrouille l'état mais rien ne prévient l'écran. Ajoute à `ILocalAuthService` un événement `LockStateChanged`, déclenché par `LockApp()` et par `EvaluateAutoLockOnResumeAsync` quand `AutoLockPolicy.ShouldLock` renvoie vrai. `MainLayout` s'y abonne (`InvokeAsync`, puis `NavigateTo("unlock", replace: true)`), se désabonne dans `Dispose`. Extrais la décision dans une petite classe pure (`LockNavigationCoordinator`) pour la tester. Tests : l'événement part dans les deux cas ; pas de navigation vers `/unlock` si on y est déjà.
2. **PBKDF2 hors du fil UI (N8)** : exécute `HashPin` dans `Task.Run` pour `SetPin`, `VerifyPin` et la montée de version. Pendant la vérification, le clavier du verrouillage est désactivé et affiche « Vérification… ». Mesure le temps sur l'hôte dans un test (affiche la valeur) ; ne change pas le nombre d'itérations. Le temps réel sur Redmi sera mesuré par l'utilisateur (point de la checklist).
3. **Clé de chiffrement sans repli en clair (N6)** : supprime `GetOrCreateFallbackFileKeyAsync` et le fichier `.sb_vault_key`. Introduis un état du coffre (`VaultState` : `Ready`, `KeyUnavailable`, `KeyLost`). Règles : 
   - si l'espace sécurisé échoue : `KeyUnavailable`, aucune clé écrite ailleurs ;
   - si une base chiffrée existe mais que la clé est introuvable : `KeyLost`, la base n'est pas touchée et aucune nouvelle clé n'est créée ;
   - une nouvelle clé n'est créée que s'il n'y a pas de base, ou si la base est en clair (migration).
   `DatabaseService` expose l'état ; l'application affiche une page `/vault-error` localisée avec « Réessayer » et « Réinitialiser le coffre (efface les données) » à double confirmation, en rappelant qu'une sauvegarde peut être restaurée (lot 8D). Tests avec de fausses clés : (a) échec du stockage donne `KeyUnavailable` et aucun fichier `.sb_vault_key` n'est créé ; (b) base existante sans clé donne `KeyLost` et le hachage du fichier base est inchangé ; (c) installation neuve crée la clé.
4. **Migration claire vers chiffré** : `InsertAllAsync` de sqlite-net n'insère pas la colonne à auto-incrément, donc les identifiants peuvent être renumérotés et casser `Expense.CategoryId`. Écris d'abord un test avec une base claire dont les identifiants ont des trous (lignes supprimées) et des dépenses liées à des catégories ; s'il échoue, remplace la copie par `ATTACH DATABASE … KEY …; SELECT sqlcipher_export('chiffrée'); DETACH` (vérifie que le package l'accepte), qui conserve tout, y compris les tables de tontines.
5. **Biométrie (N16)** : `IsAvailableAsync` doit renvoyer faux sous Android 9 (API 28) ou si aucune empreinte n'est enregistrée ; le bouton biométrique n'apparaît alors pas dans Profil ni sur l'écran de verrouillage. Statut NON-VÉRIFIÉ-APPAREIL.
6. Politique de confidentialité et fiche Data Safety : remplace « Keystore matériel » par « protégée par le Keystore Android lorsque l'appareil le permet ».
7. Ajoute des étapes à l'auto-test de la CI (ouverture de la base, verrouillage). Passation.

## Porte de validation
Vérification verte ; tests (a), (b), (c) et la migration avec trous verts ; `Select-String "sb_vault_key"` ne trouve plus rien dans le code.

## Pièges
- Ne jamais supprimer ni réécrire une base existante pour « réparer » : l'utilisateur décide.
- Ne journalise jamais la clé ni le PIN.
