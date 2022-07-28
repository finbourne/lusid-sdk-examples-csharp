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
    public class ContractForDifferencesExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope,ModelSelection.ModelEnum model, LusidInstrument cfd)
        {
            var equityRequest = TestDataUtilities.BuildEquityQuoteRequest(
                instrumentId: "some-id",
                effectiveFrom:new DateTimeOffset(2019, 2, 7, 0, 0, 0, TimeSpan.Zero),
                effectiveAt:new DateTimeOffset(2020, 4, 3, 0, 0, 0, TimeSpan.Zero),
                instrumentIdType:QuoteSeriesId.InstrumentIdTypeEnum.RIC
            );
            var upsertEquityResponse = _quotesApi.UpsertQuotes(scope, equityRequest);
            Assert.That(upsertEquityResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertEquityResponse.Values.Count, Is.EqualTo(equityRequest.Count));

            Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));

                var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode,
            string recipeCode, string instrumentID)
        {
            var cfd = (ContractForDifference) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                windowStart: cfd.StartDate.AddDays(-3),
                windowEnd: cfd.MaturityDate.AddDays(3), 
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            Assert.That(cashflows.Count, Is.EqualTo(1));
            Assert.That(cashflows[0].Currency, Is.EqualTo(cfd.PayCcy));
        }

        [LusidFeature("F5-9")]
        [Test]
        public void CfdCreationAndUpsertionExample()
        {
            // CREATE an CFD (that can then be upserted into LUSID)
            var cfd = (ContractForDifference) InstrumentExamples.CreateExampleCfd();

            // ASSERT that it was created
            Assert.That(cfd, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = cfd.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>(){(cfd, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);

            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);

            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.ContractForDifference);
            var roundTripCfd = retrieved as ContractForDifference;
            Assert.That(roundTripCfd, Is.Not.Null);
            Assert.That(roundTripCfd.PayCcy, Is.EqualTo(cfd.PayCcy));
            Assert.That(roundTripCfd.Code, Is.EqualTo(cfd.Code));
            Assert.That(roundTripCfd.ReferenceRate, Is.EqualTo(cfd.ReferenceRate));
            Assert.That(roundTripCfd.StartDate, Is.EqualTo(cfd.StartDate));
            Assert.That(roundTripCfd.UnderlyingCcy, Is.EqualTo(cfd.UnderlyingCcy));

            // DELETE instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }

        [LusidFeature("F22-3")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void CfdValuationExample(ModelSelection.ModelEnum model)
        {
            var cfd = InstrumentExamples.CreateExampleCfd();
            CallLusidGetValuationEndpoint(cfd, model);
        }

        [LusidFeature("F22-4")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void CfdInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var cfd = InstrumentExamples.CreateExampleCfd();
            CallLusidInlineValuationEndpoint(cfd, model);
        }

        [TestCase(ModelSelection.ModelEnum.Discounting)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        public void CfdPortfolioCashFlowsExample(ModelSelection.ModelEnum model)
        {
            var cfd = InstrumentExamples.CreateExampleCfd();
            CallLusidGetPortfolioCashFlowsEndpoint(cfd, model);
        }
    }
}