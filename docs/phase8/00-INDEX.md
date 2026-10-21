# Phase 8 : corriger les écarts relevés par l'audit du code

Contexte : l'audit du code de `develop` a montré que les services (sécurité, base chiffrée, parseurs SMS, tests) sont solides, mais que l'interface est en partie factice et que plusieurs fonctions « corrigées » ne sont pas utilisables. Cette phase les rend réelles, vérifiées et honnêtes.

## Ordre des lots (une session par lot)
| Lot | Fichier | Sujet | Priorité |
|---|---|---|---|
| 8A | `8A-verite-et-build.md` | Build réel, script de vérification, documents réalignés | Bloquant |
| 8B | `8B-ci-reelle.md` | CI qui tourne vraiment, auto-test sur émulateur | Bloquant |
| 8C | `8C-securite-verrou-cle.md` | Verrouillage visible, clé sans repli en clair, PIN hors thread UI | Bloquant |
| 8D | `8D-export-backup-restore.md` | Partage de fichiers, sauvegarde par mot de passe, restauration | Bloquant |
| 8E | `8E-tontines-reelles.md` | Tontines persistées, accessibles, revenus irréguliers branchés | Bloquant |
| 8F | `8F-import-sms.md` | Import SMS réel (page, partage Android, enregistrement) | Bloquant |
| 8G | `8G-ml-17-categories.md` | Modèle sur les 17 catégories, FR + EN, évaluation honnête | Important |
| 8H | `8H-localisation-conformite-play.md` | Textes, À propos, avertissement, dossier Play Store | Important |

Les lots se font dans l'ordre. 8B vient tôt pour que chaque lot suivant reçoive un retour de la CI.

## Constats à traiter (IDs à ajouter au registre en 8A)
- N1 Page Tontines = maquette (données en dur, rien en base, aucun lien d'accès)
- N2 Import SMS : affiche seulement, n'enregistre rien, sans partage Android, caché dans la page Tontines
- N3 Verrouillage au retour d'arrière-plan sans effet à l'écran
- N4 Export CSV et sauvegarde dans un dossier privé, sans partage
- N5 Restauration sans écran
- N6 Repli de la clé en fichier clair `.sb_vault_key` ; base illisible si la clé change
- N7 Modèle ML entraîné sur 11 anciennes catégories anglaises ; 9 des 17 catégories impossibles à prédire ; pas de jeu EN
- N8 PBKDF2 600 000 itérations sur le fil de l'interface
- N9 CI jamais exécutée ; script d'émulateur multi-lignes invalide ; portes de qualité factices
- N10 Textes en anglais et erreurs brutes (`ex.Message`) dans Paramètres, Profil, Ajout de dépense ; Tontines en français en dur
- N11 Pas d'écran « À propos », pas d'avertissement « pas un conseil financier »
- N12 Vrais identifiants de transaction et soldes dans les tests SMS (dépôt public)
- N13 Rappels de tontine fictifs (`InMemoryLocalNotificationService`)
- N14 `IrregularIncomePlanner`, `BudgetPeriodMode`, `IncomeProfileType` jamais utilisés par l'interface
- N15 `.gitignore` sans `*.jks`
- N16 Biométrie « disponible » sur Android 7-8 alors que l'authentification renvoie toujours faux
- N17 Documents contradictoires (chiffres ML, parseurs SMS, fiabilité, nombre de clés de traduction)
À rouvrir : A16 (CI non exécutée). Partiels : A4, A5, A8, A11, A12.

## Décisions par défaut (l'agent les applique sauf contre-ordre de l'utilisateur)
1. Aucune notification système (aucune nouvelle permission) : un bandeau « À cotiser » sur l'accueil.
2. Sauvegarde chiffrée par mot de passe choisi par l'utilisateur (restaurable sur un autre téléphone).
3. Pas de réécriture de l'historique Git pour retirer les anciens identifiants SMS.
4. Si le modèle ML ne dépasse pas les règles d'au moins 3 points de macro-F1 : proposer de le retirer (APK plus léger) et demander.
5. Cible Windows conservée pour le développement uniquement.

## Questions à poser à l'utilisateur au bon moment
- Fin de 8A : autorisation de pousser `develop` et la branche du lot pour que la CI tourne.
- 8H : nom du développeur, e-mail de contact, hébergement de la politique de confidentialité (GitHub Pages ?).

## Définition de « terminé » pour la phase 8
- `./tools/verify.ps1` vert, `docs/generated/STATUS.md` à jour.
- Un run CI vert noté dans `docs/CI_RUN_LOG.md`, avec test sur émulateur.
- Chaque ligne du registre a un statut final et une preuve.
- Aucune fonction annoncée (README, fiche Play, politique) qui ne soit atteignable et enregistrée.
- Reste à l'utilisateur : test sur son Redmi (`docs/DEVICE_TEST_CHECKLIST.md`), clé de signature, publication.

## Modèle de début et de fin de session
Début : lire `docs/passation.md` et le fichier du lot, rien d'autre. Annoncer en une phrase le plan.
Fin : vérification verte, mise à jour de la passation et du registre, commit, fusion locale, puis « Lot terminé. Fais /clear avant le lot suivant. »
