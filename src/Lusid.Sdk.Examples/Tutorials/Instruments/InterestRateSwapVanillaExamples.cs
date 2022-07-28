using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    /// <summary>
    /// Code examples for a vanilla (single-currency, fixed-to-floating) interest rate swap.
    /// </summary>
    [TestFixture]
    public class InterestRateSwapVanillaExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            InterestRateSwap irs = instrument as InterestRateSwap;
            FloatingLeg floatLeg = (FloatingLeg) irs.Legs.First(x => x.InstrumentType == LusidInstrument.InstrumentTypeEnum.FloatingLeg);
            var indexName = floatLeg.LegDefinition.IndexConvention.IndexName;
            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            switch (indexName)
            {
                case "LIBOR":
                    TestDataUtilities.BuildQuoteRequest(
                        quoteRequest,
                        "UniqueKeyForDictionary",
                        TestDataUtilities.VanillaSwapFixingReference,
                        QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                        0.05m,
                        "InterestRate",
                        TestDataUtilities.ResetDate,
                        QuoteSeriesId.QuoteTypeEnum.Price);
                    break;
                case "CDOR":
                    TestDataUtilities.BuildQuoteRequest(
                        quoteRequest,
                        "req",
                        TestDataUtilities.CDORFixingReference,
                        QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                        0.05m,
                        "InterestRate",
                        TestDataUtilities.ResetDate,
                        QuoteSeriesId.QuoteTypeEnum.Price);
                    break;
            }
            UpsertQuotesResponse upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // The price of a swap is determined by the price of the fixed leg and floating leg.
            // The price of a floating leg is determined by historic resets rates and projected rates.
            // In this method, we upsert reset rates.
            // For LUSID to pick up these quotes, we have added a RIC rule to the recipe (see BuildRecipeRequest in TestDataUtilities.cs)
            // The RIC rule has a large quote interval, this means that we can use one reset quote for all the resets.
            // For accurate pricing, one would want to upsert a quote per reset: TODO: Add reset extraction from instruments when available ANA-749
            InterestRateSwap irs = instrument as InterestRateSwap;

            FloatingLeg floatLeg = (FloatingLeg) irs.Legs.First(x => x.InstrumentType == LusidInstrument.InstrumentTypeEnum.FloatingLeg);

            var indexName = floatLeg.LegDefinition.IndexConvention.IndexName;
            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            UpsertQuotesResponse upsertResponse;
            Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest =
                new Dictionary<string, UpsertComplexMarketDataRequest>();
            switch (indexName)
            {
                case "LIBOR":
                    TestDataUtilities.BuildQuoteRequest(
                        quoteRequest,
                        "UniqueKeyForDictionary",
                        TestDataUtilities.VanillaSwapFixingReference,
                        QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                        0.05m,
                        "InterestRate",
                        TestDataUtilities.ResetDate,
                        QuoteSeriesId.QuoteTypeEnum.Price);
                    upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
                    Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                    Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));

                    // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves.
                    if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
                    {
                        upsertComplexMarketDataRequest.Add("discount_curve_USD",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve_USD",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR",
                                TestDataUtilities.ExampleDiscountFactors2, "6M"));

                        var upsertComplexMarketDataResponse =
                            _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                        ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse,
                            upsertComplexMarketDataRequest.Count);
                    }

                    break;
                case "CDOR":
                    TestDataUtilities.BuildQuoteRequest(
                        quoteRequest,
                        "req",
                        TestDataUtilities.CDORFixingReference,
                        QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                        0.05m,
                        "InterestRate",
                        TestDataUtilities.ResetDate,
                        QuoteSeriesId.QuoteTypeEnum.Price);
                    upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
                    Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                    Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));

                    // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves.
                    if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
                    {
                        upsertComplexMarketDataRequest.Add("discount_curve_CAD",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "CAD", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "CAD", "CDOR",
                                TestDataUtilities.ExampleDiscountFactors2, "3M"));

                        var upsertComplexMarketDataResponse =
                            _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                        ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse,
                            upsertComplexMarketDataRequest.Count);
                    }
                    break;
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(
            LusidInstrument instrument,
            string scope, string portfolioCode,
            string recipeCode,
            string instrumentID)
        {
            var swap = (InterestRateSwap) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: swap.StartDate.AddDays(-3),
                windowEnd: swap.MaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            Assert.That(cashflows.Count, Is.GreaterThanOrEqualTo(1));
        }

        [LusidFeature("F5-1")]
        [Test]
        public void InterestRateSwapCreationAndUpsertionExample()
        {
            // CREATE an interest rate swap (that can then be upserted into LUSID)
            var swap = InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla);

            // ASSERT that it was created
            Assert.That(swap, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = swap.InstrumentType + Guid.NewGuid().ToString();
            var instrumentsIds = new List<(LusidInstrument, string)>{(swap, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);

            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);

            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.InterestRateSwap);
            var roundTripSwap = retrieved as InterestRateSwap;
            Assert.That(roundTripSwap, Is.Not.Null);
            Assert.That(roundTripSwap.MaturityDate, Is.EqualTo(swap.MaturityDate));
            Assert.That(roundTripSwap.StartDate, Is.EqualTo(swap.StartDate));
            Assert.That(roundTripSwap.Legs.Count, Is.EqualTo(swap.Legs.Count));

            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }

        [LusidFeature("F22-22")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapValuationExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla);
            CallLusidGetValuationEndpoint(irs, model);
        }

        [LusidFeature("F22-23")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla);
            CallLusidInlineValuationEndpoint(irs, model);
        }

        [LusidFeature("F22-49")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapCDORInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.CDOR);
            CallLusidInlineValuationEndpoint(irs, model);
        }

        [LusidFeature("F22-24")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapPortfolioCashFlowsExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla);
            CallLusidGetPortfolioCashFlowsEndpoint(irs, model);
        }

        [LusidFeature("F22-46")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void LifeCycleManagementForInterestRateSwap(ModelSelection.ModelEnum model)
        {
            // CREATE an InterestRateSwap
            var interestRateSwap = (InterestRateSwap) InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla);
            // Pick out the currency for future reference.
            var fixedLeg = (FixedLeg) interestRateSwap.Legs
                .First(leg => leg.InstrumentType == LusidInstrument.InstrumentTypeEnum.FixedLeg);
            var currency = fixedLeg.LegDefinition.Conventions.Currency;

            // CREATE wide enough window to pick up all cashflows associated to the InterestRateSwap
            var windowStart = interestRateSwap.StartDate.AddMonths(-1);
            var windowEnd = interestRateSwap.MaturityDate.AddMonths(1);

            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, interestRateSwap);

            // Populate stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, interestRateSwap);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);

            // We expect that the PV of the InterestRateSwap should be zero after the maturity.
            // CREATE valuation request for this portfolio consisting of the InterestRateSwap,
            // with valuation dates a few days before, day of and a few days after the instrument expiration.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: interestRateSwap.MaturityDate.AddDays(2).AddMilliseconds(1),
                effectiveFrom: interestRateSwap.MaturityDate.AddDays(-2).AddMilliseconds(1)); //TODO: ANA-1292

            // CALL GetValuation before upserting back the cash flows. We check
            // (1) there is no cash holdings in the portfolio prior to upserting the cash flows.
            // (2) that when the InterestRateSwap has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationInterestRateSwap = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationInterestRateSwap,
                currency);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationInterestRateSwap,
                interestRateSwap.MaturityDate);

            // GET all upsertable cash flows (transactions) for the InterestRateSwap.
            // EffectiveAt after maturity so we have all cash flows
            var effectiveAt = interestRateSwap.MaturityDate.AddDays(1);
            var allInterestRateSwapCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
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

            // Check that some cash flows were returned
            Assert.That(allInterestRateSwapCashFlows, Is.Not.Empty);

            // For the portfolio to contain the cash flows coming from the InterestRateSwap
            // we have to upsert the transactions we obtained from the GetUpsertablePortfolioCashFlows endpoint above.
            // First populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = tutorials.Ibor.PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allInterestRateSwapCashFlows);

            // Then UPSERT the cash flows back into LUSID.
            _transactionPortfoliosApi.UpsertTransactions(
                scope,
                portfolioCode,
                tutorials.Ibor.PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            // HAVING upserted both cash flow and underlying into LUSID, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest);

            // ASSERT that we have some cash in the portfolio
            var luidsContainedInValuationResponse = valuationAfterUpsertingCashFlows
                .Data
                .Select(dictionaryOfMetrics => (string)dictionaryOfMetrics[TestDataUtilities.Luid]);
            Assert.That(luidsContainedInValuationResponse, Does.Contain($"CCY_{currency}"));


            // Every time a cash flow transaction occurs,
            // the PV of the InterestRateSwap changes accordingly in an equal and opposite amount.
            // Thus we expect the PV of the portfolio to be constant throughout.

            // Note all the dates on which cash flows occur.
            var cashFlowDates = allInterestRateSwapCashFlows
                .Select(cashFlowTransaction => cashFlowTransaction.TransactionDate)
                .Distinct()
                .ToList();

            // CHECK that the PV of the portfolio is constant across dates
            // that are in a small neighbourhood of every cash flow transaction date.
            foreach (var cashFlowDate in cashFlowDates)
            {
                // CREATE a valuation request for the given cash flow transaction date
                var valuationRequestNearCashFlow = TestDataUtilities.CreateValuationRequest(
                    scope,
                    portfolioCode,
                    recipeCode,
                    effectiveAt: cashFlowDate.AddDays(2).AddMilliseconds(1),
                    effectiveFrom: cashFlowDate.AddDays(-2).AddMilliseconds(1));

                // GET valuation on dates close to and on either side of the given cash flow transaction date
                var valuationResponseNearCashFlow = _aggregationApi.GetValuation(valuationRequestNearCashFlow);

                // ASSERT that the PV is constant across the cash flow
                TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationResponseNearCashFlow);
            }

            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
