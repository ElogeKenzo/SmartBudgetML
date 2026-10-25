# Privacy Policy — SmartBudget ML (EN)

**Last Updated:** October 2026  
**Application:** SmartBudget ML (`com.smartbudgetml.app`)

## 1. Core Principle: 100% On-Device & Offline ("Privacy by Design")
**SmartBudget ML** operates strictly on your device (**Edge Computing / Local-First**).  
The application **does not even request the `INTERNET` permission** from Android: it is technically incapable of transmitting your financial data to any external server, cloud service, analytics tracker, or third party.

## 2. Data Stored Locally on Your Device
All information you enter remains exclusively on your phone:
- Your transactions (expenses, income, descriptions, categories, dates).
- Your Tontine (Njangi) schedules, budget goals, and currency/language preferences.
- Your local profile (name or nickname, indicative monthly income).
- Category corrections used to train the on-device machine learning model locally on your phone.

## 3. Data Security & Encryption
- **Encryption at rest:** Your data is stored in a local database encrypted with **256-bit SQLCipher** (`sqlite-net-sqlcipher`). The 256-bit master key is generated via a cryptographic random number generator (CSPRNG) and stored via `SecureStorage`, protected by the Android Keystore when the device allows it. It is never written anywhere else: if secure storage is unavailable, the app shows an error screen instead of opening the data.
- **Local PIN:** Your PIN is hashed locally using **PBKDF2-HMAC-SHA256 with 600,000 iterations** and a unique 128-bit cryptographic salt.
- **Biometrics:** When enabled, fingerprint verification is handled exclusively by the Android OS (`BiometricPrompt`). SmartBudget ML never accesses raw biometric data.
- **Mobile Money SMS Import:** SmartBudget ML **never** reads your SMS inbox automatically (no `READ_SMS` permission). Only text messages you explicitly paste or share into the app are parsed locally in memory.

## 4. Data Export & Permanent Deletion
- **User-initiated export:** From *Settings* you can export your transactions to CSV or create a backup. The app opens the Android share sheet and you choose the destination app (Drive, Files, messaging…): nothing is sent unless you choose to, and the file then leaves the app's protection. The CSV file is not encrypted.
- **Password-encrypted backup:** The backup (`.sbbackup`) is encrypted with a password you choose (at least 8 characters). The password is not stored anywhere, on the phone or elsewhere: **if it is lost, the backup cannot be read and nobody can recover it**. It can be restored on this phone or another one from *Settings → Restore a backup*; restoring replaces all current data, after a double confirmation.
- **Immediate erasure:** Tapping *"Clear Local Database & Key"* in *Settings* permanently deletes both the encrypted SQLite file and its cryptographic master key from your device.

## 5. Legal Disclaimer (Not Financial Advice)
SmartBudget ML is a personal budgeting and statistical forecasting utility. **The application does not provide financial, tax, legal, or investment advice, and is not affiliated with any bank or Mobile Money operator (Orange Money, MTN MoMo).**
