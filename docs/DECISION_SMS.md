# Décision Architecturale — Import de SMS Mobile Money (Phase 6.1)

## 1. Contexte et Problématique
Au Cameroun, **Orange Money** et **MTN Mobile Money (MoMo)** constituent le principal canal de paiement quotidien (envois et réceptions d'argent, retraits en kiosque, paiements marchands, factures ENEO/Camwater, achat de crédit et forfaits Internet).  
Chaque transaction génère un SMS de confirmation structuré envoyé par l'opérateur.

Deux approches techniques ont été évaluées pour importer ces transactions dans **SmartBudget ML** :
- **Option A :** Lecture automatique des SMS via les permissions Android `READ_SMS` et `RECEIVE_SMS` (`BroadcastReceiver` en arrière-plan).
- **Option B :** Import explicite à l'initiative de l'utilisateur par **Copier-Coller** (un SMS ou un lot de SMS) et **Partage Android (`Intent.ACTION_SEND` `text/plain`)** depuis l'application Messages vers SmartBudget ML.

---

## 2. Décision Retenue : Option B (Zéro permission `READ_SMS` / `RECEIVE_SMS`)

Nous retenons **exclusivement l'Option B** pour trois raisons fondamentales :

### 2.1 Conformité à la politique Google Play (Permissions SMS/Journal d'appels)
Depuis janvier 2019 (mise à jour de la politique Google Play sur les permissions sensibles `READ_SMS`, `RECEIVE_SMS`, `SEND_SMS`), **seules les applications définies comme gestionnaire SMS par défaut (`Default SMS Handler`)** ou relevant de rares exceptions matérielles strictement encadrées sont autorisées à demander `READ_SMS` ou `RECEIVE_SMS`.  
Une application de gestion de budget personnel demandant `READ_SMS` fait l'objet d'un **rejet automatique** sur Google Play.

### 2.2 Vie privée et confiance de l'utilisateur (Architecture Local-First)
Ne pas demander l'accès en lecture à l'intégralité de la boîte de réception SMS garantit techniquement à l'utilisateur que SmartBudget ML ne peut lire ni ses conversations privées ni ses codes OTP bancaires. Seuls les SMS que l'utilisateur choisit explicitement de coller ou de partager sont analysés en mémoire locale.

### 2.3 Portabilité et maintenabilité
Le mécanisme de collage en lot (`ISmsParserRegistry.ParseBatch`) permet à l'utilisateur de copier plusieurs SMS d'un coup depuis sa messagerie et de les prévisualiser avant validation, avec dédoublonnage automatique par référence de transaction (`Reference` / `TxID`) ou par fenêtre temporelle (`Date ±2 min`, `Amount`, `Operator`).

---

## 3. Architecture Implémentée (`SmartBudgetML.Services.Sms`)
- [`ParsedMobileMoneyTransaction`](../SmartBudgetML/Models/ParsedMobileMoneyTransaction.cs) : modèle normalisé (`Operator`, `Type`, `Amount`, `Fee`, `BalanceAfter`, `Counterparty`, `Reference`, `Date`, `IsIncome`).
- [`IMobileMoneySmsParser`](../SmartBudgetML/Services/Sms/IMobileMoneySmsParser.cs) : contrat par opérateur (`OperatorName`, `CanParse`, `TryParse`).
- [`ISmsParserRegistry`](../SmartBudgetML/Services/Sms/IMobileMoneySmsParser.cs) / [`SmsParserRegistry`](../SmartBudgetML/Services/Sms/SmsParserRegistry.cs) : registre multi-opérateurs, analyse par lot (`ParseBatch`) et détection de doublons (`IsDuplicate`).
- [`MobileMoneyAmountParser`](../SmartBudgetML/Services/Sms/SmsParserRegistry.cs) : normalisation des montants XAF/FCFA quels que soient les séparateurs de milliers (`1.500`, `1 500`, `1,500`, `1500 FCFA`).

---

## 4. État des Parseurs Opérateurs (Orange Money & MTN MoMo)
Conformément à la règle d'honnêteté technique (`CLAUDE.md` §6.1), **aucun format de SMS opérateur n'est inventé** :
- `OrangeMoneyCameroonParser` : **NON IMPLÉMENTÉ** (en attente d'échantillons réels anonymisés fournis par l'utilisateur).
- `MtnMomoCameroonParser` : **NON IMPLÉMENTÉ** (en attente d'échantillons réels anonymisés fournis par l'utilisateur).
