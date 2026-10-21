# Lot 8B : une CI qui tourne vraiment

Branche : `feature/phase8-b-ci`. Constat : N9, A16.
Début de session : lis `docs/passation.md` et ce fichier uniquement.

## Étapes
1. **Script d'émulateur** : l'action `android-emulator-runner` exécute chaque ligne de `script:` dans un shell séparé, donc les variables et les blocs `if` du workflow actuel ne fonctionnent pas. Déplace tout dans `.github/scripts/emulator-smoke.sh` (`set -euo pipefail`) et appelle-le avec `script: bash .github/scripts/emulator-smoke.sh`.
2. **Activité de démarrage** : ne devine pas le nom de classe avec le hachage `crc64…`. Récupère-le avec `adb shell cmd package resolve-activity --brief com.smartbudgetml.app` et utilise la dernière ligne.
3. **Auto-test embarqué** (`#if SB_SELFTEST`, constante définie uniquement pour le build de test de la CI, jamais pour le build Play) : une classe `SelfTestRunner` lancée au démarrage. Elle ouvre la base chiffrée avec `MauiSecureKeyStore`, crée un profil et un PIN via `LocalAuthService`, ajoute une dépense, interroge le catégoriseur (« Moto bendskin Akwa » doit donner `transport`, « Facture ENEO » doit donner `utilities`), mesure le temps de hachage du PIN, calcule une prévision. Chaque étape écrit une ligne `SB_SELFTEST|étape|OK|durée_ms` dans logcat (tag `SB_SELFTEST`), puis `SB_SELFTEST|DONE|n_ok/n_total`. Les lots suivants ajouteront leurs propres étapes.
4. **Job émulateur** : installe l'APK de test, mesure `am start -W` (TotalTime), attend la ligne `DONE` (délai 120 s), relève `dumpsys meminfo` après l'auto-test, publie le tout dans le résumé du job, dépose logcat en artefact. Échec si une étape n'est pas OK.
5. **Vraies portes de qualité** : retire tout `|| echo` ; `dotnet format --verify-no-changes` sur les deux projets ; couverture lue dans le fichier Cobertura et comparée au seuil (si la mesure réelle est sous 70 %, comble avec de vrais tests, sinon fixe le seuil à la valeur mesurée moins 2 points et note-le, sans tricher) ; `dotnet list … package --vulnerable --include-transitive` analysé pour échouer en cas de vulnérabilité, sur le projet MAUI aussi.
6. **Inspection de l'APK** : avec `aapt2 dump badging`, affiche `targetSdkVersion`, `minSdkVersion` et les permissions dans le résumé. Échec si `targetSdkVersion` < 36 ou si une permission n'est pas dans la liste autorisée (`USE_BIOMETRIC`). Source de l'exigence : https://support.google.com/googleplay/android-developer/answer/11926878
7. **Job de release** (`workflow_dispatch` et tags `v*`) : lit les secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` ; s'ignore avec un avertissement s'ils sont absents ; `-p:ApplicationVersion=${{ github.run_number }}` ; n'affiche jamais un secret ; publie l'AAB signé.
8. Cache NuGet, annulation des runs en double (`concurrency`).
9. **Exécution réelle** : demande à l'utilisateur l'autorisation de pousser `develop` et la branche du lot. Puis `gh run list --limit 3`, `gh run watch`, et pour un échec `gh run view <id> --log-failed | Select-Object -Last 80`. Trois tentatives par échec au maximum, puis passation. Si `gh` n'est pas disponible, demande à l'utilisateur de coller les journaux.
10. Note les URL et résultats dans `docs/CI_RUN_LOG.md`. Seulement alors, les statuts VÉRIFIÉ-CI et VÉRIFIÉ-ÉMULATEUR sont autorisés, pour ce que l'auto-test couvre réellement.

## Porte de validation
Un run vert sur `develop` avec les lignes `SB_SELFTEST` visibles, ou un échec documenté avec sa cause.

## Pièges
- Le build de test (avec `SB_SELFTEST`) et le build Play sont deux builds distincts : vérifie que le job de release n'utilise pas la constante.
- Un émulateur est plus lent qu'un Redmi réel ou plus rapide selon la machine : publie les mesures sans les présenter comme celles d'un téléphone.
