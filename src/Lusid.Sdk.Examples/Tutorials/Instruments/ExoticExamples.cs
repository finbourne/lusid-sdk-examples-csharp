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
    public class ExoticExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // Nothing specific to upsert for Exotics.
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
            
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }

        [LusidFeature("F5-13")]
        [Test]
        public void ExoticCreationAndUpsertionExample()
        {
            // CREATE an exotic instrument (that can then be upserted into LUSID)
            var exotic = InstrumentExamples.CreateExampleExotic();

            // ASSERT that it was created
            Assert.That(exotic, Is.Not.Null);
            
            // CAN NOW UPSERT TO LUSID
            var uniqueId = exotic.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(exotic, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            UpsertInstrumentsResponse upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse ,uniqueId);
            
            // CHECK contents
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.ExoticInstrument);
            var roundTripExotic = retrieved as ExoticInstrument;
            Assert.That(roundTripExotic, Is.Not.Null);
            Assert.That(roundTripExotic.Content, Is.EqualTo(exotic.Content));
            Assert.That(roundTripExotic.InstrumentFormat, Is.EqualTo(exotic.InstrumentFormat));
            
            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }
        
        [LusidFeature("F22-13")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        public void ExoticValuationExample(ModelSelection.ModelEnum model)
        {
            var exotic = InstrumentExamples.CreateExampleExotic();
            CallLusidGetValuationEndpoint(exotic, model);
        }
    }
}
