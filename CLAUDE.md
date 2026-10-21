# MISSION : faire passer SmartBudget ML au niveau supérieur (v1.0 → v2.0)

Tu es l'ingénieur principal de ce projet. Tu travailles dans le dépôt local :
`C:\Users\abel kenzo\Desktop\App Mobile Smart budget\SmartBudgetML` (solution `SmartBudgetML.sln`, projet MAUI `SmartBudgetML/`, tests `SmartBudgetML.Tests/`).
Environnement : Windows + PowerShell. Dépôt GitHub : ElogeKenzo/SmartBudgetML.

Ce document est ta **spécification complète**. Lis-le en entier avant de toucher au moindre fichier.

---

## 0. CONTEXTE (à comprendre avant d'agir)

SmartBudget ML est une application mobile de budget personnel, 100 % locale (aucun serveur), faite avec **.NET 8 MAUI Blazor Hybrid**, SQLite (`sqlite-net-pcl`), ML.NET 3.0.1 (classification de texte SDCA + régression linéaire pour prévoir la fin de mois), PIN PBKDF2 + biométrie.

**Public cible réel : le Cameroun.** Utilisateurs sur Android d'entrée de gamme (2-3 Go de RAM, stockage limité), data chère, réseau instable, paiements dominés par **Orange Money et MTN Mobile Money**, espèces, **tontines (njangi)**, revenus irréguliers, solidarité familiale. Langues : français d'abord, anglais ensuite, pidgin dans le vocabulaire de catégorisation.

Un audit externe a été fait. Il a trouvé que **le rapport de projet affirme des choses que le code ne fait pas**. Ta première mission est de rendre le projet **honnête et vrai**, la deuxième de le **sécuriser**, la troisième de le **rendre pertinent pour le Cameroun**.

### DÉCISIONS DÉJÀ VALIDÉES PAR L'UTILISATEUR (elles priment sur tout le reste du document)

1. **Distribution : Google Play Store.** Conséquences : livrable de publication = **AAB** (Android App Bundle), pas seulement APK ; permissions minimales ; politique de confidentialité et formulaire « Sécurité des données » à préparer ; **la permission de lecture des SMS (`READ_SMS` / `RECEIVE_SMS`) est EXCLUE** (politique Google Play restrictive, une app de budget n'y est en pratique pas éligible). Le mode SMS retenu est **uniquement « coller / partager le texte du SMS »**. Ne propose ni `READ_SMS`, ni lecture automatique par service de notifications.
2. **Appareil de référence : Redmi avec 3 Go de RAM** (Android d'entrée de gamme). Tous les budgets de performance se calculent pour cette classe d'appareil (voir « Budgets de performance » ci-dessous). Un **test sur émulateur Android configuré comme ce Redmi (3 Go de RAM, écran ~720x1600, ARM ou x86 selon ce que la CI permet)** est **obligatoire en CI** (Phase 7).
3. **Plateforme : Android uniquement pour le moment.** Retire les cibles `ios`, `maccatalyst` et `tizen` du `.csproj` ainsi que leurs dossiers `Platforms/iOS`, `Platforms/MacCatalyst`, `Platforms/Tizen` (en commit séparé, réversible). Tu peux **garder la cible Windows uniquement comme confort de développement local** (non distribuée) si elle compile encore ; sinon retire-la aussi et note-le.
4. **Langues : français (par défaut) et anglais.**
5. **Vrais SMS Mobile Money : l'utilisateur les fournira quand tu arriveras à la Phase 6.1.** Tu **dois alors t'arrêter et les lui demander** avec le message exact prévu dans la Phase 6.1. Ne code aucune expression régulière d'opérateur avant de les avoir reçus.

### Budgets de performance (cibles à mesurer, pas des faits acquis)

À vérifier sur émulateur en CI quand c'est possible, et par l'utilisateur sur son Redmi via `docs/DEVICE_TEST_CHECKLIST.md`. Si tu ne peux pas mesurer une cible, écris « non mesuré » ; n'écris jamais une valeur supposée.

| Indicateur | Cible |
|---|---|
| Taille de téléchargement Play (AAB → APK par appareil) | < 40 Mo (cible haute : 60 Mo) |
| Démarrage à froid jusqu'à l'écran de déverrouillage | < 3 s |
| Mémoire en usage normal | < 250 Mo |
| Suggestion de catégorie après la saisie (hors entraînement) | < 100 ms perçus |
| Ajout d'une dépense jusqu'à l'affichage à jour | < 500 ms |
| Aucun blocage UI (> 100 ms) pendant l'entraînement du modèle | 0 |

### Constats de l'audit (à corriger, vérifie chacun toi-même dans le code avant de le traiter)

| # | Constat | Où regarder |
|---|---|---|
| A1 | La base SQLite n'est **pas chiffrée** (le rapport dit le contraire). `SQLitePCLRaw.bundle_green` n'est pas SQLCipher. | `SmartBudgetML.csproj`, `DatabaseService.cs`, `DatabaseConstants.cs` |
| A2 | La « précision du modèle » est **codée en dur** (`ModelAccuracy = 95.8`, puis `95.8 + n*0.15`). | `Components/Pages/Settings.razor` (~l.162, ~l.211) |
| A3 | Le curseur « seuil de confiance ML » (`MlConfidenceThreshold`) n'est **branché à rien** ; `AddExpense` utilise `0.70f` en dur. | `Settings.razor`, `AddExpense.razor` (`OnDescriptionChanged`) |
| A4 | `ExportData()` construit un CSV en mémoire mais **ne l'écrit nulle part** et affiche « Exported ». | `Settings.razor` (~l.225) |
| A5 | `BackupDatabase()` **affiche seulement un message**. | `Settings.razor` (~l.241) |
| A6 | Taille de base affichée **codée en dur** (`"180 KB"`). | `Settings.razor` |
| A7 | Les corrections de l'utilisateur sont stockées dans une `List` **en mémoire** : perdues au redémarrage. | `ExpenseCategorizerService.cs` (`_userTrainingBuffer`) |
| A8 | **Aucun verrouillage** à la mise en veille / retour d'application. `AutoLockTimeoutSeconds` n'est jamais utilisé. `App.xaml.cs` ne gère pas le cycle de vie. | `App.xaml.cs`, `MainLayout.razor`, `AppSettings.cs` |
| A9 | **Aucune limite de tentatives de PIN** (4 chiffres = 10 000 combinaisons testables à la main). | `LocalAuthService.VerifyPinAsync` |
| A10 | `Predict()` peut appeler `TrainAsync().GetAwaiter().GetResult()` (synchrone sur async) : risque de **blocage de l'UI**. De plus le modèle est **réentraîné entièrement à chaque dépense** ajoutée, sans persistance du modèle. | `ExpenseCategorizerService.cs` |
| A11 | Données d'entraînement : **74 phrases américaines** (Starbucks, Walmart, Amtrak…), inutiles au Cameroun. | `Services/ML/SeedTrainingData.cs` |
| A12 | UI et catégories **en anglais**, devise par défaut **USD**, plafonds en dollars (350, 800…). | `DatabaseService.SeedDefaultDataAsync`, pages Razor |
| A13 | Montants formatés avec `{x:C0}` (dépend de la culture de l'appareil) dans les messages de prévision. | `BudgetForecastingService.cs` |
| A14 | **.NET 8 MAUI n'est plus supporté** (fin mai 2025). L'avertissement `NETSDK1202` a été contourné au lieu d'être réglé ; la propriété `CheckEolTargetFramework=false` citée dans le rapport n'existe d'ailleurs pas dans le `.csproj`. | `SmartBudgetML.csproj`, `global.json` |
| A15 | `Plugin.Fingerprint 3.0.0-beta.1` : **version bêta**. | `SmartBudgetML.csproj` |
| A16 | L'application n'a **jamais été lancée sur Android** (démo filmée sur Windows). Le fonctionnement de ML.NET sur Android ARM n'est pas prouvé. | rapport, CI |
| A17 | Les `catch { }` vides avalent les erreurs. | `AddExpense.razor` et autres |
| A18 | Le rapport contient des inexactitudes (répartition des tests, « push » en CI, `salt:hash`, etc.). | `RAPPORT*.md` |
| A19 | Régression prévisionnelle : revenus supposés mensuels fixes ; pas de gestion des revenus irréguliers. | `UserProfile.cs`, `BudgetForecastingService.cs` |

---

## 1. RÈGLES DE CONDUITE (non négociables)

### 1.1 Honnêteté et preuves
1. **Ne déclare jamais qu'une chose fonctionne sans l'avoir exécutée.** Chaque affirmation (« tests passent », « modèle précis à X % ») doit s'appuyer sur une commande que tu as lancée et dont tu colles la sortie résumée dans le rapport de phase.
2. **N'invente aucune donnée.** Pas de précision fictive, pas de faux SMS Mobile Money « réels », pas de métriques estimées. Les jeux de données que tu rédiges sont **synthétiques** : marque-les comme tels dans le code et la doc.
3. **Étiquette chaque fonctionnalité** dans la doc avec un statut : `VÉRIFIÉ-TESTS` (couvert par des tests automatisés exécutés), `VÉRIFIÉ-BUILD` (compile pour la cible), `NON-VÉRIFIÉ-APPAREIL` (jamais exécuté sur un vrai téléphone). Tout ce qui touche à Android natif (biométrie, SecureStorage, SMS, SQLCipher natif, ML.NET sur ARM) est **NON-VÉRIFIÉ-APPAREIL** tant que l'utilisateur ne l'a pas testé sur un téléphone.
4. Si tu constates que l'audit se trompe sur un point, dis-le et documente-le. Si tu trouves un problème que l'audit n'avait pas vu, ajoute-le au registre (`docs/AUDIT_REGISTER.md`).

### 1.2 Contrainte matérielle : on ne peut pas installer l'application
L'application n'est pas encore installable/validée sur un téléphone. **Ne tente pas** de lancer un émulateur Android, de déployer un APK ni de lancer l'app MAUI. À la place :
- **Toute logique métier doit vivre dans du code testable sans MAUI** (projet de tests existant `net` console, qui lie les sources de `Models/`, `Services/`, `Components/Shared/UiFormat.cs`). Pour tout nouveau code non lié à la plateforme, il doit être ajouté à ce mécanisme de liens et **testé en xUnit**.
- Tout code spécifique plateforme (biométrie, SecureStorage, partage de fichiers, SMS) est derrière **une interface** (`IBiometricService`, `ISecureKeyStore`, `IFileExportService`, etc.) avec **une implémentation MAUI** et **une fausse implémentation pour les tests** (Moq ou fake maison).
- Pour **ML.NET**, la preuve se fait **par tests sur l'hôte** : entraînement, évaluation sur jeu de validation, sérialisation/chargement du modèle, temps d'inférence, taille du fichier modèle. Voir §Phase 4. Ces tests prouvent la **logique**, pas le fonctionnement sur ARM : écris-le explicitement dans la doc.
- Vérifie que le projet **compile pour Android** (`dotnet build -f net…-android` si le workload est installé ; sinon via la CI GitHub Actions). Si le workload Android n'est pas installable localement (droits administrateur), **ne le contourne pas** : note-le, laisse la CI faire le build Android, et passe à la suite.

### 1.3 Processus Git
- Respecte GitFlow tel qu'utilisé dans le dépôt : une branche `feature/<nom>` par phase, créée depuis `develop`, fusionnée avec `--no-ff`, tags de phase.
- Messages de commit en **Conventional Commits** (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`), petits commits atomiques (un sujet par commit).
- **Ne fais jamais `git push`, ne fusionne jamais dans `main`, et ne force jamais rien** sans demande explicite de l'utilisateur. Fusionne dans `develop` localement quand la porte de validation de la phase est verte.
- Ne supprime jamais de fichiers de l'utilisateur hors du périmètre du projet. Pas de `Remove-Item -Recurse` hors `bin/` et `obj/`.

### 1.4 Qualité de code
- C# 12/13, `Nullable` activé, **zéro nouveau warning** par rapport à la base de départ (mesure-la en Phase 0).
- **Interdit** : `catch { }` vide. Toute exception est soit gérée avec un message utilisateur adapté, soit journalisée via `ILogger`, soit propagée.
- **Interdit** : valeur affichée à l'utilisateur qui ne vient pas d'une vraie source (voir A2, A6).
- **Interdit** : appeler une méthode async de façon bloquante (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) dans du code UI.
- Chaque correction ou fonctionnalité ⇒ **au moins un test** qui échoue sans elle (écris le test, vérifie qu'il échoue, puis corrige).
- Les montants restent en `decimal`. La devise **XAF/FCFA n'a pas de décimales** : arrondis et affiche en conséquence.
- Aucune chaîne utilisateur en dur dans les `.razor` à partir de la Phase 5 : tout passe par le système de localisation.

### 1.5 Suivi du travail
- Utilise la liste de tâches (TodoWrite/TaskCreate) : une tâche par phase, des sous-tâches par item. Mets-la à jour au fil de l'eau.
- Tiens à jour `docs/AUDIT_REGISTER.md` : tableau `ID | constat | statut (ouvert/corrigé/refusé/nouveau) | commit | preuve (test ou commande)`.
- À la fin de chaque phase, écris `docs/phase-reports/PHASE-N.md` (modèle en annexe).

### 1.6 Quand t'arrêter et demander
Arrête-toi **et pose la question à l'utilisateur** (sans deviner) si :
- une porte de validation reste rouge après 3 tentatives de correction distinctes ;
- une décision change le périmètre produit ou nécessite de la donnée réelle (ex. vrais formats de SMS Orange/MTN) ;
- une migration risque de détruire des données utilisateur existantes ;
- un package nécessaire est incompatible avec la version cible, sans alternative évidente ;
- tu as besoin d'une action qui exige les droits administrateur ou un appareil physique.

Sinon, **enchaîne les phases sans attendre** dès qu'une porte de validation est verte.

---

## 2. PHASES

Chaque phase suit le même cycle : **Lire → Tester d'abord → Implémenter → Porte de validation → Documenter → Commit/merge local → Phase suivante.**

### PHASE 0 : Base de référence (aucun changement fonctionnel)

Objectif : savoir exactement d'où on part.

1. `git status`, `git log --oneline -15`, vérifier que `develop` existe ; sinon la créer depuis `main`. Travaille depuis `develop`.
2. Vérifie les SDK : `dotnet --list-sdks`, `dotnet --list-runtimes`, `dotnet workload list`. Note si **.NET 10 SDK stable** est installé (le poste avait un .NET 10 preview).
3. Restaure, compile et teste : `dotnet restore`, `dotnet build SmartBudgetML.Tests -c Release`, `dotnet test SmartBudgetML.Tests -c Release --logger "console;verbosity=normal"`. Enregistre : nombre de tests (le rapport dit 53), nombre de warnings, durée.
4. Compte **les vrais tests par fichier** (les `[Fact]` + chaque `[InlineData]` des `[Theory]`) et compare au rapport : l'audit a mesuré 5 (base) / 10 (auth) / 5 (prévision) / 4+8 (catégoriseur) / 8+ (UI) par attributs. Documente la vérité.
5. Parcours le dépôt et **vérifie chacun des constats A1 à A19** dans le code. Pour chacun : confirmé / infirmé / partiel, avec fichier et ligne.
6. Crée `docs/AUDIT_REGISTER.md` (tableau décrit en §1.5) et `docs/phase-reports/PHASE-0.md`.
7. Mesure la **taille de la base de départ** : combien de tests, de lignes de code (cloc ou `Get-ChildItem | Measure-Object`).

**Porte de validation P0 :** `dotnet test` vert, registre d'audit créé, aucun fichier de code modifié (`git diff --stat` ne montre que `docs/`).

---

### PHASE 1 : Rendre l'application honnête (corrections A2–A7, A13, A17)

Branche : `feature/honesty-fixes`. **Écris les tests avant.**

1. **A2/A3, métriques et seuil réels**
   - Crée `IModelEvaluator` (ou équivalent) qui calcule **accuracy et macro-F1** sur un jeu de validation (voir Phase 4 pour le jeu). Settings affiche **ces valeurs réelles**, ou « Non évalué » tant qu'aucune évaluation n'a eu lieu. Supprime toute constante de précision.
   - Persiste `MlConfidenceThreshold` dans `AppSettings` (nouveau champ + migration de schéma SQLite sans perte de données). `AddExpense` lit ce seuil via un service (`IAppSettingsProvider`), plus de `0.70f` en dur. Teste : changer le seuil change la décision d'auto-sélection.
2. **A4, export CSV réel**
   - Crée `IFileExportService` (implémentation MAUI : écrire dans le cache puis `Share.Default.RequestAsync` / `FileSaver` ; implémentation de test : en mémoire).
   - Génère un CSV **correct** : échappement RFC 4180 (guillemets, virgules, retours à la ligne), UTF-8 avec BOM pour Excel, en-têtes, dates ISO, catégorie incluse (pas seulement l'ID), montants avec point décimal invariant. Teste avec des descriptions contenant `"`, `,`, `\n`, accents.
   - Le message « Exporté » ne s'affiche **que si** le fichier existe réellement.
3. **A5, sauvegarde réelle**
   - Copie cohérente de la base (utilise `VACUUM INTO` ou l'API de backup SQLite, pas une simple copie de fichier ouverte). Fichier horodaté, partagé via `IFileExportService`. Ajoute la **restauration** (import d'une sauvegarde avec validation de l'en-tête et confirmation). Teste : sauvegarder, modifier, restaurer, retrouver les données d'origine.
4. **A6, taille réelle** de la base : lis la taille du fichier (`FileInfo.Length`), formatée Ko/Mo.
5. **A7, persistance des retours utilisateur** : nouvelle table `CategoryFeedback` (description normalisée, catégorie, date, count). `RetrainWithFeedbackAsync` écrit en base. Au démarrage, le catégoriseur relit cette table. Teste : redémarrage simulé (nouvelle instance) conserve les corrections.
6. **A13, formatage monétaire** : un seul `ICurrencyFormatter` basé sur la devise du profil (FCFA/XAF sans décimales, espaces insécables en séparateur de milliers, ex. `15 000 FCFA`), utilisé partout, y compris dans `BudgetForecastingService`. Plus de `{:C0}`. Teste XAF, EUR, USD, culture fr-FR et fr-CM.
7. **A17, supprime tous les `catch { }` vides** : remplace par gestion explicite + `ILogger` + message utilisateur. Liste les occurrences trouvées dans le rapport de phase.

**Porte de validation P1 :** tous les tests verts ; `Select-String` sur le dépôt ne trouve **plus** `95.8`, `"180 KB"`, ni `catch\s*{\s*}` ; chaque item A2-A7/A13/A17 a un test nommé dans le registre.

---

### PHASE 2 : Migration vers .NET 10 LTS (A14, A15, A16 partiel)

Branche : `feature/dotnet10`. **Fais cette migration avant la sécurité et le ML** pour ne pas refaire le travail deux fois.

1. Vérifie qu'un **SDK .NET 10 stable** (pas preview) est installé. Sinon **STOP** (§1.6) : explique à l'utilisateur comment l'installer.
2. Mets à jour `global.json` (SDK 10, `allowPrerelease: false`), `TargetFrameworks` (`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`), les packages MAUI, `Microsoft.Extensions.Logging.Debug`, le projet de tests (`net10.0`), la CI (`DOTNET_VERSION`).
3. **Android uniquement (décision validée)** : retire `ios`, `maccatalyst`, `tizen` du `.csproj`, supprime `Platforms/iOS`, `Platforms/MacCatalyst`, `Platforms/Tizen`, dans un commit dédié (`chore: drop non-Android targets`). Garde Windows seulement comme cible de développement si elle compile ; sinon retire-la. Mets à jour la CI en conséquence. `SupportedOSPlatformVersion` Android : choisis une version minimale cohérente avec le parc Redmi courant (vérifie les exigences actuelles du Play Store pour le `targetSdk` : **cherche l'information à jour, ne te fie pas à ta mémoire**, et note la source).
4. Remplace `Plugin.Fingerprint 3.0.0-beta.1` : cherche (`dotnet package search`) des alternatives **stables et maintenues** compatibles .NET 10 MAUI pour la biométrie (ou l'API native via `BiometricPrompt`/`LocalAuthentication` derrière `IBiometricService`). Compare au moins 2 options, **choisis avec justification écrite** dans le rapport. Ne choisis pas un package dont tu n'as pas vérifié qu'il existe et qu'il est compatible.
5. Corrige les warnings de dépréciation introduits. Vérifie qu'aucune propriété du type `CheckEolTargetFramework=false` n'est nécessaire.
6. Restaure, compile, teste. Si le workload Android est installable sans droit admin, compile `net10.0-android` ; sinon la CI le fera et tu le notes.

**Porte de validation P2 :** `dotnet test` vert sur net10 ; aucun avertissement `NETSDK1202` ; `grep` ne trouve plus `net8.0` ni `beta` dans les `.csproj` ; décision biométrie documentée.

---

### PHASE 3 : Sécurité réelle (A1, A8, A9)

Branche : `feature/security-hardening`.

1. **Chiffrement de la base (A1)**
   - Passe à **SQLCipher** : évalue `sqlite-net-sqlcipher` + `SQLitePCLRaw.bundle_e_sqlcipher` (ou équivalent maintenu), **vérifie la compatibilité .NET 10/Android** avant de t'engager. Documente le choix.
   - La clé de chiffrement est **générée aléatoirement (32 octets, CSPRNG)** à la première ouverture et stockée via `ISecureKeyStore` (MAUI `SecureStorage`), **jamais** dérivée du PIN à 4 chiffres et **jamais** en dur ni en base.
   - Migration : si une base **non chiffrée** existe, la convertir (`sqlcipher_export` ou export/réimport) **avec sauvegarde préalable** et vérification du nombre de lignes avant/après. Si la conversion échoue, ne détruis rien (§1.6).
   - Tests (hôte) : une base créée avec clé A ne s'ouvre pas avec clé B ni sans clé ; migration d'une base claire fixture → chiffrée conserve toutes les données. (Si SQLCipher natif ne peut pas tourner sur l'hôte de test, isole-le derrière une interface et teste la logique de migration avec des fakes, puis étiquette `NON-VÉRIFIÉ-APPAREIL`.)
2. **Limitation des tentatives de PIN (A9)**
   - Compteur d'échecs **persisté** (pas en mémoire seulement) avec **délai exponentiel** (ex. 5 échecs → 30 s, 6 → 1 min, 7 → 5 min, 8+ → 15 min), horloge monotone/UTC robuste (gère le changement d'heure de l'appareil : si l'horloge recule, garde le verrou). Remise à zéro après succès.
   - Option **PIN à 6 chiffres** (par défaut pour les nouveaux comptes, 4 accepté pour compatibilité). Rejette les PIN triviaux (`0000`, `1234`, `1111`…).
   - Augmente les itérations PBKDF2 à **un niveau actuel** (≥ 600 000 pour SHA-256 selon les recommandations OWASP) avec **versionnage du hash** (champ `PinHashVersion`) pour rehacher à la connexion suivante les anciens PIN. Teste la migration de version.
   - Tests : séquence d'échecs → verrou → temps simulé via `ISystemClock`/`TimeProvider` → déverrouillage ; le verrou survit à une nouvelle instance du service.
3. **Verrouillage automatique (A8)**
   - Gère le cycle de vie dans `App.xaml.cs`/`Window` (`Stopped`/`Deactivated`/`Resumed`) : à la mise en veille ou en arrière-plan, mémorise l'heure ; au retour, si délai > `AutoLockTimeoutSeconds`, verrouille et redirige vers `/unlock`. Réglage utilisateur : immédiat / 30 s / 1 min / 5 min.
   - Logique de décision **pure et testée** (`AutoLockPolicy.ShouldLock(lastBackgroundAt, now, timeout)`), le branchement MAUI reste fin.
   - Android : masque l'aperçu dans le sélecteur d'applications (`FLAG_SECURE`) via une option « Protéger l'écran », et désactive les captures si l'option est activée.
4. **Hygiène** : plus aucune donnée financière dans les logs ; `android:allowBackup="false"` dans le manifeste (ou règles de sauvegarde explicites) pour éviter l'extraction de la base par sauvegarde ADB ; purge sécurisée réelle (supprime le fichier de base **et** la clé) avec confirmation à deux étapes.

**Porte de validation P3 :** tests de verrouillage, de migration de hash et de chiffrement verts ; le registre marque A1/A8/A9 « corrigé » avec preuves, et ce qui dépend de l'appareil est étiqueté `NON-VÉRIFIÉ-APPAREIL`.

---

### PHASE 4 : Moteur de catégorisation et de prévision (A10, A11, A19) : validation par les tests

Branche : `feature/ml-hybrid`. C'est ici que s'applique la contrainte « on ne peut pas installer l'app » : **toute la preuve se fait par tests sur l'hôte.**

#### 4.1 Architecture hybride (le ML n'est plus le seul étage)

Remplace le catégoriseur « ML seulement » par un pipeline à **3 étages**, du plus fiable au moins fiable :
1. **Mémoire utilisateur** : correspondance exacte ou normalisée avec les choix passés (table `CategoryFeedback`, Phase 1). Si la même description a déjà été classée par l'utilisateur, on utilise sa catégorie avec confiance 1,0.
2. **Règles de mots-clés** : dictionnaire par catégorie (français, anglais, pidgin, marques et services camerounais), après normalisation (minuscules, suppression des accents, ponctuation, chiffres de montants, mots vides).
3. **ML.NET (secours)** : classifieur texte entraîné sur le jeu de départ **plus** les retours utilisateur. Sa confiance doit dépasser le **seuil utilisateur** (Phase 1) pour être proposé ; sinon on ne propose rien (ou « Autre »).

L'API `Predict` renvoie : catégorie, confiance, **source** (`UserMemory` / `Rules` / `Model`). L'UI peut ainsi expliquer « Suggestion : Transport (règle : "moto") ».

#### 4.2 Règles d'or sur ML.NET (corrige A10)
- `Predict()` est **synchrone, rapide, et ne déclenche JAMAIS d'entraînement**. Si le modèle n'est pas prêt, il retourne seulement les étages 1 et 2.
- **Persiste le modèle** (`mlContext.Model.Save` vers `FileSystem.AppDataDirectory`, derrière `IModelStore`). Au démarrage : charge le modèle sauvegardé si sa **version de jeu de données/schéma** correspond ; sinon réentraîne **en arrière-plan** (`Task.Run`, jamais sur le thread UI) avec un indicateur de progression.
- **Ne réentraîne plus à chaque dépense.** Déclencheurs : N retours accumulés (par défaut 15) **ou** action manuelle, avec limitation de fréquence et annulation (`CancellationToken`). Pendant l'entraînement, l'ancien modèle continue de servir.
- `PredictionEngine` n'est pas thread-safe : garde le verrou existant ou utilise un pool ; teste l'appel concurrent.
- **Débounce** de la saisie côté Razor (≈300 ms) pour ne pas prédire à chaque frappe.

#### 4.3 Données d'entraînement camerounaises (A11)
- Remplace `SeedTrainingData` par un **jeu synthétique camerounais** d'au moins **~25 exemples par catégorie** minimum (vise 40+), en français, avec un peu de pidgin et de formulations de saisie rapide (« taxi », « moto », « kia taxi », « credit 1000 », « data mtn », « marche mokolo », « bouffe », « cotisation njangi », « ENEO facture », « CAMWATER », « transfert OM maman », « frais scolaires », « loyer », « pharmacie », « Total carburant »...). **Marque clairement ces données comme synthétiques** (commentaire et métadonnées).
- N'écris **aucun** vrai numéro de téléphone, nom de personne ou donnée personnelle.
- Stocke le jeu dans un **fichier de ressources versionné** (`Resources/Raw/seed_fr.csv` ou JSON) plutôt que dans du C en dur, pour pouvoir l'enrichir sans recompiler la logique.

#### 4.4 Évaluation réelle (remplace la fausse précision)
- Constitue **deux jeux distincts** : `train` et `holdout`. Le `holdout` doit être **rédigé séparément** (≥ 150 phrases, mélange de cas faciles, ambigus, fautes d'orthographe, abréviations) et **ne jamais être utilisé pour l'entraînement**. Évite la fuite de données (aucune phrase du holdout, même à un mot près, dans `train`) : écris un test qui le vérifie.
- Écris un test d'évaluation qui calcule **accuracy, macro-F1, matrice de confusion** pour : (a) règles seules, (b) ML seul, (c) pipeline hybride. **Affiche les résultats** dans la sortie du test et copie-les dans `docs/ML_EVALUATION.md`.
- Pose des **seuils de non-régression** réalistes calculés à partir de **tes mesures réelles** (ex. `hybride ≥ valeur_mesurée − marge`), pas des chiffres rêvés. Si l'hybride ne bat pas les règles seules, **dis-le** et recommande la simplification (retirer ML.NET pour alléger l'APK est une conclusion légitime : documente-la avec les chiffres).
- Tests de **performance** : temps d'inférence moyen et p95 sur 1 000 prédictions, temps d'entraînement complet, **taille du fichier modèle** sur disque, **mémoire** approximative. Affiche et documente. Ce sont des mesures **sur l'hôte** : écris-le (l'ARM Android sera plus lent).
- Tests de **robustesse** : chaîne vide, 10 000 caractères, emojis, caractères non latins, `null`, descriptions avec seulement des chiffres.
- Test de **sérialisation** : modèle sauvegardé puis rechargé donne les mêmes prédictions.

#### 4.5 Prévision de budget (A19, A13)
- Conserve l'algorithme actuel (régression + run-rate pondéré) **mais** : (a) messages localisés et formatés via `ICurrencyFormatter` ; (b) gestion des **revenus irréguliers** : le budget peut être défini **par mois, par semaine ou par « enveloppes »** ; (c) ajoute un mode **« dépenses fixes connues »** (loyer, scolarité) qui sont projetées à leur date réelle plutôt que linéairement ; (d) étends les tests de prévision (début/fin de mois, février bissextile, aucune dépense, une seule dépense énorme, dépense future, budget nul, devise sans décimales).
- Documente honnêtement les **limites** (une projection linéaire sur peu de jours est peu fiable) et affiche un indice de fiabilité (ex. « peu de données ») quand `jours < 7`.

**Porte de validation P4 :** `docs/ML_EVALUATION.md` contient les **vraies** métriques (règles / ML / hybride) ; tests de non-régression, de perf, de robustesse et de sérialisation verts ; `grep` ne trouve plus `GetAwaiter().GetResult()` dans le code ; la décision « garder ML.NET ou non » est écrite avec chiffres.

---

### PHASE 5 : Localisation et adaptation au Cameroun (A12)

Branche : `feature/cameroon-localization`.

1. **Localisation (FR par défaut, EN en second, décision validée)** : `IStringLocalizer` + fichiers `.resx` (`fr` par défaut, `en` en second). Détecte la langue du téléphone (si ni `fr` ni `en`, prends `fr`), avec réglage manuel dans l'app. Le **jeu de données ML**, les **mots-clés** et les **catégories** existent en FR et en EN (le pidgin reste dans les mots-clés FR). **Aucune chaîne en dur** dans les `.razor`, les services (messages de prévision) et le seed. Teste qu'il n'y a pas de clé manquante entre `fr` et `en` (test qui compare les deux fichiers).
2. **Devise** : défaut **XAF / FCFA** ; formatage `12 500 FCFA`. Conserve EUR, USD, GBP, MAD.
3. **Catégories camerounaises** (noms localisés, icônes, couleurs, plafonds par défaut en FCFA réalistes mais **modifiables** et clairement présentés comme « suggestions ») : Alimentation/Marché, Transport (taxi, moto), Logement/Loyer, Électricité/Eau (ENEO, CAMWATER), Crédit & Data, Santé, Scolarité, Tontine/Njangi, Famille & Solidarité (envois au village, aide), Événements (deuil, mariage), Église/Dons, Loisirs, Épargne, Autres ; revenus : Salaire, Business/Débrouille, Transferts reçus.
4. **Migration des données** : les utilisateurs existants gardent leurs catégories (identifiées par une clé stable, pas par le nom affiché). Teste la migration.
5. **Accessibilité et appareils modestes** : cibles tactiles ≥ 48 dp, contraste vérifié, tailles de police respectant le réglage système, pas d'animations lourdes si « réduire les animations » est activé.
6. **Poids de l'application (Redmi 3 Go, Play Store)** : mesure et documente la taille d'un **AAB et de l'APK Release par architecture** avec la CI (artefact), active le **trimming** / compression si compatible (`PublishTrimmed`, `AndroidLinkMode`, `AndroidEnableProfiledAot`, `AndroidUseAapt2`, découpage par ABI, etc. : **vérifie la compatibilité de chaque option avec Blazor, SQLCipher et ML.NET par un build et des tests, sans supposer**) avec un test de non-régression de build. Cible : voir « Budgets de performance » (< 40 Mo, haute 60 Mo). Si impossible, explique pourquoi avec les chiffres et propose les options (retirer ML.NET, supprimer Bootstrap inutilisé : `wwwroot/css/bootstrap` pèse ~600 Ko et n'est probablement pas nécessaire, etc.).

**Porte de validation P5 :** tests de parité `fr`/`en` verts ; grep ne trouve plus de texte anglais en dur dans les `.razor` ; devise par défaut XAF testée ; rapport de taille APK produit par la CI (ou noté « à mesurer » si la CI n'a pas pu tourner).

---

### PHASE 6 : Fonctionnalités à valeur locale

Branche : `feature/local-value`. Trois blocs, par ordre de priorité.

#### 6.1 Import de SMS Mobile Money (priorité 1)
**ATTENTION : tu ne dois PAS inventer de formats de SMS.** Les vrais formats Orange Money / MTN MoMo changent et ne sont pas dans ta mémoire de façon fiable.
1. Conçois une **architecture de parseurs extensible** : `IMobileMoneySmsParser` (une implémentation par opérateur, chaque parseur déclare les motifs qu'il reconnaît), un `SmsParserRegistry`, un modèle `ParsedMobileMoneyTransaction` (type : dépôt/retrait/envoi/réception/paiement/achat de crédit/frais ; montant, frais, solde, contrepartie, référence, date).
2. **STOP OBLIGATOIRE : demande les vrais SMS à l'utilisateur AVANT d'écrire la moindre expression régulière d'opérateur.** Construis d'abord seulement l'architecture (interfaces, registre, modèle, UI de collage, dédoublonnage, tests de l'infrastructure). Puis affiche à l'utilisateur ce message, tel quel, et attends sa réponse :

   > « Je suis arrivé à l'import des SMS Mobile Money. Pour écrire des parseurs fiables, j'ai besoin de **10 à 20 vrais SMS par opérateur** (Orange Money, MTN MoMo), couvrant : envoi, réception, retrait, dépôt, paiement marchand, achat de crédit/data, et un SMS avec frais. **Remplace** les numéros de téléphone, les noms de personnes et les références complètes par des valeurs factices (ex. `6XXXXXXXX`, `NOM`), mais **garde exactement** le format, la ponctuation, les mots, les séparateurs de montants, les dates et l'ordre des champs. Colle-les ici, un SMS par bloc, avec l'opérateur et le type d'opération si tu le connais. »

   Quand il les fournit : enregistre-les dans `SmartBudgetML.Tests/Fixtures/sms/<operateur>.txt` (anonymisés), écris les parseurs **à partir de ces échantillons uniquement**, et fais un test par échantillon. Si un SMS ne correspond à aucun motif, **dis-le** au lieu de forcer un motif. Tant que les échantillons ne sont pas reçus, les tests de parseur d'opérateur sont en `Skip` avec la raison « en attente d'échantillons réels », et le statut du parseur reste `NON IMPLÉMENTÉ`. Ne prétends jamais qu'il fonctionne en vrai.
3. **Mode 1 (sans permission sensible, à livrer en premier)** : l'utilisateur **colle** un SMS ou **partage** le texte vers l'application ; l'app l'analyse et propose la dépense/revenu à valider (jamais d'ajout silencieux). Dédoublonnage par référence de transaction.
4. **Lecture automatique des SMS : EXCLUE (décision validée, publication sur Google Play).** N'ajoute **aucune** permission `READ_SMS`, `RECEIVE_SMS`, ni de service de lecture de notifications. Écris `docs/DECISION_SMS.md` qui consigne la décision et sa justification (politique Google Play sur les permissions SMS : **vérifie la formulation à jour de la politique et cite la source**, ne cite pas de mémoire). Fais en sorte que l'expérience « coller / partager » soit **la plus fluide possible** :
   - cible de partage Android (`ACTION_SEND` texte) pour envoyer un SMS depuis l'application Messages vers SmartBudget ;
   - détection **à l'ouverture** de l'app d'un texte ressemblant à un SMS Mobile Money dans le presse-papiers, avec une **proposition explicite** (jamais d'ajout automatique, jamais de lecture silencieuse du presse-papiers en arrière-plan) ;
   - écran de validation en un geste (montant, catégorie suggérée, confirmer/modifier) ;
   - import par lot : coller plusieurs SMS d'un coup.
5. Tests : parsing, montants avec séparateurs (`1.500`, `1 500`, `1,500`), frais, dédoublonnage, SMS non reconnu (ne plante pas, retourne « non reconnu »).

#### 6.2 Tontines / njangi (priorité 2)
- Modèle : `Tontine` (nom, montant de cotisation, fréquence, nombre de membres, ordre de passage, date de début), `TontineMember`, `TontineContribution`, `TontineRound`. Calculs purs et testés : prochain bénéficiaire, montant à recevoir, retards de cotisation, solde net de l'utilisateur dans la tontine (ce qu'il a payé vs reçu), intégration au budget (la cotisation est une dépense planifiée, le « pot » reçu est un revenu exceptionnel qui ne fausse pas la prévision de dépenses).
- UI simple : liste des tontines, calendrier des tours, bouton « j'ai cotisé », rappel local (notification locale, derrière interface).
- Tests : tontine de 5 membres sur 5 tours, cas de membre qui quitte, cotisation en retard, fréquence hebdomadaire/mensuelle, passage de fin d'année.

#### 6.3 Revenus irréguliers et solidarité (priorité 3)
- `IncomeProfile` : `Fixed` / `Irregular` / `Mixed`. En mode irrégulier, le budget se calcule sur une **moyenne glissante** des revenus et propose un **« minimum vital »** (dépenses incompressibles) plus des enveloppes hebdomadaires.
- Suivi « Famille & solidarité » : objectifs mensuels d'envoi, historique, part du revenu.
- Tests de la moyenne glissante, des mois à zéro revenu, et de l'enveloppe hebdomadaire.

**Porte de validation P6 :** tests verts ; `docs/DECISION_SMS.md` écrit ; **les vrais SMS ont été demandés à l'utilisateur** (message exact du §6.1) et les parseurs d'opérateur sont soit écrits à partir de ses échantillons avec un test par échantillon, soit marqués `NON IMPLÉMENTÉ` avec tests en `Skip` « en attente d'échantillons réels » ; aucune permission SMS dans le manifeste (vérifie avec `Select-String` sur `AndroidManifest.xml`) ; liste claire de ce qui attend l'utilisateur. Si l'utilisateur n'a pas encore répondu, **ne bloque pas les autres phases** : termine 6.2 et 6.3, passe à la Phase 7, et rappelle la demande dans le rapport final.

---

### PHASE 7 : Qualité, CI/CD et documentation finale (A16, A18)

Branche : `feature/quality-ci-docs`.

1. **CI GitHub Actions** : déclenche sur `push` (develop, main, feature/**) **et** `pull_request`. Jobs : `test` (Ubuntu, avec résumé des tests et **rapport de couverture** via `coverlet`/`reportgenerator`, seuil minimal documenté), `build-android` (Windows ou Ubuntu avec workload, publie l'APK en artefact **et écrit sa taille dans le résumé du job**), cache NuGet. Ajoute un job d'analyse (`dotnet format --verify-no-changes`, analyseurs, `dotnet list package --vulnerable --include-transitive` et `--outdated`).
2. **Job de test sur émulateur Android (OBLIGATOIRE, décision validée).** Ajoute à la CI un job d'émulateur (par ex. `reactivecircus/android-emulator-runner`, **vérifie la version et la disponibilité avant de l'utiliser** ; si l'accélération matérielle n'est pas disponible sur le runner, documente-le et choisis une alternative) avec un profil proche du **Redmi 3 Go de RAM** (`ram` = 3072 Mo, écran ~720x1600, API cible raisonnable). Le test de fumée doit : installer l'APK de test, démarrer l'app, atteindre l'écran de déverrouillage, créer un compte (onboarding), ouvrir la base **chiffrée**, ajouter une dépense, obtenir une **prédiction ML.NET** réelle et la valider, relever le **temps de démarrage à froid** et la **mémoire** (`dotnet-counters` n'est pas disponible ici : utilise `adb shell dumpsys meminfo` et `am start -W`), puis **publier ces mesures dans le résumé du job**. C'est la **seule façon propre de prouver ML.NET sur Android sans téléphone**. Les statuts `NON-VÉRIFIÉ-APPAREIL` passent à `VÉRIFIÉ-ÉMULATEUR` si ce test les couvre, sans jamais être présentés comme équivalents à un vrai téléphone. Si le job ne parvient pas à tourner après 3 tentatives distinctes, STOP et explique pourquoi (§1.6) plutôt que de le désactiver silencieusement.
3. **Couverture de tests** : cible ≥ 70 % sur `Services/` ; comble les trous avec des tests utiles (pas de tests triviaux pour gonfler le chiffre).
4. **Réécris le rapport de projet** (`RAPPORT_PROJET.md`) pour qu'il soit **exact** : supprime/corrige toutes les affirmations de l'audit (A1–A19), mets les vrais chiffres de tests, la vraie description CI, la vraie sécurité (SQLCipher, tentatives limitées), les vraies métriques ML (de `ML_EVALUATION.md`), ajoute une section **« Limites connues »** et un **tableau de statut des fonctionnalités** (`VÉRIFIÉ-TESTS` / `VÉRIFIÉ-BUILD` / `NON-VÉRIFIÉ-APPAREIL`). Réécris la section post-mortem avec les vrais problèmes de cette migration, y compris ceux de l'audit.
5. **README** : installation, commandes, architecture (diagramme à jour), comment lancer les tests, comment contribuer, comment fournir de vrais SMS anonymisés.
6. **Checklist « premier test sur téléphone »** (`docs/DEVICE_TEST_CHECKLIST.md`) : liste précise de ce que l'utilisateur doit vérifier sur un vrai Android (installation, taille, démarrage < X s, saisie, ML, biométrie, verrouillage, SQLCipher, export/partage, mode avion, 2 Go de RAM…) avec une case par point et une colonne « résultat » à remplir. Elle est adaptée au **Redmi 3 Go de RAM** (mesures de démarrage à froid, mémoire, fluidité de saisie, chauffe, autonomie sur 30 min d'usage, comportement en mode avion, avec d'autres applications ouvertes en arrière-plan, avec peu d'espace de stockage libre, rotation d'écran, retour d'arrière-plan et verrouillage). C'est ce qui permettra de passer les statuts `NON-VÉRIFIÉ-APPAREIL` à `VÉRIFIÉ`.
7. **Préparation Google Play (décision validée : publication sur le Play Store).** Livrables dans `docs/play-store/` :
   - **Format** : configure la publication en **AAB** (`AndroidPackageFormat=aab`), `ApplicationId` définitif, `ApplicationVersion` (versionCode) incrémenté par la CI, signature : **le fichier de clé (keystore) ne doit JAMAIS être commité** ; la CI le lit depuis des **secrets GitHub** ; documente la génération de la clé de téléversement et la sauvegarde de la clé (perdre la clé = perdre l'app). Active **Play App Signing** dans la doc.
   - **Conformité** : `targetSdk` conforme à l'exigence **actuelle** de Google Play (cherche l'information à jour, cite la source), architectures 64 bits, **permissions minimales** (liste exhaustive des permissions demandées avec la justification de chacune ; supprime tout ce qui n'est pas indispensable), aucune permission SMS/contacts/localisation.
   - **Documents** : brouillon de **politique de confidentialité** (FR et EN) fidèle au comportement réel (données 100 % locales, chiffrées ; aucune donnée envoyée ; ce que fait le partage/export de fichier lancé par l'utilisateur), **réponses suggérées au formulaire « Sécurité des données »**, fiche de présentation (titre, description courte/longue FR+EN, mots-clés, catégorie « Finance »), liste des captures d'écran à produire, classification de contenu. Rappelle que l'app **ne doit pas se présenter comme un conseil financier ni un service bancaire** : ajoute une mention claire dans l'app et la fiche.
   - **Mise à jour in-app** et **retours utilisateur** : prévois un écran « À propos » (version, licences open source utilisées, lien de confidentialité, contact).
   - Ne publie **rien** et ne crée aucun compte : tu prépares seulement les fichiers. L'utilisateur fait la publication.

**Porte de validation P7 :** CI verte (ou expliquée si elle ne peut pas tourner localement) ; rapport réécrit sans affirmation non prouvée ; registre d'audit : **chaque ligne A1–A19 a un statut final et une preuve**.

---

## 3. LIVRABLES FINAUX

À la fin, remets à l'utilisateur :
1. Les branches de phase fusionnées localement dans `develop` (pas de push).
2. `docs/AUDIT_REGISTER.md`, `docs/ML_EVALUATION.md`, `docs/DECISION_SMS.md`, `docs/DEVICE_TEST_CHECKLIST.md` (version Redmi 3 Go), `docs/play-store/` (confidentialité FR/EN, sécurité des données, fiche, guide de signature AAB), `docs/phase-reports/PHASE-0…7.md`, `README.md`, `RAPPORT_PROJET.md` mis à jour.
3. Un **résumé final** : ce qui est corrigé, ce qui reste ouvert, ce qui dépend de l'utilisateur (vrais SMS, test téléphone, décisions), et la **liste des commandes** pour tout vérifier lui-même.

## 4. ANNEXE : MODÈLE DE RAPPORT DE PHASE (`docs/phase-reports/PHASE-N.md`)

```
# Phase N : <titre>
Branche : feature/... | Commits : <liste courte> | Date : ...
## Objectif
## Changements réalisés (par item, avec fichiers)
## Tests ajoutés (noms) et résultat global : X réussis / Y total
## Preuves (sorties de commandes résumées)
## Décisions prises et justifications
## Écarts par rapport à la spec / constats nouveaux
## Statut des fonctionnalités touchées (VÉRIFIÉ-TESTS / VÉRIFIÉ-BUILD / NON-VÉRIFIÉ-APPAREIL)
## Ce qui reste ouvert / questions pour l'utilisateur
## Porte de validation : ✅/❌ (détail)
```

---

## 5. PAR OÙ COMMENCER

Commence **maintenant** par la Phase 0. Annonce en une phrase ce que tu fais, crée la liste de tâches (une par phase), puis exécute. N'attends pas de validation entre deux phases si la porte est verte ; arrête-toi uniquement dans les cas du §1.6.
