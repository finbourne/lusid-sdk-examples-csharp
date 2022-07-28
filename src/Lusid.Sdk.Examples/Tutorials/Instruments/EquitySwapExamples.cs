using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.tutorials.Ibor;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class EquitySwapExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // UPSERT quote for pricing of the equity swap. In particular we upsert a quote for the equity underlying.
            var equitySwap = (EquitySwap) instrument;

            // FOR GetValuation, we need the price of the equity underlying on those dates
            var quotesToUpsert = new Dictionary<string, UpsertQuoteRequest>();

            // FOR valuation example, we price on TestDataUtilities.EffectiveAt
            Dictionary<string, UpsertQuoteRequest> equityUnderlyingQuoteRequestAtEffectiveAt = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quotesToUpsert,
                equitySwap.Code,
                equitySwap.Code,
                QuoteSeriesId.InstrumentIdTypeEnum.Figi,
                135m,
                "USD",
                TestDataUtilities.EffectiveAt,
                QuoteSeriesId.QuoteTypeEnum.Price);

            // FOR lifecycle example, we value around maturity of the equity swap on the following days
            var days = new []{0, 1, 2, 3, 4};
            foreach (var n in days)
            {
                var date = equitySwap.MaturityDate.AddDays(-n);
                TestDataUtilities.BuildQuoteRequest(
                    quotesToUpsert,
                    $"{equitySwap.Code}_{n.ToString()}",
                    equitySwap.Code,
                    QuoteSeriesId.InstrumentIdTypeEnum.Figi,
                    135m,
                    "USD",
                    date,
                    QuoteSeriesId.QuoteTypeEnum.Price);
            }

            // UPSERT quote for the floating leg on reset date.
            TestDataUtilities.BuildQuoteRequest(
                quotesToUpsert,
                "UniqueKeyForDictionary",
                TestDataUtilities.EquitySwapFixingRef,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                0.05m,
                "USD",
                TestDataUtilities.ResetDate,
                QuoteSeriesId.QuoteTypeEnum.Price
                );

            var upsertResponse = _quotesApi.UpsertQuotes(scope, quotesToUpsert);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quotesToUpsert.Count));        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // Any required resets.
            CreateAndUpsertInstrumentResetsToLusid(scope, model, instrument);

            // Upsert discounting curves
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest =
                    new Dictionary<string, UpsertComplexMarketDataRequest>();
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));

                var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode, string instrumentID)
        {
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: new DateTimeOrCutLabel(new DateTimeOffset(2000, 01, 01, 01, 0, 0, 0, TimeSpan.Zero)),
                windowEnd: new DateTimeOrCutLabel(new DateTimeOffset(2050, 01, 01, 01, 0, 0, 0, TimeSpan.Zero)),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            Assert.That(cashflows.Count, Is.GreaterThanOrEqualTo(2));
        }

        [LusidFeature("F5-10")]
        [Test]
        public void EquitySwapCreationAndUpsertionExample()
        {
            // CREATE an equitySwap instrument (that can then be upserted into LUSID)
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap();

            // ASSERT that it was created
            Assert.That(equitySwap, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = equitySwap.InstrumentType + Guid.NewGuid().ToString();
            var instrumentsIds = new List<(LusidInstrument, string)>{(equitySwap, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse ,uniqueId);

            // CHECK contents
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.EquitySwap);
            var roundTripEquitySwap = retrieved as EquitySwap;
            Assert.That(roundTripEquitySwap, Is.Not.Null);
            Assert.That(roundTripEquitySwap.StartDate, Is.EqualTo(equitySwap.StartDate));
            Assert.That(roundTripEquitySwap.MaturityDate, Is.EqualTo(equitySwap.MaturityDate));
            Assert.That(roundTripEquitySwap.Quantity, Is.EqualTo(equitySwap.Quantity));
            Assert.That(roundTripEquitySwap.Code, Is.EqualTo(equitySwap.Code));
            Assert.That(roundTripEquitySwap.IncludeDividends, Is.EqualTo(equitySwap.IncludeDividends));
            Assert.That(roundTripEquitySwap.EquityFlowConventions.Code, Is.EqualTo(equitySwap.EquityFlowConventions.Code));
            Assert.That(roundTripEquitySwap.InitialPrice, Is.EqualTo(equitySwap.InitialPrice));
            Assert.That(roundTripEquitySwap.FundingLeg.InstrumentType, Is.EqualTo(equitySwap.FundingLeg.InstrumentType));
            Assert.That(roundTripEquitySwap.NotionalReset, Is.EqualTo(equitySwap.NotionalReset));
            Assert.That(roundTripEquitySwap.UnderlyingIdentifier, Is.EqualTo(equitySwap.UnderlyingIdentifier));

            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }

        [LusidFeature("F22-40")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        public void EquitySwapValuationExample(ModelSelection.ModelEnum model, bool multiCoupon)
        {
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap(multiCoupon);
            CallLusidGetValuationEndpoint(equitySwap, model);
        }

        [LusidFeature("F22-41")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        public void EquitySwapInlineValuationExample(ModelSelection.ModelEnum model, bool multiCoupon)
        {
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap(multiCoupon);
            CallLusidInlineValuationEndpoint(equitySwap, model);
        }

        [LusidFeature("F22-42")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        public void EquitySwapPortfolioCashFlowsExample(ModelSelection.ModelEnum model, bool multiCoupon)
        {
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap(multiCoupon);
            CallLusidGetPortfolioCashFlowsEndpoint(equitySwap, model);
        }

        [LusidFeature("F22-43")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void EquitySwapValuationExampleWithExposureAndAccruedInterest(ModelSelection.ModelEnum model)
        {
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap();

            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, equitySwap);

            // UPSERT EquitySwap to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, equitySwap);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model);

            // CREATE valuation request for this portfolio consisting of the instrument
            var accruedInterestKey = "Valuation/Accrued";
            var exposureKey = "Valuation/Exposure";
            var exposureAndAccruedKeys = new List<string>
            {
                accruedInterestKey,
                exposureKey,
            };
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                additionalRequestsKeys: exposureAndAccruedKeys);

            // CALL LUSID's GetValuation endpoint
            var results = _aggregationApi.GetValuation(valuationRequest).Data;
            Assert.That(results.Count, Is.EqualTo(1));
            var data = results.First();

            // CHECK exposure
            var exposure = (double) data[exposureKey];
            Assert.That(exposure, Is.GreaterThanOrEqualTo(0));

            // CHECK accrued interest is returned and is not zero (for equity swaps, it can be positive or negative).
            var accruedInterest = (double) data[accruedInterestKey];
            Assert.That(accruedInterest, Is.Not.EqualTo(0).Within(1e-3));

            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }

        [LusidFeature("F22-44")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void LifeCycleManagementForEquitySwap(ModelSelection.ModelEnum model)
        {
            // CREATE an EquitySwap
            var equitySwap = InstrumentExamples.CreateExampleEquitySwap();

            // CREATE wide enough window to pick up all cashflows associated to the EquitySwap
            var windowStart = equitySwap.StartDate.AddMonths(-1);
            var windowEnd = equitySwap.MaturityDate.AddMonths(1);

            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, equitySwap);

            // UPSERT EquitySwap to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, equitySwap);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);

            // GET all upsertable cashflows (transactions) for the EquitySwap.
            // EffectiveAt after maturity so we have all the data.
            var effectiveAt = equitySwap.MaturityDate.AddDays(1);
            var allEquitySwapCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
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

            Assert.That(allEquitySwapCashFlows.Count, Is.EqualTo(2));
            var cashFlowDate = allEquitySwapCashFlows.First().TransactionDate;

            // CREATE valuation request for this portfolio consisting of the EquityEquitySwap,
            // with valuation dates a few days before, day of and a few days after the instrument expiration = cashflow date.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(5),
                effectiveFrom: cashFlowDate.AddDays(-5));

            // CALL GetValuation before upserting back the cashflows. We check
            // (1) there is no cash holdings in the portfolio prior to expiration
            // (2) that when the EquitySwap has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationEquitySwap = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationEquitySwap,
                equitySwap.EquityFlowConventions.Currency);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationEquitySwap,
                equitySwap.MaturityDate);

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allEquitySwapCashFlows);

            _transactionPortfoliosApi.UpsertTransactions(
                scope,
                portfolioCode,
                PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            // HAVING upserted both cashflow and underlying into LUSID, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest);

            // ASSERT that we have some cash in the portfolio
            var containsCashAfterUpsertion = valuationAfterUpsertingCashFlows
                .Data
                .Select(d => (string) d[TestDataUtilities.Luid])
                .Any(luid => luid != $"CCY_{equitySwap.EquityFlowConventions.Currency}");
            Assert.That(containsCashAfterUpsertion, Is.True);

            // ASSERT portfolio PV is constant for each valuation date.
            // We expect this to be true since we upserted the cashflows back in.
            // That is instrument pv + cashflow = constant for each valuation date.
            TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationAfterUpsertingCashFlows);

            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
