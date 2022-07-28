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
    public class FutureExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // Nothing to upsert specifically for Futures.
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
            
            Assert.That(cashflows.Count, Is.EqualTo(2));
        }

        [LusidFeature("F5-14")]
        [Test]
        public void FuturesCreationAndUpsertionExample()
        {
            // CREATE a future instrument (that can then be upserted into LUSID)
            var future = InstrumentExamples.CreateExampleFuture();

            // ASSERT that it was created
            Assert.That(future, Is.Not.Null);
            
            // CAN NOW UPSERT TO LUSID
            var uniqueId = future.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(future, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse ,uniqueId);
            
            // CHECK contents
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.Future);
            var roundTripFuture = retrieved as Future;
            Assert.That(roundTripFuture, Is.Not.Null);
            Assert.That(roundTripFuture.StartDate, Is.EqualTo(future.StartDate));
            Assert.That(roundTripFuture.RefSpotPrice, Is.EqualTo(future.RefSpotPrice));
            Assert.That(roundTripFuture.MaturityDate, Is.EqualTo(future.MaturityDate));
            Assert.That(roundTripFuture.Contracts, Is.EqualTo(future.Contracts));
            Assert.That(roundTripFuture.ContractDetails.Description, Is.EqualTo(future.ContractDetails.Description));
            Assert.That(roundTripFuture.ContractDetails.ContractMonth, Is.EqualTo(future.ContractDetails.ContractMonth));
            Assert.That(roundTripFuture.Underlying.InstrumentType, Is.EqualTo(future.Underlying.InstrumentType));
            Assert.That(roundTripFuture.Underlying.InstrumentType, Is.EqualTo(LusidInstrument.InstrumentTypeEnum.ExoticInstrument));
            
            // DELETE instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId); 
        }
        
        [LusidFeature("F22-14")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        public void FutureValuationExample(ModelSelection.ModelEnum model)
        {
            var future = InstrumentExamples.CreateExampleFuture();
            CallLusidGetValuationEndpoint(future, model);
        }
    }
}
