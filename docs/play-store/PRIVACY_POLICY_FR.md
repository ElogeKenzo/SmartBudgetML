# Politique de Confidentialité — SmartBudget ML (FR)

**Dernière mise à jour :** Octobre 2026  
**Application :** SmartBudget ML (`com.smartbudgetml.app`)

## 1. Principe Fondamental : 100 % Local et Hors-Ligne (« Privacy by Design »)
**SmartBudget ML** fonctionne exclusivement sur votre téléphone (**Edge Computing / Local-First**).  
L'application **ne demande même pas la permission `INTERNET`** au système Android : il lui est techniquement impossible de transmettre vos données financières vers un serveur externe, un service cloud ou un tiers.

## 2. Données Stockées sur Votre Appareil
Toutes les informations que vous saisissez restent uniquement sur votre appareil :
- Vos transactions (dépenses, revenus, descriptions, catégories, dates).
- Vos paramètres de tontines (Njangi), objectifs budgétaires et préférences de devise/langue.
- Votre profil local (nom ou pseudonyme, revenu mensuel indicatif).
- Les corrections de catégorisation utilisées pour entraîner localement le modèle d'intelligence artificielle sur votre téléphone.

## 3. Protection et Chiffrement des Données
- **Base de données chiffrée au repos :** Vos données sont stockées dans une base locale chiffrée en **AES-256 via SQLCipher** (`sqlite-net-sqlcipher`). La clé maître de 256 bits est générée par un générateur cryptographique sûr (CSPRNG) et conservée via `SecureStorage`, protégée par le Keystore Android lorsque l'appareil le permet. Elle n'est jamais écrite ailleurs : si le stockage sécurisé est indisponible, l'application affiche un écran d'erreur au lieu d'ouvrir les données.
- **Code PIN local :** Votre code PIN est haché localement via **PBKDF2-HMAC-SHA256 avec 600 000 itérations** et un sel aléatoire unique de 128 bits.
- **Biométrie :** Si vous activez le déverrouillage biométrique, la vérification de votre empreinte digitale est effectuée directement par le système d'exploitation Android (`BiometricPrompt`). SmartBudget ML n'a jamais accès à vos données biométriques brutes.
- **Import de SMS Mobile Money :** SmartBudget ML n'accède **jamais** automatiquement à vos SMS (`READ_SMS` n'est pas utilisée). Seuls les textes que vous choisissez explicitement de coller ou de partager dans l'application sont analysés en mémoire locale.

## 4. Export et Suppression de Vos Données
- **Export à votre initiative :** Depuis *Paramètres*, vous pouvez exporter vos transactions au format CSV ou créer une sauvegarde. L'application ouvre le menu de partage d'Android et c'est vous qui choisissez l'application de destination (Drive, Fichiers, messagerie…) : rien n'est envoyé sans votre choix, et le fichier quitte alors la protection de l'application. Le fichier CSV n'est pas chiffré.
- **Sauvegarde chiffrée par mot de passe :** La sauvegarde (`.sbbackup`) est chiffrée avec un mot de passe que vous choisissez (8 caractères minimum). Ce mot de passe n'est enregistré nulle part, ni sur le téléphone ni ailleurs : **s'il est perdu, la sauvegarde est illisible et personne ne peut la récupérer**. Elle peut être restaurée sur ce téléphone ou un autre depuis *Paramètres → Restaurer une sauvegarde* ; la restauration remplace toutes les données actuelles, après une double confirmation.
- **Droit à l'effacement immédiat :** Le bouton *« Effacer la base locale et la clé »* dans *Paramètres* supprime définitivement le fichier de base de données chiffré ainsi que la clé de chiffrement stockée sur votre téléphone.

## 5. Avertissement Légal (Absence de Conseil Financier)
SmartBudget ML est un outil personnel de suivi budgétaire et de prévision statistique indicative. **L'application ne fournit aucun conseil financier, fiscal, juridique ou d'investissement, et n'est affiliée à aucune banque ni à aucun opérateur Mobile Money (Orange Money, MTN MoMo).**
