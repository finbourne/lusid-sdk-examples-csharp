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
    public class InterestRateSwapWithNamedConventions: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "UniqueKeyForDictionary",
                TestDataUtilities.VanillaSwapFixingReference,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                0.05m,
                "InterestRate",
                TestDataUtilities.ResetDate,
                QuoteSeriesId.QuoteTypeEnum.Price);

            var upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
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

            CreateAndUpsertInstrumentResetsToLusid(scope, model, instrument);

            // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves.
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

        [LusidFeature("F5-22")]
        [Test]
        public void InterestRateSwapWithNamedConventionsCreationAndUpsertionExample()
        {
            // CREATE a named convention Interest Rate Swap (IRS) (that can then be upserted into LUSID)
            var swap = InstrumentExamples.CreateSwapByNamedConventions();

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

        private void UpsertNamedConventionsToLusid()
        {
            // CREATE the flow conventions and index convention for swap
            string scope = "Conventions";
            string flowConventionsCode = "USD-6M";
            string indexConventionCode = "USD-6M-LIBOR";

            var flowConventions = new FlowConventions(
                scope: scope,
                code: flowConventionsCode,
                currency: "USD",
                paymentFrequency: "6M",
                rollConvention: "ModifiedFollowing",
                dayCountConvention: "Actual365",
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                settleDays: 2,
                resetDays: 2
            );

            var indexConvention = new IndexConvention(
                scope: scope,
                code: indexConventionCode,
                publicationDayLag: 0,
                currency: "USD",
                paymentTenor: "6M",
                dayCountConvention: "Actual365",
                fixingReference: TestDataUtilities.VanillaSwapFixingReference,
                indexName: "LIBOR"
            );

            // UPSERT the conventions to Lusid
            var flowConventionsResponse =  _conventionsApi.UpsertFlowConventions(new UpsertFlowConventionsRequest(flowConventions));
            Assert.That(flowConventionsResponse, Is.Not.Null);
            Assert.That(flowConventionsResponse.Value, Is.Not.Null);

            var indexConventionsResponse = _conventionsApi.UpsertIndexConvention(new UpsertIndexConventionRequest(indexConvention));
            Assert.That(indexConventionsResponse, Is.Not.Null);
            Assert.That(indexConventionsResponse.Value, Is.Not.Null);
        }

        [LusidFeature("F22-36")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapWithNamedConventionsValuationExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateSwapByNamedConventions();
            UpsertNamedConventionsToLusid();
            CallLusidGetValuationEndpoint(irs, model);
        }

        [LusidFeature("F22-37")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwapWithNamedConventionsInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateSwapByNamedConventions();
            UpsertNamedConventionsToLusid();
            CallLusidInlineValuationEndpoint(irs, model);
        }

        [LusidFeature("F22-38")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void InterestRateSwaptionPortfolioCashFlowsExample(ModelSelection.ModelEnum model)
        {
            var irs = InstrumentExamples.CreateSwapByNamedConventions(TestDataUtilities.StartDate.AddYears(1));
            CallLusidGetPortfolioCashFlowsEndpoint(irs, model);
        }
    }
}
