using SmartBudgetML.Models;
using SmartBudgetML.Services.Budget;
using SmartBudgetML.Services.Sms;
using SmartBudgetML.Services.Tontines;
using Xunit;

namespace SmartBudgetML.Tests.LocalValue;

public class Phase6LocalValueTests
{
    // =========================================================================
    // 6.1 Mobile Money SMS Parser Architecture & Deduplication Tests
    // =========================================================================

    [Theory]
    [InlineData("1.500", 1500)]
    [InlineData("1 500", 1500)]
    [InlineData("1,500", 1500)]
    [InlineData("1500 FCFA", 1500)]
    [InlineData("25 000 XAF", 25000)]
    [InlineData("150.000 FCFA", 150000)]
    public void MobileMoneyAmountParser_ShouldParseVariousXafSeparators(string rawAmount, decimal expected)
    {
        var ok = MobileMoneyAmountParser.TryParseAmountXaf(rawAmount, out var parsed);
        Assert.True(ok);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void SmsParserRegistry_ShouldExtractFees_DeduplicateByReferenceOrTimeWindow_AndHandleInvalidSmsGracefully()
    {
        var registry = new SmsParserRegistry();
        registry.RegisterParser(new SyntheticTestSmsParser());

        // 1. Invalid / unrecognized SMS -> returns null without throwing
        Assert.Null(registry.Parse(""));
        Assert.Null(registry.Parse("Bonjour maman, tu vas bien ?"));

        // 2. Valid SMS with fee and reference
        var sms1 = "TEST_MOMO|SENT|Amount:15.000 FCFA|Fee:150 FCFA|Ref:TX998877|To:Jean";
        var parsed1 = registry.Parse(sms1);
        Assert.NotNull(parsed1);
        Assert.Equal("TestMoMo", parsed1!.Operator);
        Assert.Equal(MobileMoneyTransactionType.TransferSent, parsed1.Type);
        Assert.Equal(15000m, parsed1.Amount);
        Assert.Equal(150m, parsed1.Fee);
        Assert.Equal("TX998877", parsed1.Reference);
        Assert.False(parsed1.IsIncome);

        // 3. Batch parsing with deduplication by Reference
        var batch = string.Join("\n\n", new[]
        {
            sms1,
            sms1, // duplicate reference TX998877
            "TEST_MOMO|RECV|Amount:25 000 FCFA|Fee:0 FCFA|Ref:TX112233|From:Paul"
        });

        var batchResult = registry.ParseBatch(batch);
        Assert.Equal(2, batchResult.Count);
        Assert.Contains(batchResult, t => t.Reference == "TX998877" && !t.IsIncome);
        Assert.Contains(batchResult, t => t.Reference == "TX112233" && t.IsIncome);

        // 4. Deduplication against existing transactions (by Reference or Date ±2min + Amount + Operator)
        var duplicateByRef = new ParsedMobileMoneyTransaction
        {
            Operator = "TestMoMo",
            Amount = 999m,
            Reference = "TX998877",
            Date = DateTime.UtcNow.AddHours(5)
        };
        Assert.True(registry.IsDuplicate(duplicateByRef, batchResult));

        var duplicateByWindow = new ParsedMobileMoneyTransaction
        {
            Operator = "TestMoMo",
            Amount = 15000m,
            Reference = null,
            Date = parsed1.Date.AddSeconds(45)
        };
        Assert.True(registry.IsDuplicate(duplicateByWindow, batchResult));
    }

    [Fact]
    public void OrangeMoneyCameroonParser_ShouldParseAllRealUserSmsFormats()
    {
        var parser = new OrangeMoneyCameroonParser();

        // Bloc 1: Transfert reçu (avec Details: et Frais: 0 FCFA)
        var bloc1 = "Transfert de 6XXXXXXXX NOM_EXP vers 6XXXXXXXX NOM_DEST reussi. Details: ID transaction: PP261019.2025.B10926, Montant Transaction: 400FCFA, Frais: 0 FCFA, Commission: 0 FCFA, Montant Net: 400 FCFA, Nouveau Solde: 407.42 FCFA.";
        Assert.True(parser.TryParse(bloc1, out var tx1));
        Assert.Equal("Orange Money", tx1!.Operator);
        Assert.Equal(MobileMoneyTransactionType.TransferReceived, tx1.Type);
        Assert.True(tx1.IsIncome);
        Assert.Equal(400m, tx1.Amount);
        Assert.Equal(0m, tx1.Fee);
        Assert.Equal(407.42m, tx1.BalanceAfter);
        Assert.Equal("PP261019.2025.B10926", tx1.Reference);
        Assert.Equal(new DateTime(2026, 10, 19, 20, 25, 0, DateTimeKind.Utc), tx1.Date);

        // Bloc 2: Dépôt effectué
        var bloc2 = "Depot effectue par 6XXXXXXXX AGENT_OM  to 6XXXXXXXX NOM_DEST. Informations detaillees: Montant de transaction : 5000 FCFA, ID transaction : CI261012.1905.C01502, Frais : 0 FCFA, Commission : 0 FCFA, Montant Net du Credit : 5000 FCFA, Nouveau Solde : 20077.42 FCFA.";
        Assert.True(parser.TryParse(bloc2, out var tx2));
        Assert.Equal(MobileMoneyTransactionType.Deposit, tx2!.Type);
        Assert.True(tx2.IsIncome);
        Assert.Equal(5000m, tx2.Amount);
        Assert.Equal("CI261012.1905.C01502", tx2.Reference);
        Assert.Equal(20077.42m, tx2.BalanceAfter);

        // Bloc 3: Paiement marchand (My Way5)
        var bloc3 = "Paiement de My Way5 reussi par 6XXXXXXXX NOM_CLIENT. ID transaction:MP2612.1706.C34410, Montant:300 FCFA. Solde: 15077.42 FCFA.";
        Assert.True(parser.TryParse(bloc3, out var tx3));
        Assert.Equal(MobileMoneyTransactionType.MerchantPayment, tx3!.Type);
        Assert.False(tx3.IsIncome);
        Assert.Equal(300m, tx3.Amount);
        Assert.Equal("My Way5", tx3.Counterparty);
        Assert.Equal("MP2612.1706.C34410", tx3.Reference);

        // Bloc 4 & 9: Transfert envoyé avec frais (5 FCFA)
        var bloc4 = "Transfert de 6XXXXXXXX NOM_EXP vers 6XXXXXXXX NOM_DEST reussi. ID transaction: PP261012.0927.A90863, Montant Transaction: 500 FCFA, Frais: 5 FCFA, Commission: 0 FCFA, Montant Net: 505 FCFA, Nouveau Solde: 15777.42 FCFA.";
        Assert.True(parser.TryParse(bloc4, out var tx4));
        Assert.Equal(MobileMoneyTransactionType.TransferSent, tx4!.Type);
        Assert.False(tx4.IsIncome);
        Assert.Equal(500m, tx4.Amount);
        Assert.Equal(5m, tx4.Fee);
        Assert.Equal("PP261012.0927.A90863", tx4.Reference);

        // Bloc 5: Achat forfait Maxit 2.0 (AirtimeData)
        var bloc5 = "Paiement de Achat forfait Maxit 2.0 reussi par 6XXXXXXXX NOM_CLIENT. ID transaction:MP261011.2136.B91088, Montant:500 FCFA. Solde: 16782.42 FCFA.";
        Assert.True(parser.TryParse(bloc5, out var tx5));
        Assert.Equal(MobileMoneyTransactionType.AirtimeData, tx5!.Type);
        Assert.Equal(500m, tx5.Amount);
        Assert.Equal("MP261011.2136.B91088", tx5.Reference);

        // Bloc 6: Format anglais WelcomePack (sans unité FCFA)
        var bloc6 = "Successful Payment of WelcomePack from 6XXXXXXXX NOM_CLIENT. Transaction ID: MP261011.1605.D39727, Amount: 100, New balance: 32.96.";
        Assert.True(parser.TryParse(bloc6, out var tx6));
        Assert.Equal(MobileMoneyTransactionType.AirtimeData, tx6!.Type);
        Assert.Equal(100m, tx6.Amount);
        Assert.Equal(32.96m, tx6.BalanceAfter);
        Assert.Equal("MP261011.1605.D39727", tx6.Reference);

        // Bloc 7: Transfert envoyé avec frais décimaux (4.2 FCFA)
        var bloc7 = "Transfert de 6XXXXXXXX NOM_EXP vers 6XXXXXXXX NOM_DEST  reussi. ID transaction: PP261011.1603.B09049, Montant Transaction: 100 FCFA, Frais: 4.2 FCFA, Commission: 0 FCFA, Montant Net: 104.2 FCFA, Nouveau Solde: 17282.42 FCFA.";
        Assert.True(parser.TryParse(bloc7, out var tx7));
        Assert.Equal(MobileMoneyTransactionType.TransferSent, tx7!.Type);
        Assert.Equal(100m, tx7.Amount);
        Assert.Equal(4.2m, tx7.Fee);

        // Bloc 8: Format anglais Successful transfer
        var bloc8 = "Successful transfer from 6XXXXXXXX NOM_EXP to 6XXXXXXXX NOM_DEST. Details: Transaction ID: PP261011.1603.B09049, Transaction amount: 100 FCFA, Charges: 0 FCFA, Commission: 0 FCFA, Net amount :100 FCFA, New balance: 132.96 FCFA.";
        Assert.True(parser.TryParse(bloc8, out var tx8));
        Assert.Equal(MobileMoneyTransactionType.TransferReceived, tx8!.Type);
        Assert.Equal(100m, tx8.Amount);
        Assert.Equal(0m, tx8.Fee);
        Assert.Equal(132.96m, tx8.BalanceAfter);
    }

    [Fact]
    public void MtnMomoCameroonParser_ShouldParseAllRealUserSmsFormats()
    {
        var parser = new MtnMomoCameroonParser();

        // Bloc 1, 3, 5: Achat forfait MTNC BUNDLES_FORFAITS avec texte promo en fin de SMS
        var bloc1 = "Une transaction de 499 XAF effectuee par MTNC BUNDLES_FORFAITS   (MTN_Bundles) sur votre compte d'argent mobile s'est terminee avec succes a 2026-10-15 08:33:55. Le montant faisait l'objet d'une remise de 0 et de bons de reduction d'une valeur de 0. Message du destinataire du debit : . Votre nouveau solde : 5720 XAF. Les frais s'elevaient a 0 XAF, les frais de fidelite a 0 et la recompense de fidelite a 0. Identifiant de transaction financiere : 19240427975. ID de transaction externe : 6004394991641868752. Emprunte jusqu'à 100 000 FCFA d'avance en quelques secondes pour tes transactions. Tape *126*6# ou télécharge la nouvelle MoMo App ici  link.mtn.cm/NewMoMoAppRef.";
        Assert.True(parser.TryParse(bloc1, out var mtn1));
        Assert.Equal("MTN MoMo", mtn1!.Operator);
        Assert.Equal(MobileMoneyTransactionType.AirtimeData, mtn1.Type);
        Assert.False(mtn1.IsIncome);
        Assert.Equal(499m, mtn1.Amount);
        Assert.Equal(0m, mtn1.Fee);
        Assert.Equal(5720m, mtn1.BalanceAfter);
        Assert.Equal("19240427975", mtn1.Reference);
        Assert.Equal(new DateTime(2026, 10, 15, 8, 33, 55, DateTimeKind.Utc), mtn1.Date);

        // Bloc 2, 6, 7, 11: Réception d'argent (avec motif 'tontine' et FRAIS: FCFA 0)
        var bloc6 = "Vous avez recu 101500 XAF de NOM_EXPEDITEUR (2376XXXXXXXX) sur votre compte mobile money à 2026-10-05 19:18:10. Message de l'expéditeur: tontine. Votre nouveau solde: 101647 XAF. FRAIS: FCFA 0. Financial Transaction Id: 19095118226.";
        Assert.True(parser.TryParse(bloc6, out var mtn6));
        Assert.Equal(MobileMoneyTransactionType.TransferReceived, mtn6!.Type);
        Assert.True(mtn6.IsIncome);
        Assert.Equal(101500m, mtn6.Amount);
        Assert.Equal(0m, mtn6.Fee);
        Assert.Equal(101647m, mtn6.BalanceAfter);
        Assert.Equal("19095118226", mtn6.Reference);

        // Bloc 4 & 9: Réception d'argent précédée d'une bannière promo ("1Go Njoh!...") et "Votre nouveau solde est de 493 FCFA"
        var bloc4 = "1Go Njoh! si tu te connectes sur l'appli New MoMoApp. Lien: link.mtn.cm/NewMoMoAppRef. Vous avez recu 450 XAF de NOM_EXPEDITEUR (2376XXXXXXXX ) sur votre compte Mobile Money 2026-10-12 12:43:33. Message de l'expediteur: . Votre nouveau solde est de 493 FCFA. Frais: 0 XAF. Transaction ID: 19197073957.";
        Assert.True(parser.TryParse(bloc4, out var mtn4));
        Assert.Equal(MobileMoneyTransactionType.TransferReceived, mtn4!.Type);
        Assert.Equal(450m, mtn4.Amount);
        Assert.Equal(493m, mtn4.BalanceAfter);
        Assert.Equal("19197073957", mtn4.Reference);

        // Bloc 8: Paiement marchand finalisé avec promo contenant d'autres montants ("5 237 F", "50 000 F")
        var bloc8 = "Votre paiement de 2000 XAF a MARCHAND SARL (2376XXXXXXXX) a ete finalise a 2026-09-06 11:01:24. Message : . Votre nouveau solde : 645 XAF. Les frais s'elevaient a 0 XAF. Le montant faisait l'objet d'une reduction de 0 XAF et de coupons d'une valeur de . Identifiant de transaction financiere : 18665100746. ID de transaction externe : -. }. C'est la Rentrée...MoMo Te Gère ! Pour toute transactions d'au moins 5 237 F , tente de gagner chaque jour:  Un Laptop + 50 000 F + une Home Box.";
        Assert.True(parser.TryParse(bloc8, out var mtn8));
        Assert.Equal(MobileMoneyTransactionType.MerchantPayment, mtn8!.Type);
        Assert.False(mtn8.IsIncome);
        Assert.Equal(2000m, mtn8.Amount);
        Assert.Equal(645m, mtn8.BalanceAfter);
        Assert.Equal("18665100746", mtn8.Reference);

        // Bloc 10: Retrait d'espèces ("Vous avez effectue avec succes le retrait de 1000 FCFA...")
        var bloc10 = "Vous avez effectue avec succes le retrait de 1000 FCFA de votre compte mobile money  chez AGENT_MOMO le 2026-08-14 12:56:55. Votre nouveau solde est de:6 FCFA. Transaction Id: 18345981868.  Emprunte jusqu'à 100 000 FCFA d'avance en quelques secondes pour tes transactions.";
        Assert.True(parser.TryParse(bloc10, out var mtn10));
        Assert.Equal(MobileMoneyTransactionType.Withdrawal, mtn10!.Type);
        Assert.False(mtn10.IsIncome);
        Assert.Equal(1000m, mtn10.Amount);
        Assert.Equal(6m, mtn10.BalanceAfter);
        Assert.Equal("18345981868", mtn10.Reference);
    }

    // =========================================================================
    // 6.2 Tontines / Njangi Pure Business Logic Tests
    // =========================================================================

    [Fact]
    public void TontineCalculator_5Members5Rounds_FullCycle_AndNetUserBalance()
    {
        var tontine = new Tontine
        {
            Id = 1,
            Name = "Tontine Quartier Bastos",
            ContributionAmount = 50_000m,
            Frequency = TontineFrequency.Monthly,
            TotalMembers = 5,
            MyTurnOrder = 2,
            StartDate = new DateTime(2026, 1, 10)
        };

        var members = new List<TontineMember>
        {
            new() { Id = 1, TontineId = 1, Name = "Paul", TurnOrder = 1, IsCurrentUser = false },
            new() { Id = 2, TontineId = 1, Name = "Moi", TurnOrder = 2, IsCurrentUser = true },
            new() { Id = 3, TontineId = 1, Name = "Chantal", TurnOrder = 3, IsCurrentUser = false },
            new() { Id = 4, TontineId = 1, Name = "Eric", TurnOrder = 4, IsCurrentUser = false },
            new() { Id = 5, TontineId = 1, Name = "Nadine", TurnOrder = 5, IsCurrentUser = false }
        };

        var pot = TontineCalculator.CalculatePot(tontine, members);
        Assert.Equal(250_000m, pot);

        var rounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(5, rounds.Count);
        Assert.Equal(2, rounds[1].BeneficiaryMemberId); // Round 2 belongs to "Moi"

        // Simulate Rounds 1 and 2 paid out; User contributed to Rounds 1, 2, 3
        rounds[0].IsPaidOut = true;
        rounds[1].IsPaidOut = true;

        var nextBeneficiary = TontineCalculator.GetNextBeneficiary(rounds, members);
        Assert.NotNull(nextBeneficiary);
        Assert.Equal("Chantal", nextBeneficiary!.Name);

        var contributions = new List<TontineContribution>
        {
            new() { TontineId = 1, RoundNumber = 1, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 1, 10) },
            new() { TontineId = 1, RoundNumber = 2, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 2, 10) },
            new() { TontineId = 1, RoundNumber = 3, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 3, 10) }
        };

        var summary = TontineCalculator.CalculateUserSummary(tontine, rounds, members, contributions);
        Assert.Equal(250_000m, summary.PotAmount);
        Assert.Equal(150_000m, summary.TotalContributedByUser);
        Assert.Equal(250_000m, summary.TotalReceivedByUser);
        // Net balance = 250,000 received - 150,000 contributed = +100,000 FCFA
        Assert.Equal(100_000m, summary.NetUserBalance);
        Assert.Equal(2, summary.CompletedRounds);
        Assert.Equal(3, summary.RemainingRounds);
    }

    [Fact]
    public void TontineCalculator_WithdrawnMember_LateContributions_WeeklyFrequency_AndYearEndTransition()
    {
        // Year-end transition: starts in November 2026, runs 5 monthly rounds -> ends March 2027
        var tontine = new Tontine
        {
            Id = 2,
            Name = "Njangi Fin d'Année",
            ContributionAmount = 20_000m,
            Frequency = TontineFrequency.Monthly,
            TotalMembers = 5,
            MyTurnOrder = 1,
            StartDate = new DateTime(2026, 11, 15)
        };

        var members = new List<TontineMember>
        {
            new() { Id = 10, TontineId = 2, Name = "Moi", TurnOrder = 1, IsCurrentUser = true },
            new() { Id = 11, TontineId = 2, Name = "Membre 2", TurnOrder = 2 },
            new() { Id = 12, TontineId = 2, Name = "Membre Retiré", TurnOrder = 3, IsWithdrawn = true },
            new() { Id = 13, TontineId = 2, Name = "Membre 4", TurnOrder = 4 },
            new() { Id = 14, TontineId = 2, Name = "Membre 5", TurnOrder = 5 }
        };

        // Active members = 4 (since Member 3 withdrew) -> Pot = 4 * 20,000 = 80,000
        var activePot = TontineCalculator.CalculatePot(tontine, members);
        Assert.Equal(80_000m, activePot);

        var monthlyRounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(4, monthlyRounds.Count); // Only active members get a round
        Assert.Equal(new DateTime(2026, 11, 15), monthlyRounds[0].ExpectedDate);
        Assert.Equal(new DateTime(2026, 12, 15), monthlyRounds[1].ExpectedDate);
        Assert.Equal(new DateTime(2027, 1, 15), monthlyRounds[2].ExpectedDate);
        Assert.Equal(new DateTime(2027, 2, 15), monthlyRounds[3].ExpectedDate);

        // Detect late contributions for Round 1 when reference date is 2026-11-20
        var recorded = new List<TontineContribution>
        {
            new() { TontineId = 2, RoundNumber = 1, MemberId = 10, Amount = 20_000m, IsPaid = true, Date = new DateTime(2026, 11, 14) }
        };

        var lateList = TontineCalculator.DetectLateContributions(monthlyRounds, members, recorded, new DateTime(2026, 11, 20));
        // 3 active members (11, 13, 14) have not paid Round 1 which was due 2026-11-15
        Assert.Equal(3, lateList.Count);
        Assert.All(lateList, c => Assert.True(c.IsLate && !c.IsPaid));

        // Weekly frequency check
        tontine.Frequency = TontineFrequency.Weekly;
        var weeklyRounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(new DateTime(2026, 11, 22), weeklyRounds[1].ExpectedDate);
    }

    // =========================================================================
    // 6.3 Irregular Income, Zero-Income Month & Weekly Envelopes Tests
    // =========================================================================

    [Fact]
    public void IrregularIncomePlanner_ShouldComputeRollingAverage_HandleZeroIncomeMonth_AndSplitWeeklyEnvelopes()
    {
        // Fluctuating incomes over 4 months including a 0 FCFA month
        var incomes = new decimal[] { 200_000m, 100_000m, 0m, 180_000m };
        var rolling3 = IrregularIncomePlanner.ComputeRollingAverageIncome(incomes, windowMonths: 3);
        // Last 3 months: (100,000 + 0 + 180,000) / 3 = 93,333.33 -> rounded
        Assert.Equal(93_333m, Math.Round(rolling3, 0));

        // Zero-income month with reserve cushion: must not divide by zero
        var zeroIncomes = new decimal[] { 0m, 0m, 0m };
        var plan = IrregularIncomePlanner.BuildWeeklyEnvelopePlan(
            recentMonthlyIncomes: zeroIncomes,
            vitalMinimumSpend: 60_000m,
            reserveCushion: 100_000m,
            weeksInMonth: 4,
            spentPerWeek: new decimal[] { 5_000m, 12_000m, 0m, 0m });

        Assert.Equal(0m, plan.RollingMonthlyIncome);
        // Available pool uses reserveCushion (100,000) - vitalMinimum (60,000) = 40,000 across 4 weeks = 10,000/week
        Assert.Equal(40_000m, plan.DiscretionaryPool);
        Assert.Equal(4, plan.WeeklyEnvelopes.Count);
        Assert.Equal(10_000m, plan.WeeklyEnvelopes[0].AllocatedBudget);
        Assert.Equal(5_000m, plan.WeeklyEnvelopes[0].RemainingBudget);
        Assert.Equal(-2_000m, plan.WeeklyEnvelopes[1].RemainingBudget);

        // Family solidarity report
        var solidarity = IrregularIncomePlanner.AnalyzeFamilySolidarity(
            solidaritySpend: 30_000m,
            monthlyIncome: 150_000m,
            monthlyTarget: 25_000m);

        Assert.Equal(20.0, solidarity.PercentageOfMonthlyIncome);
        Assert.True(solidarity.IsOverTarget);
    }

    private sealed class SyntheticTestSmsParser : IMobileMoneySmsParser
    {
        public string OperatorName => "TestMoMo";

        public bool CanParse(string smsBody) =>
            !string.IsNullOrWhiteSpace(smsBody) && smsBody.StartsWith("TEST_MOMO|", StringComparison.Ordinal);

        public bool TryParse(string smsBody, out ParsedMobileMoneyTransaction? transaction)
        {
            transaction = null;
            if (!CanParse(smsBody)) return false;

            var parts = smsBody.Split('|');
            var type = parts[1] == "RECV"
                ? MobileMoneyTransactionType.TransferReceived
                : MobileMoneyTransactionType.TransferSent;

            decimal amount = 0m;
            decimal fee = 0m;
            string? reference = null;

            foreach (var p in parts.Skip(2))
            {
                if (p.StartsWith("Amount:", StringComparison.OrdinalIgnoreCase))
                    MobileMoneyAmountParser.TryParseAmountXaf(p["Amount:".Length..], out amount);
                else if (p.StartsWith("Fee:", StringComparison.OrdinalIgnoreCase))
                    MobileMoneyAmountParser.TryParseAmountXaf(p["Fee:".Length..], out fee);
                else if (p.StartsWith("Ref:", StringComparison.OrdinalIgnoreCase))
                    reference = p["Ref:".Length..].Trim();
            }

            transaction = new ParsedMobileMoneyTransaction
            {
                Operator = OperatorName,
                Type = type,
                Amount = amount,
                Fee = fee,
                Reference = reference,
                Date = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                RawMessage = smsBody
            };
            return true;
        }
    }
}
