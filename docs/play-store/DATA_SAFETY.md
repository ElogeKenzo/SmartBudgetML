# Formulaire « Sécurité des Données » (Google Play Data Safety) — SmartBudget ML

Réponses exactes à renseigner dans la **Google Play Console → Contenu de l'application → Sécurité des données** :

## 1. Collecte et partage des données
| Question de la Console Google Play | Réponse à sélectionner | Justification technique |
|---|---|---|
| **Votre application collecte-t-elle ou partage-t-elle certains des types de données utilisateur requis ?** | **Non** (`No`) | L'application ne possède même pas la permission `android.permission.INTERNET` dans `AndroidManifest.xml`. Aucune donnée ne quitte l'appareil vers le développeur ou un tiers. |
| **Toutes les données utilisateur collectées par votre application sont-elles chiffrées en transit ?** | **Sans objet** (Aucune donnée collectée ni transmise sur le réseau) | Zéro communication réseau. |
| **Proposez-vous aux utilisateurs un moyen de demander la suppression de leurs données ?** | **Oui** (Suppression locale intégrée) | L'écran *Paramètres → Effacer la base locale et la clé* (`PurgeDatabaseAndKeyAsync`) détruit immédiatement la base SQLCipher et la clé maître conservée via `SecureStorage` (protégée par le Keystore Android lorsque l'appareil le permet). |

## 2. Récapitulatif par catégorie de données Google Play
- **Informations personnelles (Nom, e-mail optionnel) :** Stockées **uniquement en local** sur l'appareil, jamais collectées ni partagées.
- **Informations financières (Dépenses, revenus, budgets, tontines) :** Stockées **uniquement en local** dans la base chiffrée SQLCipher 256 bits, jamais collectées ni partagées.
- **Messages (SMS Mobile Money) :** Aucune permission `READ_SMS` ou `RECEIVE_SMS`. Analyse locale en mémoire vive uniquement lorsque l'utilisateur colle volontairement un texte.
- **Identifiants de l'appareil ou autres ID :** Aucun SDK publicitaire, aucun tracker analytique (pas de Firebase Analytics, pas de Sentry, pas d'AdMob).
