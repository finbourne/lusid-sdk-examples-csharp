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
    public class ForwardRateAgreementExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // Nothing to upsert specifically for Forward Rate Agreements (for now - ANA-1301).
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode, string instrumentID)
        {
            // ANA-1301
        }

        [LusidFeature("F5-24")]
        [Test]
        public void ForwardRateAgreementCreationAndUpsertionExample()
        {
            // CREATE a future instrument (that can then be upserted into LUSID)
            var forwardRateAgreement = (ForwardRateAgreement) InstrumentExamples.CreateExampleForwardRateAgreement();

            // ASSERT that it was created
            Assert.That(forwardRateAgreement, Is.Not.Null);
            
            // CAN NOW UPSERT TO LUSID
            var uniqueId = forwardRateAgreement.InstrumentType + Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(forwardRateAgreement, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId });
            ValidateInstrumentResponse(getResponse ,uniqueId);
            
            // CHECK contents
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.ForwardRateAgreement);
            var roundTripFuture = retrieved as ForwardRateAgreement;
            Assert.That(roundTripFuture, Is.Not.Null);
            Assert.That(roundTripFuture.StartDate, Is.EqualTo(forwardRateAgreement.StartDate));
            Assert.That(roundTripFuture.MaturityDate, Is.EqualTo(forwardRateAgreement.MaturityDate));
            Assert.That(roundTripFuture.FixingDate, Is.EqualTo(forwardRateAgreement.FixingDate));
            Assert.That(roundTripFuture.FraRate, Is.EqualTo(forwardRateAgreement.FraRate));
            Assert.That(roundTripFuture.DomCcy, Is.EqualTo(forwardRateAgreement.DomCcy));
            
            // DELETE instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId); 
        }
        
        [LusidFeature("F22-50")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        public void ForwardRateAgreementValuationExample(ModelSelection.ModelEnum model)
        {
            var fra = InstrumentExamples.CreateExampleForwardRateAgreement();
            CallLusidGetValuationEndpoint(fra, model);
        }
    }
}
