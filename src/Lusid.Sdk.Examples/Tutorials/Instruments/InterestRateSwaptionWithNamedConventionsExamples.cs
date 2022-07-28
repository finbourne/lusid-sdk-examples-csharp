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
    public class InterestRateSwaptionWithNamedConventionsExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // The price of a swaption depends on its swap underlying which in turn
            // itself is determined by the price of the fixed leg and floating leg.
            // The price of a floating leg is determined by historic resets rates and projected rates.
            // In this method, we upsert reset rates.
            // For LUSID to pick up these quotes, we have added a RIC rule to the recipe (see BuildRecipeRequest in TestDataUtilities.cs)
            // The RIC rule has a large quote interval, this means that we can use one reset quote for all the resets.
            // For accurate pricing, one would want to upsert a quote per reset.

            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "UniqueKeyForDictionary",
                TestDataUtilities.VanillaSwapFixingReference,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                0.02m,
                "InterestRate",
                TestDataUtilities.ResetDate,
                QuoteSeriesId.QuoteTypeEnum.Price);
            var upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));

            // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves.
            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));
            }
            if (model == ModelSelection.ModelEnum.BlackScholes || model == ModelSelection.ModelEnum.Bachelier)
            {
                var volatility = (model == ModelSelection.ModelEnum.BlackScholes) ? 0.2m : 10m;
                upsertComplexMarketDataRequest.Add("VolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, instrument, model, volatility));
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
            string scope,
            string portfolioCode,
            string recipeCode,
            string instrumentID)
        {
            var swaption = (InterestRateSwaption) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: swaption.StartDate.AddDays(-3),
                windowEnd: swaption.Swap.MaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            Assert.That(cashflows.Count, Is.GreaterThanOrEqualTo(1));
        }

        [LusidFeature("F5-16")]
        [Test]
        public void InterestRateSwaptionWithNamedConventionsExamplesCreationAndUpsertionExample()
        {
            // CREATE an interest rate swaption (that can then be upserted into LUSID)
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaptionWithNamedConventions();

            // ASSERT that it was created
            Assert.That(swaption, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = swaption.InstrumentType+Guid.NewGuid().ToString();
            var instrumentsIds = new List<(LusidInstrument, string)>{(swaption, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);

            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);

            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.InterestRateSwaption);
            var roundTripSwaption = retrieved as InterestRateSwaption;
            Assert.That(roundTripSwaption, Is.Not.Null);
            Assert.That(roundTripSwaption.DeliveryMethod, Is.EqualTo(swaption.DeliveryMethod));
            Assert.That(roundTripSwaption.StartDate, Is.EqualTo(swaption.StartDate));
            Assert.That(roundTripSwaption.PayOrReceiveFixed, Is.EqualTo(swaption.PayOrReceiveFixed));
            Assert.That(roundTripSwaption.Swap, Is.Not.Null);
            Assert.That(roundTripSwaption.Swap.InstrumentType, Is.EqualTo(LusidInstrument.InstrumentTypeEnum.InterestRateSwap));

            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }

        [LusidFeature("F22-25")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        [TestCase(ModelSelection.ModelEnum.Bachelier)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes)]
        public void InterestRateSwaptionWithNamedConventionsExamplesValuationExample(ModelSelection.ModelEnum model)
        {
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaptionWithNamedConventions();
            UpsertNamedConventionsToLusid();
            CallLusidGetValuationEndpoint(swaption, model);
        }

        [LusidFeature("F22-26")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        [TestCase(ModelSelection.ModelEnum.Bachelier)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes)]
        public void InterestRateSwaptionWithNamedConventionsExamplesInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaptionWithNamedConventions();
            UpsertNamedConventionsToLusid();
            CallLusidInlineValuationEndpoint(swaption, model);
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
    }
}
