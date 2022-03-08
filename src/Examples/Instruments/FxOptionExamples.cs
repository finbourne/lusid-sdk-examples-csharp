using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Examples.Utilities;
using Lusid.Sdk.Model;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Examples.Examples.Instruments
{
    [TestFixture]
    public class FxOptionExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument fxOption)
        {
            // POPULATE with required market data for valuation of the instruments
            var upsertFxRateRequestreq = TestDataUtilities.BuildFxRateRequest(TestDataUtilities.EffectiveAt);
            var upsertQuoteResponse = _quotesApi.UpsertQuotes(scope, upsertFxRateRequestreq);
            
            ValidateQuoteUpsert(upsertQuoteResponse, upsertFxRateRequestreq.Count);

            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                foreach (var kv in TestDataUtilities.BuildRateCurvesRequests(TestDataUtilities.EffectiveAt))
                {
                    upsertComplexMarketDataRequest.Add(kv.Key, kv.Value);
                }
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
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId });
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
    }
}
