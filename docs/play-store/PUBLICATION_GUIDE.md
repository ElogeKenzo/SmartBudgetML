# Guide de Signature et Publication Google Play — SmartBudget ML v2.0

## 1. Format de Livrable : Android App Bundle (`.aab`)
Google Play exige le format **Android App Bundle (`.aab`)** pour toutes les nouvelles applications :
```powershell
dotnet publish SmartBudgetML/SmartBudgetML.csproj `
  -c Release `
  -f net10.0-android `
  -p:AndroidPackageFormat=aab
```

## 2. Création et Sécurisation du Keystore de Signature
> **Règle de sécurité absolue :** Ne commitez **jamais** un fichier `.keystore` ou `.jks` dans le dépôt Git (`*.keystore` et `*.jks` doivent rester hors de contrôle de version).

### 2.1 Générer la clé de téléversement (Upload Key)
```bash
keytool -genkeypair -v \
  -keystore smartbudgetml-upload.keystore \
  -alias smartbudgetml-upload \
  -keyalg RSA -keysize 2048 -validity 10000
```

### 2.2 Configurer les Secrets GitHub Actions
Dans *GitHub → Settings → Secrets and variables → Actions*, créez les 4 secrets suivants :
- `ANDROID_KEYSTORE_BASE64` : contenu du fichier `.keystore` encodé en Base64 (`[Convert]::ToBase64String([IO.File]::ReadAllBytes("smartbudgetml-upload.keystore"))`).
- `ANDROID_KEYSTORE_PASSWORD` : mot de passe du keystore.
- `ANDROID_KEY_ALIAS` : `smartbudgetml-upload`.
- `ANDROID_KEY_PASSWORD` : mot de passe de la clé.

### 2.3 Signature en CI/CD
Lors du build de release signé :
```powershell
dotnet publish SmartBudgetML/SmartBudgetML.csproj `
  -c Release `
  -f net10.0-android `
  -p:AndroidPackageFormat=aab `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=smartbudgetml-upload.keystore `
  -p:AndroidSigningKeyAlias=$env:ANDROID_KEY_ALIAS `
  -p:AndroidSigningStorePass=$env:ANDROID_KEYSTORE_PASSWORD `
  -p:AndroidSigningKeyPass=$env:ANDROID_KEY_PASSWORD
```

## 3. Activation de Play App Signing
1. Dans la **Google Play Console**, créez l'application **SmartBudget ML** (langue par défaut : `Français (fr-FR)`, catégorie : `Finance`).
2. Activez **Play App Signing** : Google gère la clé de signature finale distribuée aux téléphones, tandis que votre clé sert uniquement de *clé d'envoi (Upload Key)* révocable en cas de compromission.

## 4. Conformité `targetSdkVersion` et Permissions Minimales
- **Target SDK :** .NET 10 MAUI cible l'API Android conforme aux exigences Google Play en vigueur (API 35+ supporté par le SDK .NET 10 Android, `minSdkVersion = 24`).
- **Justification des permissions dans `AndroidManifest.xml` :**
  - `android.permission.USE_BIOMETRIC` : utilisée uniquement pour le déverrouillage local facultatif par empreinte digitale via l'API native `Android.Hardware.Biometrics.BiometricPrompt`.
  - **Aucune** permission `INTERNET`, `ACCESS_NETWORK_STATE`, `READ_SMS`, `RECEIVE_SMS`, `READ_CONTACTS` ou `ACCESS_FINE_LOCATION` n'est présente dans le manifeste.
