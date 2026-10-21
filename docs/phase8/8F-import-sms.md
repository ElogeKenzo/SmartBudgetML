# Lot 8F : import des SMS Mobile Money, de bout en bout

Branche : `feature/phase8-f-import-sms`. Constat : N2. Aucune permission SMS, jamais (Google Play).
Début de session : lis `docs/passation.md` et ce fichier uniquement.

Aujourd'hui les parseurs fonctionnent (Orange Money et MTN MoMo), mais l'import affiche seulement un résultat, dans la page Tontines, sans rien enregistrer ni partage Android.

## Étapes
1. **Page `/import-sms`**, accessible depuis Ajout de dépense (« Importer un SMS Mobile Money ») et depuis une action rapide de l'accueil. Zone de texte, bouton « Coller » (lit le presse-papiers uniquement au toucher, avec `Clipboard.Default.GetTextAsync`), bouton « Analyser ». Message d'aide : « Copiez le SMS dans Messages, puis collez-le ici. » Retire la zone SMS de la page Tontines.
2. **Liste des résultats** : une ligne par transaction reconnue (case à cocher cochée par défaut), type, montant, frais, contrepartie, date, catégorie suggérée modifiable. Doublons marqués « Déjà importé » et décochés. SMS non reconnus listés à part, sans plantage.
3. **Catégorie suggérée** : correspondance type → catégorie (crédit/forfait → `telecom_data`, paiement marchand → catégoriseur hybride sur le nom du marchand, réception → `transfers_received`, retrait → `other`, etc.), documentée dans le code. Les frais deviennent une ligne séparée en `other`.
4. **Enregistrement** : bouton « Ajouter les sélectionnés ». Ajoute à `Expense` les colonnes `ExternalReference` et `ImportSource` (migration sans perte) avec une unicité par opérateur et référence ; le dédoublonnage se fait contre la base, pas seulement dans la liste collée. Message de fin : nombre ajoutés / ignorés.
5. **Partage depuis Messages** : dans `MainActivity`, ajoute un `intent-filter` `ACTION_SEND` de type `text/plain` (aucune permission). Traite le texte reçu dans `OnCreate` et `OnNewIntent` via un `IPendingShareService`. **Ne jamais ouvrir l'import tant que l'application est verrouillée** : garde le texte en attente en mémoire, ouvre `/import-sms` après le déverrouillage. Le texte reçu n'est jamais journalisé.
6. **Tests** (échantillons déjà fictifs depuis 8A) : analyse → catégorie → enregistrement → second import ignoré ; SMS inconnu ; lot de 50 SMS (temps affiché) ; partage reçu pendant le verrouillage reste en attente ; test de garde sur le manifeste (le `intent-filter` est présent, `READ_SMS` absent).
7. Mets à jour `docs/DECISION_SMS.md` : ce qui existe réellement, ce qui est exclu, pourquoi.
8. Étape d'auto-test : analyse d'un SMS fictif et enregistrement. Passation.

## Porte de validation
Vérification verte ; la page `/import-sms` est atteignable (test `AllPagesAreReachable`) ; `Select-String "READ_SMS|RECEIVE_SMS" SmartBudgetML/Platforms/Android/AndroidManifest.xml` vide ; un second import du même SMS n'ajoute rien.

## Pièges
- Ne lis jamais le presse-papiers en arrière-plan ni à l'ouverture sans geste de l'utilisateur.
- Ne crée aucune dépense sans que l'utilisateur ait validé.
