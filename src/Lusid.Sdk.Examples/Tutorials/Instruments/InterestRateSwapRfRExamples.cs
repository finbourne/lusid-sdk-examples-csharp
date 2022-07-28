using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class InterestRateSwapRfRExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            Dictionary<string, UpsertQuoteRequest> quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "req",
                TestDataUtilities.RFRFixingReference,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                0.05m,
                "InterestRate",
                TestDataUtilities.ResetDate,
                QuoteSeriesId.QuoteTypeEnum.Price);

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
            // For accurate pricing, one would want to upsert a quote per reset.
            InterestRateSwap irs = instrument as InterestRateSwap;

            FloatingLeg floatLeg =
                (FloatingLeg) irs.Legs.Where(x => x.InstrumentType == LusidInstrument.InstrumentTypeEnum.FloatingLeg).First();

            var indexName = floatLeg.LegDefinition.IndexConvention.IndexName;

            // resets
            CreateAndUpsertInstrumentResetsToLusid(scope, model, instrument);

            Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();;
            UpsertStructuredDataResponse upsertComplexMarketDataResponse;
            // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves. 
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                switch (indexName)
                {
                    case "SOFR":
                        upsertComplexMarketDataRequest.Add("discount_curve_USD",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "SOFR",
                                TestDataUtilities.ExampleDiscountFactors2, "1D"));
                        break;
                    case "ESTR":
                        upsertComplexMarketDataRequest.Add("discount_curve_EUR",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "EUR", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "EUR", "ESTR",
                                TestDataUtilities.ExampleDiscountFactors2, "1D"));
                        break;
                    case "SONIA":
                        upsertComplexMarketDataRequest.Add("discount_curve_GBP",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "GBP", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "GBP", "SONIA",
                                TestDataUtilities.ExampleDiscountFactors2, "1D"));
                        break;
                    case "TONA":
                        upsertComplexMarketDataRequest.Add("discount_curve_JPY",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "TONA",
                                TestDataUtilities.ExampleDiscountFactors2, "1D"));
                        break;
                    case "SARON":
                        upsertComplexMarketDataRequest.Add("discount_curve_CHF",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "CHF", "OIS",
                                TestDataUtilities.ExampleDiscountFactors1));
                        upsertComplexMarketDataRequest.Add("projection_curve",
                            TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "CHF", "SARON",
                                TestDataUtilities.ExampleDiscountFactors2, "1D"));
                        break;
                }
                upsertComplexMarketDataResponse =
                    _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse,
                    upsertComplexMarketDataRequest.Count);
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
                effectiveAt: TestDataUtilities.EffectiveAt.AddYears(3),
                windowStart: swap.StartDate.AddDays(-3),
                windowEnd: swap.MaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;
            
            Assert.That(cashflows.Count, Is.GreaterThanOrEqualTo(1));
        }

        [LusidFeature("F5-23")]
        [TestCase(InstrumentExamples.InterestRateSwapType.SOFR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.ESTR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SONIA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.TONA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SARON)]
        public void InterestRateSwapRfRCreationAndUpsertionExample(InstrumentExamples.InterestRateSwapType type)
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
       
        [LusidFeature("F22-47")]
        [TestCase(InstrumentExamples.InterestRateSwapType.SOFR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.ESTR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SONIA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.TONA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SARON)]
        public void InterestRateSwapRfRInlineValuationExample(InstrumentExamples.InterestRateSwapType rfr)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(rfr);
            CallLusidInlineValuationEndpoint(irs, ModelSelection.ModelEnum.Discounting);
        }
        
        [LusidFeature("F22-48")]
        [TestCase(InstrumentExamples.InterestRateSwapType.SOFR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.ESTR)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SONIA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.TONA)]
        [TestCase(InstrumentExamples.InterestRateSwapType.SARON)]
        public void InterestRateSwapRfRPortfolioCashFlowsExample(InstrumentExamples.InterestRateSwapType rfr)
        {
            var irs = InstrumentExamples.CreateExampleInterestRateSwap(rfr);
            CallLusidGetPortfolioCashFlowsEndpoint(irs, ModelSelection.ModelEnum.Discounting);
        }
       
    }
}
