using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Examples.Utilities;
using Lusid.Sdk.Model;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Examples.Ibor
{

    [TestFixture]
    public class ReferencePortfolioTests: TutorialBase
    {
        private IList<string> _instrumentIds;

        //  This defines the scope that entities will be created in the test
        const string TutorialScope = "Testdemo1";
        private readonly DateTimeOffset _createDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [OneTimeSetUp]
        public void SetUp()
        {
            //  Initialise the API factory

            // Load default instruments which will be used in the tests
            var instrumentsLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = instrumentsLoader.LoadInstruments().OrderBy(x => x).ToList();
        }
        
        [LusidFeature("F39")]
        [Test]
        public void CreateReferencePortfolio()
        {
            var f39PortfolioCode = Guid.NewGuid().ToString();
            var f39PortfolioName = "F39C_Reference Portfolio name";


            //  Details of the new reference portfolio to be created
            var request = new CreateReferencePortfolioRequest(

                //  Unique portfolio code, portfolio codes must be unique across scopes
                code: f39PortfolioCode,

                //  Descriptive name for the portfolio
                displayName: f39PortfolioName,

                //  Create date for the below. We can only upsert constituents after this date.
                created: _createDate

            );

            //  Create the reference portfolio in LUSID
            var referencePortfolio = _apiFactory.Api<IReferencePortfolioApi>().CreateReferencePortfolio(TutorialScope, request);

            //  Confirm that the portfolio was successfully created.  Any failures will result in
            //  a ApiException being thrown which contain the relevant response code and error message
            Assert.That(referencePortfolio.Id.Code, Is.EqualTo(request.Code));

            // Delete the portfolio once the test is complete
            _apiFactory.Api<IPortfoliosApi>().DeletePortfolio(TutorialScope, f39PortfolioCode);
        }
        
        [LusidFeature("F40")]
        [Test]
        public void SetConstituents()
        {
            var f40PortfolioCode = Guid.NewGuid().ToString();
            var f40PortfolioName = "F40C_Test reference portfolio";

            //  First, create a new reference portfolio
            var request = new CreateReferencePortfolioRequest(

                //  Unique portfolio code, portfolio codes must be unique across scopes
                code: f40PortfolioCode,

                //  Descriptive name for the portfolio
                displayName: f40PortfolioName,

                //  Create date for the below. We can only upsert constituents after this date.
                created: _createDate

            );

            //  Create the reference portfolio in LUSID
            var referencePortfolio = _apiFactory.Api<IReferencePortfolioApi>().CreateReferencePortfolio(TutorialScope, request);

            // Declare some instruments which we loaded earlier
            var instrument1 = _instrumentIds[0];
            var instrument2 = _instrumentIds[1];
            var instrument3 = _instrumentIds[2];

            // Set date for the constituents, the date from which the constituents are valid
            var date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

            // Weighting type
            var weighting = UpsertReferencePortfolioConstituentsRequest.WeightTypeEnum.Static;

            // Create the request to upsert constituents
            var upsertConstituentsRequest = new UpsertReferencePortfolioConstituentsRequest(

                effectiveFrom: date,
                weightType: weighting,
                constituents: new List<ReferencePortfolioConstituentRequest>
                {

                    new ReferencePortfolioConstituentRequest(
                        instrumentIdentifiers: new Dictionary<string, string> { [TestDataUtilities.LusidCashIdentifier] = "GBP"},
                        weight: 10,
                        currency: "GBP"
                    ),

                    new ReferencePortfolioConstituentRequest(
                        instrumentIdentifiers: new Dictionary<string, string> {[TestDataUtilities.LusidInstrumentIdentifier] = instrument1},
                        weight: 20,
                        currency: "GBP"
                    ),
                     new ReferencePortfolioConstituentRequest(
                        instrumentIdentifiers: new Dictionary<string, string> {[TestDataUtilities.LusidInstrumentIdentifier] = instrument2},
                        weight: 30,
                        currency: "GBP"
                    ),

                     new ReferencePortfolioConstituentRequest(
                        instrumentIdentifiers: new Dictionary<string, string> {[TestDataUtilities.LusidInstrumentIdentifier] = instrument3},
                        weight: 40,
                        currency: "GBP"
                    ),
                }
            );
            // Upsert the constituents into LUSID
            var setConstituents = _apiFactory.Api<IReferencePortfolioApi>().UpsertReferencePortfolioConstituents(TutorialScope, f40PortfolioCode, upsertConstituentsRequest);

            var constituentHoldings = _apiFactory.Api<IReferencePortfolioApi>().GetReferencePortfolioConstituents(TutorialScope, f40PortfolioCode, date);

            // Check the count of holdings
            Assert.That(constituentHoldings.Constituents.Count(), Is.EqualTo(4));

            // Check the instruments on the holdings
            constituentHoldings.Constituents.Sort((h1, h2) => String.Compare(h1.InstrumentUid, h2.InstrumentUid, StringComparison.Ordinal));
            Assert.That(constituentHoldings.Constituents[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(constituentHoldings.Constituents[2].InstrumentUid, Is.EqualTo(_instrumentIds[1]));
            Assert.That(constituentHoldings.Constituents[3].InstrumentUid, Is.EqualTo(_instrumentIds[2]));

            //  Validate the holding weights
            Assert.That(constituentHoldings.Constituents[0].Weight, Is.EqualTo(10));
            Assert.That(constituentHoldings.Constituents[1].Weight, Is.EqualTo(20));
            Assert.That(constituentHoldings.Constituents[2].Weight, Is.EqualTo(30));
            Assert.That(constituentHoldings.Constituents[3].Weight, Is.EqualTo(40));

            // Delete the portfolio once the test is complete
             _apiFactory.Api<IPortfoliosApi>().DeletePortfolio(TutorialScope, f40PortfolioCode);
        }
    }
}