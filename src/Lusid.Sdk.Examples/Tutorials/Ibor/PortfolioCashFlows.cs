using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.tutorials.Ibor
{
    [TestFixture]
    public class PortfolioCashFlows: TutorialBase
    {
        private static readonly string ValuationDateKey = "Analytic/default/ValuationDate";
        private static readonly string ValuationPv = "Valuation/PV/Amount";
        private static readonly string ValuationCcy = "Valuation/PV/Ccy";
        private static readonly string Luid = "Instrument/default/LusidInstrumentId";
        private static readonly string InstrumentName = "Instrument/default/Name";
        private static readonly string InstrumentTag = "Analytic/default/InstrumentTag";
        
        private static readonly List<AggregateSpec> ValuationSpec = new List<AggregateSpec>
        {
            new AggregateSpec(ValuationDateKey, AggregateSpec.OpEnum.Value),
            new AggregateSpec(ValuationPv, AggregateSpec.OpEnum.Value),
            new AggregateSpec(ValuationCcy, AggregateSpec.OpEnum.Value),
            new AggregateSpec(InstrumentName, AggregateSpec.OpEnum.Value),
            new AggregateSpec(InstrumentTag, AggregateSpec.OpEnum.Value),
            new AggregateSpec(Luid, AggregateSpec.OpEnum.Value)
        };

        internal DateTimeOffset _effectiveAt;
        internal string _portfolioCode;
        internal string _portfolioScope;
        [SetUp]
        public void SetUp()
        {
            _portfolioScope = TestDataUtilities.TutorialScope;
            _effectiveAt = new DateTimeOffset(2020, 2, 23, 0, 0, 0, TimeSpan.Zero);
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(_portfolioScope, portfolioRequest);
            _portfolioCode = portfolioRequest.Code;

            Assert.That(portfolio?.Id.Code, Is.EqualTo(_portfolioCode));
        }

        [TearDown]
        public void TearDown()
        {
            _portfoliosApi.DeletePortfolio(_portfolioScope, _portfolioCode); 
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExamplePortfolioCashFlowsForFxForwards(bool isNdf)
        {
            // CREATE Fx Forward
            var fxForward = InstrumentExamples.CreateExampleFxForward(isNdf) as FxForward;
        
            // UPSERT Fx Forward to portfolio and populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                _portfolioScope, 
                _portfolioCode,
                _effectiveAt,
                _effectiveAt,
                new List<LusidInstrument>(){fxForward});

            // CREATE and upsert CTVoM recipe specifying discount pricing model
            var modelRecipeCode = "CTVoMRecipe";
            CreateAndUpsertRecipe(modelRecipeCode, _portfolioScope, ModelSelection.ModelEnum.ConstantTimeValueOfMoney);

            // CALL api to get cashflows at maturity
            var maturity = fxForward.MaturityDate;
            var cashFlowsAtMaturity = _transactionPortfoliosApi.GetPortfolioCashFlows(
                _portfolioScope,
                _portfolioCode,
                _effectiveAt,
                maturity.AddMilliseconds(-1),
                maturity.AddMilliseconds(1),
                null,
                null,
                _portfolioScope,
                modelRecipeCode
                );
            
            // CHECK correct number of cashflow at maturity
            var expectedNumber = isNdf ? 1 : 2;
            Assert.That(cashFlowsAtMaturity.Values.Count, Is.EqualTo(expectedNumber));
            
            var cashFlows = cashFlowsAtMaturity.Values.Select(cf => cf)
                .Select(cf => (cf.PaymentDate, cf.Amount, cf.Currency))
                .ToList();

            var expectedCashFlows = isNdf
                ? new List<(DateTimeOffset? PaymentDate, decimal? Amount, string Currency)>
                {
                    (fxForward.MaturityDate, fxForward.DomAmount, fxForward.DomCcy)
                }
                : new List<(DateTimeOffset? PaymentDate, decimal? Amount, string Currency)> 
                {
                    (fxForward.MaturityDate, fxForward.DomAmount, fxForward.DomCcy),
                    (fxForward.MaturityDate, -fxForward.FgnAmount, fxForward.FgnCcy),
                };
            Assert.That(cashFlows, Is.EquivalentTo(expectedCashFlows)); 
            
            _recipeApi.DeleteConfigurationRecipe(_portfolioScope, modelRecipeCode);
        }
        
        [Test]
        public void ExampleUpsertablePortfolioCashFlowsForBonds()
        {
            // CREATE bond
            var bond = InstrumentExamples.CreateExampleBond() as Bond;
        
            // UPSERT bond to portfolio and populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                _portfolioScope, 
                _portfolioCode,
                _effectiveAt,
                _effectiveAt,
                new List<LusidInstrument>(){bond});
            
            // CALL api to get upsertable cashflows at maturity            
            var maturity = bond.MaturityDate;
            var cashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
                _portfolioScope,
                _portfolioCode,
                maturity.AddMilliseconds(-1),
                maturity.AddMilliseconds(-1),
                maturity.AddMilliseconds(1));

            // CHECK correct number of cashflow at bond maturity: There are 2 cash flows corresponding to the last coupon amount and the principal.
            var expectedNumber = 2;
            Assert.That(cashFlows.Values.Count, Is.EqualTo(expectedNumber));
            
            // CHECK correct currency and amount of cashflows
            var currencyAndAmounts = cashFlows.Values.Select(t => t.TotalConsideration).ToList();
            var matchingCurrency = currencyAndAmounts.All(t => t.Currency == bond.DomCcy);
            var amountsPositive = currencyAndAmounts.All(t => t.Amount > 0);
            Assert.That(matchingCurrency, Is.True);
            Assert.That(amountsPositive, Is.True);
            
            // GIVEN the cashflow transactions, we create from them transaction requests and upsert them.
            var upsertCashFlowTransactions = PopulateCashFlowTransactionWithUniqueIds(cashFlows.Values);
            _transactionPortfoliosApi.UpsertTransactions(_portfolioScope, _portfolioCode, MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            var expectedPortfolioTransactions = _transactionPortfoliosApi.GetTransactions(
                    _portfolioScope, 
                    _portfolioCode, 
                    maturity.AddMilliseconds(-1), 
                    maturity.AddMilliseconds(1), 
                    DateTimeOffset.Now)
                .Values;
            
            foreach (var transaction in upsertCashFlowTransactions)
            {
                var getExpectedTransactions = expectedPortfolioTransactions.FirstOrDefault(t => t.TransactionId == transaction.TransactionId);
                
                Assert.That(getExpectedTransactions, Is.Not.Null);
                Assert.That(getExpectedTransactions.InstrumentUid, Is.EqualTo($"CCY_USD"));
                Assert.That(getExpectedTransactions.TransactionCurrency, Is.EqualTo(transaction.TransactionCurrency));
                Assert.That(getExpectedTransactions.Type, Is.EqualTo(transaction.Type));
                Assert.That(getExpectedTransactions.Units, Is.EqualTo(transaction.Units));
            }
        }
        
        [TestCase(true)]
        [TestCase(false)]
        public void ExampleUpsertablePortfolioCashFlowsForFxForwards(bool isNdf)
        {
            // CREATE Fx Forward
            var fxForward = InstrumentExamples.CreateExampleFxForward(isNdf) as FxForward;
        
            // UPSERT Fx Forward to portfolio and populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                _portfolioScope, 
                _portfolioCode,
                _effectiveAt,
                _effectiveAt,
                new List<LusidInstrument>(){fxForward});

            // CREATE and upsert CTVoM recipe specifying discount pricing model
            var modelRecipeCode = "CTVoMRecipe";
            CreateAndUpsertRecipe(modelRecipeCode, _portfolioScope, ModelSelection.ModelEnum.ConstantTimeValueOfMoney);

            // CALL api to get upsertable cashflows at maturity            
            var maturity = fxForward.MaturityDate;
            var cashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
                _portfolioScope,
                _portfolioCode,
                _effectiveAt,
                maturity.AddMilliseconds(-1),
                maturity.AddMilliseconds(1),
                null,
                null,
                _portfolioScope,
                modelRecipeCode);

            // CHECK correct number of cashflow at maturity
            var expectedNumber = isNdf ? 1 : 2;
            Assert.That(cashFlows.Values.Count, Is.EqualTo(expectedNumber));
            var currencyAndAmounts = cashFlows.Values.Select(t => t.TotalConsideration).ToList();

            var expectedCashFlows = isNdf
                ? new List<CurrencyAndAmount>
                    {
                        new CurrencyAndAmount(fxForward.DomAmount, fxForward.DomCcy)
                    }
                : new List<CurrencyAndAmount> 
                    {
                        new CurrencyAndAmount(fxForward.DomAmount, fxForward.DomCcy),
                        new CurrencyAndAmount(fxForward.FgnAmount, fxForward.FgnCcy)
                    };

            Assert.That(currencyAndAmounts, Is.EquivalentTo(expectedCashFlows)); 
            
            // GIVEN the cashflow transactions, we create from them transaction requests and upsert them.
            var upsertCashFlowTransactions = PopulateCashFlowTransactionWithUniqueIds(cashFlows.Values);
            _transactionPortfoliosApi.UpsertTransactions(_portfolioScope, _portfolioCode, MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            _recipeApi.DeleteConfigurationRecipe(_portfolioScope, modelRecipeCode);
        }

        // This method maps a list of Transactions to a list of TransactionRequests so that they can be upserted back into LUSID.
        internal static List<TransactionRequest> MapToCashFlowTransactionRequest(IEnumerable<Transaction> transactions)
        {
            return transactions.Select(transaction => new TransactionRequest(
                transaction.TransactionId,
                transaction.Type,
                transaction.InstrumentIdentifiers,
                transaction.TransactionDate,
                transaction.SettlementDate,
                transaction.Units,
                transaction.TransactionPrice,
                transaction.TotalConsideration,
                transaction.ExchangeRate,
                transaction.TransactionCurrency,
                transaction.Properties,
                transaction.CounterpartyId,
                transaction.Source)
            ).ToList();
        }
        
        // Given a transaction, this method creates a TransactionRequest so that it can be upserted back into LUSID.
        // InstrumentUid is additionally added to identify where the cashflow came from. The transaction ID needs to
        // be unique.
        internal static IEnumerable<Transaction> PopulateCashFlowTransactionWithUniqueIds(IEnumerable<Transaction> transactions)
        {
            foreach (var transaction in transactions)
            {
                transaction.InstrumentIdentifiers.Add("Instrument/default/Currency", transaction.TransactionCurrency);
            }
            
            return transactions.Select((transaction , i) => new Transaction(
                transaction.TransactionId + $"{i}",
                transaction.Type,
                transaction.InstrumentIdentifiers,
                transaction.InstrumentScope,
                transaction.InstrumentUid,
                transaction.TransactionDate,
                transaction.SettlementDate,
                transaction.Units,
                transaction.TransactionPrice,
                transaction.TotalConsideration,
                transaction.ExchangeRate,
                transaction.TransactionCurrency,
                transaction.Properties,
                transaction.CounterpartyId,
                transaction.Source)
            );
        }
    }
}
