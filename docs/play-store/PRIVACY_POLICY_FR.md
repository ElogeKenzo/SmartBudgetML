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
- **Base de données chiffrée au repos :** Vos données sont stockées dans une base locale chiffrée en **AES-256 via SQLCipher** (`sqlite-net-sqlcipher`). La clé maître de 256 bits est générée par un générateur cryptographique sûr (CSPRNG) et conservée dans le coffre sécurisé matériel d'Android (*Android Keystore* / `SecureStorage`).
- **Code PIN local :** Votre code PIN est haché localement via **PBKDF2-HMAC-SHA256 avec 600 000 itérations** et un sel aléatoire unique de 128 bits.
- **Biométrie :** Si vous activez le déverrouillage biométrique, la vérification de votre empreinte digitale est effectuée directement par le système d'exploitation Android (`BiometricPrompt`). SmartBudget ML n'a jamais accès à vos données biométriques brutes.
- **Import de SMS Mobile Money :** SmartBudget ML n'accède **jamais** automatiquement à vos SMS (`READ_SMS` n'est pas utilisée). Seuls les textes que vous choisissez explicitement de coller ou de partager dans l'application sont analysés en mémoire locale.

## 4. Export et Suppression de Vos Données
- **Export à votre initiative :** Vous pouvez exporter vos transactions au format CSV ou sauvegarder votre base de données locale à tout moment depuis l'écran *Paramètres*.
- **Droit à l'effacement immédiat :** Le bouton *« Effacer la base locale et la clé »* dans *Paramètres* supprime définitivement le fichier de base de données chiffré ainsi que la clé de chiffrement stockée sur votre téléphone.

## 5. Avertissement Légal (Absence de Conseil Financier)
SmartBudget ML est un outil personnel de suivi budgétaire et de prévision statistique indicative. **L'application ne fournit aucun conseil financier, fiscal, juridique ou d'investissement, et n'est affiliée à aucune banque ni à aucun opérateur Mobile Money (Orange Money, MTN MoMo).**
