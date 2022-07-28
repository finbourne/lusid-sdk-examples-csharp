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
    public class EquityExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        [LusidFeature("F5-20")]
        [Test]
        public void EquityCreationAndUpsertionExample()
        {
            // CREATE an Equity (that can then be upserted into LUSID)
            var equity = (Equity) InstrumentExamples.CreateExampleEquity();
            
            // ASSERT that it was created
            Assert.That(equity, Is.Not.Null);
            
            // CAN NOW UPSERT TO LUSID
            var uniqueId = equity.InstrumentType + Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(equity, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.Equity);
            var roundTripEquity = retrieved as Equity;
            Assert.That(roundTripEquity, Is.Not.Null);
            Assert.That(roundTripEquity.DomCcy, Is.EqualTo(equity.DomCcy));
            Assert.That(roundTripEquity.Identifiers, Is.EqualTo(equity.Identifiers));
            Assert.That(roundTripEquity.InstrumentType, Is.EqualTo(equity.InstrumentType));
            
            // DELETE Instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", identifier: uniqueId); 
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // No complex market data required.
            // SimpleStatic quote upsert done in base class's UpsertMarketDataForInstrument method
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(
            LusidInstrument instrument,
            string scope,
            string portfolioCode,
            string recipeCode,
            string instrumentID)
        {
            // There are no cashflows associated to an equity.
        }
        
        [LusidFeature("F22-39")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        public void EquityValuationExample(ModelSelection.ModelEnum model)
        {
            var equity = InstrumentExamples.CreateExampleEquity();
            CallLusidGetValuationEndpoint(equity, model);
        }
    }
}