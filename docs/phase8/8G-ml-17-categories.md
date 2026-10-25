# Lot 8G : modèle aligné sur les 17 catégories, FR + EN, évaluation honnête

Branche : `feature/phase8-g-ml`. Constat : N7, A11 (partiel).
Début de session : lis `docs/passation.md` et ce fichier uniquement. N'ouvre pas les CSV en entier (head, wc, Grep).

Problème : le modèle et les règles sortent les 11 anciens libellés anglais. Tontine devient « Investments », frais de scolarité « Other », forfait data « Utilities ». Neuf des 17 catégories camerounaises ne peuvent donc jamais être proposées, et il n'y a aucune donnée en anglais.

## Étapes
1. **Étiquettes = clés stables** des 17 catégories (`food_market`, `transport`, `housing_rent`, `utilities`, `telecom_data`, `healthcare`, `education`, `tontine`, `family_solidarity`, `events`, `church_donations`, `entertainment`, `savings`, `other`, `salary`, `business_hustle`, `transfers_received`). Remplace les libellés anglais dans le jeu d'entraînement, les règles, `ModelEvaluator`, `CategoryPrediction`. Garde l'alias vers les anciens libellés uniquement pour relire d'anciens retours stockés dans `CategoryFeedback` (migration des anciennes valeurs vers les clés).
2. **Type de transaction** : `Predict(description, isIncome)` ne propose que des catégories de revenu pour un revenu et de dépense pour une dépense. Documente le cas « pot de tontine reçu » : catégorie `transfers_received`.
3. **Nouveau jeu d'entraînement synthétique** (marqué comme tel, sans donnée personnelle) : au moins 30 exemples par catégorie en français et 20 en anglais, avec abréviations et fautes de frappe courantes. Garde le fichier versionné dans `Resources/Raw/`.
4. **Nouveau jeu de validation écrit à part** : au moins 10 exemples par catégorie, FR et EN, jamais utilisé pour l'entraînement ni pour régler les règles. Test anti-fuite renforcé : égalité après normalisation ET quasi-doublons (similarité de mots ≥ 0,8 ou distance d'édition ≤ 2) ; zéro autorisé.
5. **Règles** : mets à jour les mots-clés vers les nouvelles clés (njangi, tontine → `tontine` ; scolarité, pension, écolage → `education` ; forfait, data, crédit, recharge → `telecom_data` ; deuil, mariage, baptême → `events` ; dîme, offrande → `church_donations` ; envoi au village, aide famille → `family_solidarity`).
6. **Évaluation** : règles seules, ML seul, hybride ; précision, rappel et F1 par catégorie, F1 macro, matrice de confusion, par langue (FR et EN séparément). Écris `artifacts/ml-eval.json` ; `tools/verify.ps1` l'injecte dans `docs/generated/STATUS.md`. `docs/ML_EVALUATION.md` référence ce fichier, et précise clairement : jeux synthétiques écrits par une IA, donc résultats optimistes.
7. **Seuils de non-régression** = valeurs mesurées moins 3 points. Aucune affirmation publique supérieure à la mesure.
8. **Protocole avec de vraies données** : `docs/ML_REAL_DATA_PROTOCOL.md` explique comment l'utilisateur exporte ses transactions en CSV, étiquette environ 200 lignes à la main et lance une évaluation locale (`artifacts/real_labeled.csv`, ignoré par Git, jamais commité).
9. Incrémente la version du schéma du modèle, vérifie le rechargement, remesure latence et taille.
10. **Décision** : si l'hybride ne dépasse pas les règles seules d'au moins 3 points de F1 macro, écris-le avec les chiffres, recommande de retirer ML.NET (APK plus léger) et demande à l'utilisateur. Passation.

## Porte de validation
Vérification verte ; chaque catégorie des 17 est prédite au moins une fois correctement dans la validation ; anti-fuite à zéro ; les nombres du document viennent de `STATUS.md`.

## Pièges
- Ne règle pas les règles en regardant le jeu de validation.
- Ne présente jamais un résultat sur données synthétiques comme la précision réelle.
