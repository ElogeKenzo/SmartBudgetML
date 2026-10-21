# Lot 8H : textes, « À propos », avertissement et dossier Google Play

Branche : `feature/phase8-h-play`. Constats : N10, N11 et corrections du dossier Play.
Début de session : lis `docs/passation.md` et ce fichier uniquement.

## Étapes
1. **Textes** : remplace tous les messages en dur de `Settings.razor`, `Profile.razor`, `AddExpense.razor`, `Unlock.razor` et des pages ajoutées aux lots précédents par des clés `fr` et `en`. Les erreurs montrent un message générique localisé ; le détail va dans le journal (`ILogger`), jamais `ex.Message` à l'écran. Test `NoHardcodedUserFacingStrings` : analyse les `.razor` et les affectations de `FeedbackMessage`, `StatusMessage`, `ErrorMessage`, `StatusBanner` pour détecter les littéraux hors liste d'exceptions. Le test de parité FR/EN existant reste.
2. **Langue du téléphone** : au premier lancement, `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName` : `fr` → fr, `en` → en, sinon fr. Enregistrée, modifiable. Test avec un fournisseur de culture factice.
3. **Écran `/about`** (lien depuis Paramètres) : version (`AppInfo.VersionString`), licences des composants, lien vers la politique de confidentialité, e-mail de contact. Les licences viennent des fichiers LICENSE des packages NuGet réellement utilisés (SQLCipher Community Edition, sqlite-net, SQLitePCLRaw, ML.NET, .NET MAUI) : lis-les, ne les invente pas ; génère `Resources/Raw/THIRD_PARTY_NOTICES.txt` avec un script ; l'écran l'affiche. Le nom du développeur, l'e-mail et l'URL de la politique sont des constantes de configuration à renseigner par l'utilisateur (demande-les).
4. **Avertissement** : texte FR/EN « SmartBudget ML est un outil de suivi budgétaire. Il ne fournit pas de conseil financier et n'est affilié à aucune banque ni à aucun opérateur Mobile Money. » dans l'onboarding (à accepter), dans À propos, et en note sous les prévisions.
5. **Fiche Play (`docs/play-store/STORE_LISTING.md`)** : uniquement des fonctions qui existent et sont atteignables après les lots 8C à 8F ; retire « moins d'une milliseconde » et toute mention de Windows ; complète la description anglaise ; remplace « PEGI 3 » par le questionnaire de classification IARC de la Play Console ; ajoute la liste des captures d'écran à produire (accueil, ajout avec suggestion, analyses, tontine, import SMS, sauvegarde, À propos, verrouillage).
6. **Politique de confidentialité FR/EN** : nom du développeur, e-mail de contact, date d'effet, explication des sauvegardes par mot de passe et des partages lancés par l'utilisateur ; explique comment l'héberger à une URL publique (par exemple GitHub Pages sur `docs/`). **Fiche Data Safety** : relis chaque réponse par rapport au code final (aucune permission internet, aucune donnée collectée). Indique de vérifier dans la Play Console si une déclaration de fonctionnalités financières est demandée.
7. **Guide de publication** : cohérent avec le job de release de 8B ; rappelle la source de l'exigence de version cible (https://support.google.com/googleplay/android-developer/answer/11926878).
8. **Checklist Redmi (`docs/DEVICE_TEST_CHECKLIST.md`)** : ajoute le verrouillage visible au retour d'arrière-plan, le temps de vérification du PIN, sauvegarde et restauration (idéalement sur un second appareil), tontines, import et partage SMS, stockage presque plein, mode avion, une application lourde en arrière-plan.
9. **Documents finaux** : réécris `RAPPORT_PROJET.md` et `README.md` en renvoyant à `STATUS.md`, avec une section « Limites connues » exacte ; registre d'audit : chaque ligne A1 à A24 et N1 à N17 a un statut final et une preuve ; résumé final pour l'utilisateur (corrigé, ouvert, à faire de son côté).

## Porte de validation
Vérification verte et run CI vert ; `NoHardcodedUserFacingStrings` vert ; aucun texte promotionnel sans fonction correspondante ; registre complet.

## Pièges
- Ne devine pas le nom, l'e-mail ni l'URL : demande-les.
- Ne publie rien et ne crée aucun compte.
