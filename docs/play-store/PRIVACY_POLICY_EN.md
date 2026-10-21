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
- **Encryption at rest:** Your data is stored in a local database encrypted with **256-bit SQLCipher** (`sqlite-net-sqlcipher`). The 256-bit master key is generated via a cryptographic random number generator (CSPRNG) and stored in the hardware-backed *Android Keystore* (`SecureStorage`).
- **Local PIN:** Your PIN is hashed locally using **PBKDF2-HMAC-SHA256 with 600,000 iterations** and a unique 128-bit cryptographic salt.
- **Biometrics:** When enabled, fingerprint verification is handled exclusively by the Android OS (`BiometricPrompt`). SmartBudget ML never accesses raw biometric data.
- **Mobile Money SMS Import:** SmartBudget ML **never** reads your SMS inbox automatically (no `READ_SMS` permission). Only text messages you explicitly paste or share into the app are parsed locally in memory.

## 4. Data Export & Permanent Deletion
- **User-initiated export:** You can export your transactions to CSV or back up your encrypted database file at any time from the *Settings* screen.
- **Immediate erasure:** Tapping *"Clear Local Database & Key"* in *Settings* permanently deletes both the encrypted SQLite file and its cryptographic master key from your device.

## 5. Legal Disclaimer (Not Financial Advice)
SmartBudget ML is a personal budgeting and statistical forecasting utility. **The application does not provide financial, tax, legal, or investment advice, and is not affiliated with any bank or Mobile Money operator (Orange Money, MTN MoMo).**
