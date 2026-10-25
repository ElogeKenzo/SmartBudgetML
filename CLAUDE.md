# SmartBudget ML

Application de budget Android (.NET 10 MAUI Blazor Hybrid), 100 % locale, pour le Cameroun (FCFA, FR/EN).
Base SQLCipher, PIN 6 chiffres, ML.NET hybride (mémoire + règles + modèle). Distribution visée : Google Play.

## Commandes (PowerShell, racine du dépôt)
- Vérification complète : `./tools/verify.ps1` (créé au lot 8A ; avant : les 3 commandes ci-dessous)
- Tests : `dotnet test SmartBudgetML.Tests -c Release --logger "console;verbosity=minimal" | Select-Object -Last 30`
- Build Android : `dotnet build SmartBudgetML -f net10.0-android -c Release | Select-Object -Last 25`
- Build Windows (dev seulement) : `dotnet build SmartBudgetML -f net10.0-windows10.0.19041.0 | Select-Object -Last 25`

## Règles non négociables
1. Ne jamais écrire « fini / corrigé / vérifié » sans avoir lancé la commande de preuve dans CETTE session et lu son résultat.
2. Statuts autorisés, jamais plus haut que la preuve : VÉRIFIÉ-TESTS, VÉRIFIÉ-BUILD, VÉRIFIÉ-CI (run vert, lien noté dans docs/CI_RUN_LOG.md), VÉRIFIÉ-ÉMULATEUR, NON-VÉRIFIÉ-APPAREIL.
3. Aucune valeur affichée, métrique ou donnée inventée ou codée en dur. Les chiffres des documents viennent de `docs/generated/STATUS.md` (généré), jamais recopiés à la main.
4. Une fonctionnalité qui n'est pas atteignable depuis l'écran, ou pas enregistrée en base, n'existe pas : ne l'annonce nulle part.
5. Interdits : `catch {}` vide, `.Result`/`.Wait()`/`GetAwaiter().GetResult()`, texte en dur dans les `.razor`, `ex.Message` affiché à l'utilisateur.
6. Chaque correction = un test qui échoue avant le correctif (vérifie qu'il échoue, puis corrige).
7. Permissions Android autorisées : USE_BIOMETRIC uniquement. Toute autre : demander à l'utilisateur. Jamais READ_SMS / RECEIVE_SMS (Google Play).
8. Montants en `decimal`, XAF sans décimales. Catégories identifiées par leur clé stable (`CategoryCatalog`), jamais par leur nom affiché.

## Git
- GitFlow : `feature/<lot>` depuis `develop`, fusion `--no-ff` dans `develop` en local, tag `Phase8-<lot>`.
- Pas de `git push`, pas de `main`, pas de `--force` sans demande de l'utilisateur dans la session en cours.
- Commits Conventional, petits et atomiques. Jamais de secret, keystore, `.sb_vault_key`, données réelles.

## Ne jamais toucher
`main`, `docs/archive/`, l'historique Git, tout fichier hors du dépôt.

## Protocole de session (anti-pollution du contexte)
- Une session = un lot. Au démarrage : lis `docs/passation.md`, puis UNIQUEMENT le fichier du lot demandé dans `docs/phase8/`. Ne relis pas le dépôt entier ni les autres lots.
- Cherche avant de lire : Grep/Glob, puis Read avec offset et limit. Jamais `bin/`, `obj/`, `.resx` entiers, CSV entiers (utilise head/wc).
- Sorties longues : tronque (`Select-Object -Last 30`) ou écris dans `artifacts/*.txt` et ne lis que le résumé.
- Recherche sur plusieurs dossiers : délègue à un sous-agent Explore et ne garde que sa conclusion.
- Si la même correction échoue 2 fois de suite : STOP. Écris `docs/passation.md`, dis à l'utilisateur de faire `/clear`.
- Si la session s'alourdit (beaucoup de fichiers lus, longue discussion) : préviens l'utilisateur et propose `/compact` en précisant quoi garder.
- Fin de lot : vérification verte, mets à jour `docs/passation.md` et `docs/AUDIT_REGISTER.md`, commit, fusion locale, puis STOP avec le message « Lot terminé. Fais /clear avant le lot suivant. »

## Quand demander à l'utilisateur
Décision produit, donnée réelle manquante, droit administrateur, package incompatible sans alternative, migration pouvant détruire des données, ou toute question listée dans `docs/phase8/00-INDEX.md`.

## Repères
- Mission en cours : `docs/phase8/00-INDEX.md` (ordre des lots, décisions par défaut)
- Reprise de session : `docs/passation.md`
- Registre d'audit : `docs/AUDIT_REGISTER.md` — État généré : `docs/generated/STATUS.md`
- Ancienne mission (v2.0) archivée : `docs/archive/MISSION_V2_CLAUDE.md`
