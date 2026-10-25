# Lot 8E : tontines réelles, accessibles, et revenus irréguliers branchés

Branche : `feature/phase8-e-tontines`. Constats : N1, N13, N14.
Début de session : lis `docs/passation.md` et ce fichier uniquement. Si le lot devient trop gros, scinde-le en 8E1 (tontines) et 8E2 (revenus irréguliers) avec une passation entre les deux.

## Partie 1 : tontines
1. **Supprime toute donnée en dur** de `Tontines.razor` (tontine « Tontine Solidaire », membres Paul, Chantal, Eric, Nadine). Écran vide = invitation à créer.
2. **Service** `ITontineService` (`Services/Tontines/TontineService.cs`) sur les dépôts existants : créer, lire, modifier, supprimer une tontine ; membres et ordre de passage ; tours générés par `TontineCalculator` et enregistrés ; `RecordContributionAsync(tontineId, tour, membreId)` idempotent (une seule cotisation par tontine, tour et membre) qui crée la dépense liée ; `RecordPayoutAsync` quand l'utilisateur reçoit le pot (revenu `transfers_received`, marqué exceptionnel pour ne pas fausser la prévision). Ajoute à `Expense` les colonnes `SourceType` et `SourceId` pour lier la dépense à sa cotisation (migration sans perte) et empêcher les doublons.
3. **Écrans** : `/tontines` (liste), `/tontines/new` (nom, montant, fréquence hebdomadaire / bimensuelle / mensuelle, date de début, noms des membres, mon tour ; validations : montant > 0, au moins 2 membres, ordres uniques), `/tontines/{id}` (tour en cours, bénéficiaire, qui a payé, retards, mes cotisations, solde net, boutons « J'ai cotisé » et « J'ai reçu le pot »). Tout en FR/EN.
4. **Accès** : carte « Mes tontines » sur l'accueil avec le nombre de tontines et la prochaine échéance, et lien dans Profil. 
5. **Rappels (N13)** : par défaut, bandeau « À cotiser » sur l'accueil, calculé à l'ouverture, sans nouvelle permission. Retire `InMemoryLocalNotificationService` du code de production (il reste dans les tests). N'ajoute `POST_NOTIFICATIONS` que si l'utilisateur le demande.
6. **Test de garde `AllPagesAreReachable`** : analyse les `@page` de `Components/Pages` et vérifie que chaque route (sauf `unlock`, `onboarding`, `vault-error`) est référencée par un lien ou une navigation ailleurs. Il aurait détecté la page orpheline.
7. **Tests** sur une vraie base temporaire : créer → recharger ; cotisation enregistrée deux fois = une seule ligne ; paiement du pot ; retards calculés depuis la base ; passage de fin d'année ; membre qui quitte.

## Partie 2 : revenus irréguliers
8. Enregistre `IrregularIncomePlanner` dans l'injection de dépendances. Dans Profil : « Type de revenu » (Fixe / Irrégulier / Mixte), « Minimum vital », « Objectif de solidarité familiale » (champs déjà dans `UserProfile`).
9. Accueil et Analyses : en mode irrégulier, afficher l'enveloppe hebdomadaire et le reste après minimum vital ; `BudgetForecastingService` utilise `BudgetPeriodMode` selon le profil, avec l'indice de fiabilité affiché. Tests au niveau service avec une vraie base.
10. Ajoute les étapes à l'auto-test. Passation.

## Porte de validation
`AllPagesAreReachable` vert ; `Select-String "Paul|Chantal|Nadine|Tontine Solidaire"` ne trouve rien en production ; `Select-String "InMemoryLocalNotificationService" SmartBudgetML/MauiProgram.cs` vide ; `IrregularIncomePlanner` apparaît dans le code de l'interface.

## Pièges
- Une page ou un service testé mais jamais appelé n'est pas une fonctionnalité.
- Ne stocke pas de nom de membre réel dans les jeux de données.
