# Lot 8A : vérité, build réel et script de vérification

Branche : `feature/phase8-a-verite`. Aucune nouvelle fonctionnalité dans ce lot.
Constats : N12, N15, N17, A16 ; risque : le projet MAUI n'a peut-être plus été compilé depuis la phase 3.

## Étapes
1. Crée la branche depuis `develop`. Lis `docs/passation.md`.
2. **Build réel du projet MAUI** (les tests ne le compilent pas) : `dotnet build SmartBudgetML -f net10.0-android -c Release` puis la cible Windows. Corrige uniquement les erreurs de compilation, sans rien changer d'autre. Note le nombre d'avertissements. Si le workload Android manque ou demande un droit administrateur : STOP et dis-le.
3. Lance les tests et note le nombre exact de cas, de réussites, d'échecs, d'ignorés.
4. **Chiffres du modèle** : `docs/ML_EVALUATION.md` donne 98,86 % (174/176), le rapport de phase 4 et le README donnent 99,43 % (175/176). Lance le test de comparaison règles / ML / hybride avec une sortie détaillée dans `artifacts/ml-eval-output.txt`. Fais écrire au test un fichier `artifacts/ml-eval.json` (accuracy, macro-F1 de chaque approche, latence moyenne et p95, taille du modèle). Réécris `ML_EVALUATION.md` à partir de la vraie sortie et supprime l'autre série.
5. **Crée `tools/verify.ps1`** : compile les tests, lance les tests (sortie minimale), compile Android (option `-SkipAndroid`), exécute les garde-fous (aucun `catch {}` vide, aucun `GetAwaiter().GetResult()`, aucune permission hors `USE_BIOMETRIC` dans le manifeste, aucun `READ_SMS`, `git ls-files` sans keystore ni `.sb_vault_key`), copie `artifacts/ml-eval.json`, écrit `docs/generated/STATUS.md` (date, hash du commit, comptes de tests, résultat de chaque garde-fou, métriques ML). Elle affiche 25 lignes maximum et renvoie un code d'erreur si un garde-fou échoue.
6. **Réaligne les documents sur la réalité** : README, RAPPORT_PROJET.md, rapports PHASE-6 et PHASE-7. Remplace chaque chiffre recopié (tests, métriques, latences) par un renvoi vers `docs/generated/STATUS.md`. Corrige : parseurs SMS implémentés (§5.1 du rapport et README périmés), nombre de clés de traduction (compte-les), seuils de fiabilité de la prévision (lis le code ; si le code dit « faible » sous 5 jours alors que la règle est sous 7, corrige le code et son test, puis les documents). Retire toute mention `VÉRIFIÉ-ÉMULATEUR` tant qu'aucun run CI n'existe.
7. **Registre** : ajoute N1 à N17 (voir `00-INDEX.md`) au statut OUVERT, rouvre A16, passe A4, A5, A8, A11, A12 en PARTIEL avec la raison.
8. **Identifiants réels dans les tests SMS** (N12) : remplace les vrais identifiants de transaction et soldes par des valeurs fictives de même forme (même nombre de chiffres, mêmes séparateurs). La logique des parseurs ne change pas ; les tests doivent rester verts. Précise à l'utilisateur que l'historique Git contient encore les originaux (ne pas le réécrire).
9. **`.gitignore`** (N15) : ajoute `*.jks`, `*.pfx`, `*.p12`, `.sb_vault_key`, `local.properties`, `artifacts/`.
10. Mets à jour `docs/passation.md`.

## Porte de validation
- `./tools/verify.ps1` vert et `docs/generated/STATUS.md` produit.
- Les deux builds MAUI passent (ou l'obstacle est documenté).
- `Select-String` sur README et RAPPORT ne trouve plus de chiffre de test ni de métrique recopié à la main.
- Registre à jour.

## Pièges
- Ne pas « arranger » un chiffre pour que deux documents concordent : la sortie réelle fait foi.
- Ne pas modifier la logique des parseurs en masquant les identifiants.
