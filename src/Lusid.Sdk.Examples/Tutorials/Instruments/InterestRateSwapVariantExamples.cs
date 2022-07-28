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
    /// Code examples for common interest rate swap variants (cross-currency, basis, amortising, etc.)
    /// </summary>
    [TestFixture]
    public class InterestRateSwapVariantExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // The price of a floating leg is determined by historic resets rates and projected rates.
            // In this method, we upsert reset rates.
            // For LUSID to pick up these quotes, we have added a RIC rule to the recipe (see BuildRecipeRequest in TestDataUtilities.cs)
            // The RIC rule has a large quote interval, this means that we can use one reset quote for all the resets.
            // For accurate pricing, one would want to upsert a quote per reset.
            InterestRateSwap irs = instrument as InterestRateSwap;

            // provide resets for each floating leg, with id equal to the fixing reference that the leg will request
            var floatLegs = irs.Legs.OfType<FloatingLeg>().ToList();
            var fixingRefs = floatLegs.Select(floatLeg => floatLeg.LegDefinition.IndexConvention.FixingReference).Distinct().ToList();
            Dictionary<string, UpsertQuoteRequest> quoteRequests = new Dictionary<string, UpsertQuoteRequest>();
            for (int i = 0; i < fixingRefs.Count; i++)
            {
                var fixingRef = fixingRefs[i];
                TestDataUtilities.BuildQuoteRequest(
                    quoteRequests,
                    "dummyReset" + i,
                    fixingRef,
                    QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                    0.03m + i/100m,
                    "InterestRate",
                    TestDataUtilities.ResetDate,
                    QuoteSeriesId.QuoteTypeEnum.Price);
            }

            // provide fx rates for cross-currency swaps
            var ccys = floatLegs.Select(leg => leg.LegDefinition.Conventions.Currency)
                .Concat(irs.Legs.OfType<FixedLeg>().Select(fixedLeg => fixedLeg.LegDefinition.Conventions.Currency)).ToList();
            if (ccys.Count > 1)
            {
                foreach (var fxRequest in TestDataUtilities.BuildFxRateRequest("USD", "GBP", 0.8m, TestDataUtilities.EffectiveAt, TestDataUtilities.EffectiveAt, useConstantFxRate: true))
                {
                    quoteRequests.Add(fxRequest.Key, fxRequest.Value);
                }
            }

            UpsertQuotesResponse upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequests);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequests.Count));
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // The price of a floating leg is determined by historic resets rates and projected rates.
            // In this method, we upsert reset rates.
            // For LUSID to pick up these quotes, we have added a RIC rule to the recipe (see BuildRecipeRequest in TestDataUtilities.cs)
            // The RIC rule has a large quote interval, this means that we can use one reset quote for all the resets.
            // For accurate pricing, one would want to upsert a quote per reset.
            InterestRateSwap irs = instrument as InterestRateSwap;

            // provide resets for each floating leg, with id equal to the fixing reference that the leg will request
            var floatLegs = irs.Legs.OfType<FloatingLeg>().ToList();
            CreateAndUpsertInstrumentResetsToLusid(scope, model, instrument);

            // For models requiring discounting and projection curves, we upsert them below. ConstantTimeValueOfMoney does not require any curves.
            Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();;
            UpsertStructuredDataResponse upsertComplexMarketDataResponse;
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                // provide discount/projection curves in USD, for swaps with a 3M floating leg
                upsertComplexMarketDataRequest.Add("discount_curve_USD",
                    TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("projection_curve_USD_3M",
                    TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors1, "3M"));

                // provide an additional 6M projection curves in USD, for single-currency basis swaps
                if (floatLegs.Select(leg => leg.LegDefinition.IndexConvention.PaymentTenor).Contains("6M"))
                {
                    upsertComplexMarketDataRequest.Add("projection_curve_USD_6M",
                        TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));
                }

                var ccys = floatLegs.Select(leg => leg.LegDefinition.Conventions.Currency)
                    .Concat(irs.Legs.OfType<FixedLeg>().Select(fixedLeg => fixedLeg.LegDefinition.Conventions.Currency)).ToList();
                // provide discount/projection curves in GBP, for cross-currency swaps
                if (ccys.Count > 1)
                {
                    upsertComplexMarketDataRequest.Add("discount_curve_GBP",
                        TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "GBP", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                    upsertComplexMarketDataRequest.Add("projection_curve_GBP_3M",
                        TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "GBP", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "3M"));
                }
            }
            upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
            ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
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

        [LusidFeature("F5-25")]
        [TestCase(InstrumentExamples.InterestRateSwapType.Basis)]
        [TestCase(InstrumentExamples.InterestRateSwapType.CrossCurrency)]
        [TestCase(InstrumentExamples.InterestRateSwapType.Amortising)]
        public void InterestRateSwapCreationAndUpsertionExample(InstrumentExamples.InterestRateSwapType type)
        {
            // CREATE an interest rate swap (that can then be upserted into LUSID)
            var swap = InstrumentExamples.CreateExampleInterestRateSwap(type);
            
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
       
        [LusidFeature("F22-52")]
        [TestCase(InstrumentExamples.InterestRateSwapType.Basis)]
        [TestCase(InstrumentExamples.InterestRateSwapType.CrossCurrency)]
        [TestCase(InstrumentExamples.InterestRateSwapType.Amortising)]
        public void InterestRateSwapValuationExample(InstrumentExamples.InterestRateSwapType interestRateSwapType)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(interestRateSwapType);
            CallLusidGetValuationEndpoint(irs, ModelSelection.ModelEnum.Discounting);
        }

        [LusidFeature("F22-53")]
        [TestCase(InstrumentExamples.InterestRateSwapType.Basis)]
        [TestCase(InstrumentExamples.InterestRateSwapType.CrossCurrency)]
        [TestCase(InstrumentExamples.InterestRateSwapType.Amortising)]
        public void InterestRateSwapInlineValuationExample(InstrumentExamples.InterestRateSwapType interestRateSwapType)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(interestRateSwapType);
            CallLusidInlineValuationEndpoint(irs, ModelSelection.ModelEnum.Discounting);
        }

        [LusidFeature("F22-54")]
        [TestCase(InstrumentExamples.InterestRateSwapType.Basis)]
        [TestCase(InstrumentExamples.InterestRateSwapType.CrossCurrency)]
        [TestCase(InstrumentExamples.InterestRateSwapType.Amortising)]
        public void InterestRateSwapPortfolioCashFlowsExample(InstrumentExamples.InterestRateSwapType interestRateSwapType)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(interestRateSwapType);
            CallLusidGetPortfolioCashFlowsEndpoint(irs, ModelSelection.ModelEnum.Discounting);
        }
    }
}
