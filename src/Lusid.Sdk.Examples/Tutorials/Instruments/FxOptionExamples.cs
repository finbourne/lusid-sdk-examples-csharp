using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.tutorials.Ibor;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class FxOptionExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument fxOption)
        {
            // POPULATE with required market data for valuation of the instruments
            var upsertFxRateRequestreq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, TestDataUtilities.EffectiveAt, TestDataUtilities.EffectiveAt);
            var upsertQuoteResponse = _quotesApi.UpsertQuotes(scope, upsertFxRateRequestreq);
            
            ValidateQuoteUpsert(upsertQuoteResponse, upsertFxRateRequestreq.Count);

            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("discount_curve_JPY", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "OIS", TestDataUtilities.ExampleDiscountFactors1));

            }
            if (model == ModelSelection.ModelEnum.BlackScholes)
            {
                upsertComplexMarketDataRequest.Add("VolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, fxOption, model, 0.2m));
            }
            if (model == ModelSelection.ModelEnum.Bachelier)
            { 
                upsertComplexMarketDataRequest.Add("VolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, fxOption, model, 10m));
            }

            if(upsertComplexMarketDataRequest.Any())
            {
                var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);            
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(
            LusidInstrument instrument,
            string scope, string portfolioCode,
            string recipeCode,
            string instrumentID)
        {
            var fxOption = (FxOption) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: fxOption.StartDate.AddDays(-3),
                windowEnd: fxOption.OptionMaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            var expectedNumberOfCashflows = fxOption.IsDeliveryNotCash ? 2 : 1;
            Assert.That(cashflows.Count, Is.EqualTo(expectedNumberOfCashflows));
        }

        [LusidFeature("F5-15")]
        [Test]
        public void FxOptionCreationAndUpsertionExample()
        {
            // CREATE an Fx-Option (that can then be upserted into LUSID)
            var fxOption = (FxOption) InstrumentExamples.CreateExampleFxOption();
            
            // ASSERT that it was created
            Assert.That(fxOption, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = fxOption.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>(){(fxOption, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.FxOption);
            var roundTripFxOption = retrieved as FxOption;
            Assert.That(roundTripFxOption, Is.Not.Null);
            Assert.That(roundTripFxOption.DomCcy, Is.EqualTo(fxOption.DomCcy));
            Assert.That(roundTripFxOption.FgnCcy, Is.EqualTo(fxOption.FgnCcy));
            Assert.That(roundTripFxOption.Strike, Is.EqualTo(fxOption.Strike));
            Assert.That(roundTripFxOption.StartDate, Is.EqualTo(fxOption.StartDate));
            Assert.That(roundTripFxOption.OptionMaturityDate, Is.EqualTo(fxOption.OptionMaturityDate));
            Assert.That(roundTripFxOption.OptionSettlementDate, Is.EqualTo(fxOption.OptionSettlementDate));
            Assert.That(roundTripFxOption.IsCallNotPut, Is.EqualTo(fxOption.IsCallNotPut));
            Assert.That(roundTripFxOption.IsDeliveryNotCash, Is.EqualTo(fxOption.IsDeliveryNotCash));
            
            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }
        
        [LusidFeature("F22-19")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void FxOptionValuationExample(ModelSelection.ModelEnum model, bool isDeliveryNotCash)
        {
            var fxOption = InstrumentExamples.CreateExampleFxOption(isDeliveryNotCash);
            CallLusidGetValuationEndpoint(fxOption, model);
        }
        
        [LusidFeature("F22-20")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void FxOptionInlineValuationExample(ModelSelection.ModelEnum model, bool isDeliveryNotCash)
        {
            var fxOption = InstrumentExamples.CreateExampleFxOption(isDeliveryNotCash);
            CallLusidInlineValuationEndpoint(fxOption, model);
        }

        [LusidFeature("F22-21")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void FxOptionPortfolioCashFlowsExample(ModelSelection.ModelEnum model, bool isDeliveryNotCash)
        {
            var fxOption = InstrumentExamples.CreateExampleFxOption(isDeliveryNotCash);
            CallLusidGetPortfolioCashFlowsEndpoint(fxOption, model);
        }

        /// <summary>
        /// Lifecycle management (i.e. conservation of PV on cashflow events) for FX option
        /// Cash-settled case (CTVoM model):
        ///      pv(option) = max(spot - strike, 0) before maturity and
        ///      pv(cashflow) = max(spot_at_maturity - strike, 0) after maturity
        /// Physically-settled case (CTVoM model):
        ///      If option is exercised, pay the strike to obtain the underlying.
        ///      pv(option) = max(spot - strike, 0) before maturity and
        ///      pv(cash balance) = max(spot_at_maturity - strike, 0) after maturity (assuming FX rates haven't changed)
        /// </summary>
        [LusidFeature("F22-45")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        public void LifeCycleManagementForFxOption(ModelSelection.ModelEnum model, bool isDeliveryNotCash)
        {
            // CREATE option
            var option = (FxOption) InstrumentExamples.CreateExampleFxOption(isDeliveryNotCash: isDeliveryNotCash);

            // CREATE wide enough window to pick up all cashflows associated to the option
            var windowStart = option.StartDate.AddMonths(-1);
            var windowEnd = option.OptionSettlementDate.AddMonths(1);

            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, option);

            // UPSERT option to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, option);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);

            // GET all upsertable cashflows (transactions) for the option
            // For this test, we will be performing valuations around the option settlement date, so we upsert some spot rates for the underlying FX rates.
            var effectiveAt = option.OptionSettlementDate;
            var upsertFxRatesNearSettlement = TestDataUtilities.BuildFxRateRequest(
                    "USD", "JPY", 150, 
                effectiveAt.AddDays(-5), effectiveAt.AddDays(5), useConstantFxRate: true);
            var upsertFxRatesResponse = _quotesApi.UpsertQuotes(scope, upsertFxRatesNearSettlement);
            ValidateQuoteUpsert(upsertFxRatesResponse, upsertFxRatesNearSettlement.Count);

            var allCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
                    scope,
                    portfolioCode,
                    effectiveAt,
                    windowStart,
                    windowEnd,
                    null,
                    null,
                    scope,
                    recipeCode)
                .Values;

            // We expect exactly one cashflow associated to a cash settled option, and two cashflows for a physically settled option. Both occur at expiry.
            Assert.That(allCashFlows.Count, Is.EqualTo(isDeliveryNotCash ? 2 : 1));
            var cashFlowDate = allCashFlows.First().TransactionDate;

            // CREATE valuation request for this portfolio consisting of the option,
            // with valuation dates a few days before, day of and a few days after the option expiration = cashflow date.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(4),
                effectiveFrom: cashFlowDate.AddDays(-4));

            // CALL GetValuation before upserting back the cashflows. We check
            // (1) there is no cash holdings in the portfolio prior to expiration
            // (2) that when the option has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationOption = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationOption,
                option.DomCcy);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationOption,
                option.OptionSettlementDate);

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allCashFlows);

            _transactionPortfoliosApi.UpsertTransactions(
                scope,
                portfolioCode,
                PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            // HAVING upserted cashflow into LUSID, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest);

            // ASSERT that we have the expected currencies of cash in the portfolio
            var currenciesInPortfolioAfterUpsertion = valuationAfterUpsertingCashFlows
                .Data
                .Select(d => (string)d[TestDataUtilities.Luid])
                .Where(luid => luid.StartsWith("CCY_"))
                .Distinct();
            Assert.That(currenciesInPortfolioAfterUpsertion.Count, Is.EqualTo(isDeliveryNotCash ? 2 : 1));

            // ASSERT portfolio PV is constant for each valuation date (assuming that FX rates have not changed since maturity)
            // We expect this to be true since we upserted the cashflows back in.
            // That is instrument pv + cashflow = option payoff = constant for each valuation date.
            TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationAfterUpsertingCashFlows, relativeDifferenceTolerance: 1e-10);

            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
