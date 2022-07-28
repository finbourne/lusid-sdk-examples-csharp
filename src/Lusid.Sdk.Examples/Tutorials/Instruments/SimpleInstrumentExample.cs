using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class SimpleInstrumentExample: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        [LusidFeature("F5-21")]
        [Test]
        public void SimpleInstrumentCreationAndUpsertionExample()
        {
            // DEFINE scope
            string scope = "ibor";
            
            // CREATE a Simple Instrument Equity (that can then be upserted into LUSID)
            var equity = InstrumentExamples.CreateExampleSimpleInstrument() as SimpleInstrument;
            
            // ASSERT that it was created
            Assert.That(equity, Is.Not.Null);
            
            // CREATE property definition
            try
            {
                var propertyDefinitionRequest = new CreatePropertyDefinitionRequest(
                    domain: CreatePropertyDefinitionRequest.DomainEnum.Instrument,
                    scope: scope,
                    code: "dividendYield",
                    displayName: "Dividend Yield",
                    dataTypeId: new ResourceId(
                        scope: "system",
                        code: "number"
                    ),
                    lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual
                );

                _apiFactory.Api<IPropertyDefinitionsApi>()
                    .CreatePropertyDefinition(createPropertyDefinitionRequest: propertyDefinitionRequest);
            }
            
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            
            // DEFINE properties for Equity example
            decimal dividendYield = (decimal)0.88;
            Property properties = new Property(key: $"Instrument/{scope}/dividendYield", value: new PropertyValue(metricValue: new MetricValue(value: dividendYield)));
            
            // DEFINE equity instrument definition with property
            string name = "Microsoft";
            var uniqueId = equity.InstrumentType + Guid.NewGuid().ToString(); 
            var equityDefinition = new InstrumentDefinition(name: name, identifiers: new Dictionary<string, InstrumentIdValue>{{"ClientInternal", new InstrumentIdValue(uniqueId)}}, definition: equity, properties: new List<Property>{properties});
            
            // UPSERT TO LUSID
            Dictionary<string, InstrumentDefinition> upsertRequest = new Dictionary<string, InstrumentDefinition> {{uniqueId, equityDefinition}};
            var upsertResponse = _instrumentsApi.UpsertInstruments(upsertRequest);
            ValidateUpsertInstrumentResponse(upsertResponse);
            
            // QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments(identifierType: "ClientInternal", requestBody: new List<String> {uniqueId}, propertyKeys:new List<String> {$"Instrument/{scope}/dividendYield"});
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            // ASSERT that equity instrument and properties were retrieved from LUSID
            var retrievedDefinition = getResponse.Values.First().Value.InstrumentDefinition;
            var retrievedProperties = getResponse.Values.First().Value.Properties;
            Assert.That(retrievedDefinition.InstrumentType == LusidInstrument.InstrumentTypeEnum.SimpleInstrument);
            Assert.That(retrievedProperties.First().Key, Is.EqualTo($"Instrument/{scope}/dividendYield"));
            Assert.That(retrievedProperties.First().Value, Is.EqualTo(new PropertyValue(metricValue: new MetricValue(value: dividendYield))));
            var roundTripEquity = retrievedDefinition as SimpleInstrument;
            Assert.That(roundTripEquity, Is.Not.Null);
            Assert.That(roundTripEquity.AssetClass, Is.EqualTo(SimpleInstrument.AssetClassEnum.Equities));
            Assert.That(roundTripEquity.DomCcy, Is.EqualTo("USD"));
            Assert.That(roundTripEquity.InstrumentType, Is.EqualTo(LusidInstrument.InstrumentTypeEnum.SimpleInstrument));
            Assert.That(roundTripEquity.SimpleInstrumentType, Is.EqualTo("Equity"));
            
            // DELETE Instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", identifier: uniqueId); 
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode,
            string instrumentID)
        {
            throw new NotImplementedException();
        }
    }
}