using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Lusid.Sdk.Api;
using Lusid.Sdk.Client;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.MarketData
{
    [TestFixture]
    public class CorporateActions : TutorialBase
    {
        private IList<string> _instrumentIds;
        private string _corpActionTestSource;

        [OneTimeSetUp]
        public void SetUp()
        {
            var instrumentsLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = instrumentsLoader.LoadInstruments().OrderBy(x => x).ToList();

            var uuid = Guid.NewGuid().ToString();
            _corpActionTestSource = $"ca_source-{uuid}";

            try
            {
                _corporateActionSourcesApi.CreateCorporateActionSource(
                    new CreateCorporateActionSourceRequest(
                        TestDataUtilities.TutorialScope,
                        _corpActionTestSource,
                        "Test Source",
                        "Corporate Actions source used for automated testing"
                        )
                    );
            } 
            catch (ApiException ex)
            {
                var errorObj = JsonDocument.Parse(ex.ErrorContent.ToString());

                //If the corporate action source already exists, simply return
                if (errorObj.RootElement.GetProperty("code").ToString() == "173")
                    return;
                else
                    throw;
            }            
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            try
            {
                _corporateActionSourcesApi.DeleteCorporateActionSource(
                        TestDataUtilities.TutorialScope,
                        _corpActionTestSource
                    );
            }
            catch (ApiException ex)
            {
                var errorObj = JsonDocument.Parse(ex.ErrorContent.ToString());

                //Return if the source does not exist
                if (errorObj.RootElement.GetProperty("code").ToString() == "391")
                    return;
                else
                    throw;
            }
        }
        
        [LusidFeature("F12-6")]
        [Test]
        public void List_Corporate_Action_Sources()
        {
            var sources = _corporateActionSourcesApi.ListCorporateActionSources();
            Assert.Greater(sources.Values.Count, 0);
        }

        [Test, Ignore("Not implemented")]
        public void List_Corporate_Actions_For_One_Day()
        {
            var result = _corporateActionSourcesApi.GetCorporateActions(
                scope: "UK_High_Growth_Equities_Fund_a4fb",
                code: "UK_High_Growth_Equities_Fund_base_fund_corporate_action_source"
            );

        }

        [LusidFeature("F12-2")]
        [Test]
        public void Create_Dividend_Payment()
        {
            var startDate = new DateTimeOffset(2021, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var announcementDate = new DateTimeOffset(2021, 09, 6, 0, 0, 0, TimeSpan.Zero);
            var exDate = new DateTimeOffset(2021, 09, 20, 0, 0, 0, TimeSpan.Zero);
            var recordDate = new DateTimeOffset(2021, 09, 21, 0, 0, 0, TimeSpan.Zero);
            var paymentDate = new DateTimeOffset(2021, 10, 5, 0, 0, 0, TimeSpan.Zero);

            var preExDate = new DateTimeOffset(2021, 09, 15, 0, 0, 0, TimeSpan.Zero);
            var prePaymentDate = new DateTimeOffset(2021, 10, 4, 0, 0, 0, TimeSpan.Zero);
            var postPaymentDate = new DateTimeOffset(2021, 10, 12, 0, 0, 0, TimeSpan.Zero);

            var uuid = Guid.NewGuid().ToString();

            var portfolioCode = $"id-{uuid}";
            var transactions = new List<TransactionRequest>();

            var currencyLuid = "CCY_GBP";

            // Create the portfolio
            var request = new CreateTransactionPortfolioRequest(
                code: portfolioCode,
                displayName: $"Portfolio-{uuid}",
                baseCurrency: "GBP",
                created: startDate,
                corporateActionSourceId: new ResourceId(
                    scope: TestDataUtilities.TutorialScope,
                    code: _corpActionTestSource
                    )
            );
            _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, request);

            // Add starting cash position
            transactions.Add(TestDataUtilities.BuildCashFundsInTransactionRequest(2960000, "GBP", startDate));

            // Add an equity position
            transactions.Add(TestDataUtilities.BuildTransactionRequest(_instrumentIds[0], 132000, 5, "GBP", startDate, "StockIn"));

            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, portfolioCode, transactions);

            // Upsert Corporate Action
            var identifierMappingInput = new Dictionary<string, string>();
            identifierMappingInput.Add("Instrument/default/LusidInstrumentId", _instrumentIds[0]);

            var identifierMappingOutput = new Dictionary<string, string>();
            identifierMappingOutput.Add("Instrument/default/Currency", "GBP");

            var stockSplitCorpActionRequest = new UpsertCorporateActionRequest(
                TestDataUtilities.TutorialScope,
                "Dividend Payment",
                announcementDate,
                exDate,
                recordDate,
                paymentDate,
                new List<CorporateActionTransitionRequest>()
                {
                    new CorporateActionTransitionRequest()
                    {
                        InputTransition = new CorporateActionTransitionComponentRequest (identifierMappingInput,1,0),
                        OutputTransitions = new List<CorporateActionTransitionComponentRequest>()
                        {
                            new CorporateActionTransitionComponentRequest(identifierMappingOutput,(decimal)0.5,0)
                        }
                    }
                });

            _corporateActionSourcesApi.BatchUpsertCorporateActions(TestDataUtilities.TutorialScope, _corpActionTestSource, new List<UpsertCorporateActionRequest>() { stockSplitCorpActionRequest });

            //
            // fetch holdings pre ex-dividend date
            //
            var holdingsResultPreExDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: preExDate);
            var holdingsPreExDate = holdingsResultPreExDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            // check for 2 holdings records
            Assert.That(holdingsPreExDate.Count(), Is.EqualTo(2));

            // check cash balance hasn't changed
            Assert.That(holdingsPreExDate[0].InstrumentUid, Is.EqualTo(currencyLuid));
            Assert.That(holdingsPreExDate[0].Units, Is.EqualTo(2960000));
            Assert.That(holdingsPreExDate[0].HoldingType, Is.EqualTo("B"));

            //  check stock quantity hasn't changed
            Assert.That(holdingsPreExDate[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPreExDate[1].Units, Is.EqualTo(132000));


            //
            // fetch holdings after the ex-dividend date but before the payment date
            //
            var holdingsResultPostExDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: prePaymentDate);
            var holdingsPostExDate = holdingsResultPostExDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            // check for 3 holdings records (will include an accrued amount seperately)
            Assert.That(holdingsPostExDate.Count(), Is.EqualTo(3));

            //  check we still have a 'Cash Balance' holding type in the amount of 2960000
            Assert.That(holdingsPostExDate[0].InstrumentUid, Is.EqualTo(currencyLuid));
            Assert.That(holdingsPostExDate[0].Units, Is.EqualTo(2960000));
            Assert.That(holdingsPostExDate[0].HoldingType, Is.EqualTo("B"));

            //  check we have a 'Cash Accrual' in the amount of 66000
            Assert.That(holdingsPostExDate[1].InstrumentUid, Is.EqualTo(currencyLuid));
            Assert.That(holdingsPostExDate[1].Units, Is.EqualTo(66000));
            Assert.That(holdingsPostExDate[1].HoldingType, Is.EqualTo("A"));

            //  check stock quantity hasn't changed
            Assert.That(holdingsPostExDate[2].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPostExDate[2].Units, Is.EqualTo(132000));


            //
            // fetch holdings after the the payment date
            //
            var holdingsResultPostPayDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: postPaymentDate);
            var holdingsPostPayDate = holdingsResultPostPayDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            // check for 2 holdings records (accrued amount now realized)
            Assert.That(holdingsPostPayDate.Count(), Is.EqualTo(2));

            //  check cash balance has increased by the dividend amount
            Assert.That(holdingsPostPayDate[0].InstrumentUid, Is.EqualTo(currencyLuid));
            Assert.That(holdingsPostPayDate[0].Units, Is.EqualTo(3026000));
            Assert.That(holdingsPostPayDate[0].HoldingType, Is.EqualTo("B"));

            //  check stock quantity hasn't changed
            Assert.That(holdingsPostPayDate[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPostPayDate[1].Units, Is.EqualTo(132000));
        }

        [LusidFeature("F12-3")]
        [Test]
        public void Create_Stock_Split()
        {
            var startDate = new DateTimeOffset(2021, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var announcementDate = new DateTimeOffset(2021, 09, 4, 0, 0, 0, TimeSpan.Zero);
            var exDate = new DateTimeOffset(2021, 09, 6, 0, 0, 0, TimeSpan.Zero);
            var recordDate = new DateTimeOffset(2021, 09, 20, 0, 0, 0, TimeSpan.Zero);
            var paymentDate = new DateTimeOffset(2021, 09, 22, 0, 0, 0, TimeSpan.Zero);

            var postSplitDate = new DateTimeOffset(2021, 9, 24, 0, 0, 0, TimeSpan.Zero);

            var uuid = Guid.NewGuid().ToString();

            var portfolioCode = $"id-{uuid}";
            var transactions = new List<TransactionRequest>();

            // Create the portfolio
            var request = new CreateTransactionPortfolioRequest(
                code: portfolioCode,
                displayName: $"Portfolio-{uuid}",
                baseCurrency: "GBP",
                created: startDate,
                corporateActionSourceId: new ResourceId(
                    scope: TestDataUtilities.TutorialScope,
                    code: _corpActionTestSource
                    )
            );
            _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, request);

            // Add starting cash position
            transactions.Add(TestDataUtilities.BuildCashFundsInTransactionRequest(2960000, "GBP", startDate));

            // Add an equity position
            transactions.Add(TestDataUtilities.BuildTransactionRequest(_instrumentIds[0], 132000, 5, "GBP", startDate, "StockIn"));

            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, portfolioCode, transactions);

            // Upsert Corporate Action   
            var identifierMapping = new Dictionary<string, string>();
            identifierMapping.Add("Instrument/default/LusidInstrumentId", _instrumentIds[0]);

            var stockSplitCorpActionRequest = new UpsertCorporateActionRequest(
                TestDataUtilities.TutorialScope,
                "Stock Split",
                announcementDate,
                exDate,
                recordDate,
                paymentDate,
                new List<CorporateActionTransitionRequest>()
                {
                    new CorporateActionTransitionRequest()
                    {
                        InputTransition = new CorporateActionTransitionComponentRequest (identifierMapping,1,1),
                        OutputTransitions = new List<CorporateActionTransitionComponentRequest>()
                        {
                            new CorporateActionTransitionComponentRequest(identifierMapping,2,1)
                        }
                    }
                });

            _corporateActionSourcesApi.BatchUpsertCorporateActions(TestDataUtilities.TutorialScope, _corpActionTestSource, new List<UpsertCorporateActionRequest>() { stockSplitCorpActionRequest });

            //
            // fetch holdings pre ex-dividend date
            //
            var holdingsResultPreExDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: announcementDate);
            var holdingsPreExDate = holdingsResultPreExDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            //  check stock quantity hasn't changed
            Assert.That(holdingsPreExDate[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPreExDate[1].Units, Is.EqualTo(132000));
            Assert.That(holdingsPreExDate[1].SettledUnits, Is.EqualTo(132000));


            //
            // fetch holdings post ex-dividend date but pre pay date
            //
            var holdingsResultPostExDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: recordDate);
            var holdingsPostExDate = holdingsResultPostExDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            //  check stock quantity has doubled for unsettled units
            Assert.That(holdingsPostExDate[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPostExDate[1].Units, Is.EqualTo(264000));
            Assert.That(holdingsPostExDate[1].SettledUnits, Is.EqualTo(132000));


            //
            // fetch holdings post pay date
            //
            var holdingsResultPostPayDate = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: postSplitDate);
            var holdingsPostPayDate = holdingsResultPostPayDate.Values.OrderBy(h => h.InstrumentUid).ToList();

            //  check stock quantity has doubled for settled and unseltted units
            Assert.That(holdingsPostPayDate[1].InstrumentUid, Is.EqualTo(_instrumentIds[0]));
            Assert.That(holdingsPostPayDate[1].Units, Is.EqualTo(264000));
            Assert.That(holdingsPostPayDate[1].SettledUnits, Is.EqualTo(264000));
        }

        [LusidFeature("F12-4")]
        [Test]
        public void Process_Name_Change_By_Transitions()
        {
            var uuid = Guid.NewGuid().ToString();

            var portfolioCode = $"id-{uuid}";
            var transactions = new List<TransactionRequest>();

            var originalInstrument = (Figi: "BBG000C6K6G9", Name: "VODAFONE GROUP PLC");
            var newInstrument = (Figi: "BB5555555555", Name: "VODAFONE INCORPORATED");

            // Define details for the corporate action.
            var instruments = new List<(string Figi, string Name)>
            {
                originalInstrument,
                newInstrument
            };
            var initialDate = new DateTimeOffset(2021, 9, 1, 0, 0, 0, TimeSpan.Zero);
            var nameChangeDate = new DateTimeOffset(2021, 9, 2, 0, 0, 0, TimeSpan.Zero);
            var postNameChangeDate = new DateTimeOffset(2021, 9, 3, 0, 0, 0, TimeSpan.Zero);

            // Upsert Instruments
            var upsertResponse = _apiFactory.Api<IInstrumentsApi>().UpsertInstruments(instruments.ToDictionary(
                k => k.Figi,
                v => new InstrumentDefinition(
                    name: v.Name,
                    identifiers: new Dictionary<string, InstrumentIdValue> { ["Figi"] = new InstrumentIdValue(v.Figi) }
                )
            ));

            var instResponse = _apiFactory.Api<IInstrumentsApi>().GetInstruments("Figi", instruments.Select(i => i.Figi).ToList());
            var luidOriginal = instResponse.Values.Where(i => i.Key == originalInstrument.Figi).Select(i => i.Value.LusidInstrumentId).First();
            var luidNew = instResponse.Values.Where(i => i.Key == newInstrument.Figi).Select(i => i.Value.LusidInstrumentId).First();

            // Create the portfolio
            var request = new CreateTransactionPortfolioRequest(
                code: portfolioCode,
                displayName: $"Portfolio-{uuid}",
                baseCurrency: "GBP",
                created: initialDate,
                corporateActionSourceId: new ResourceId(
                    scope: TestDataUtilities.TutorialScope,
                    code: _corpActionTestSource
                    )
            );
            _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, request);

            // Add a transaction for the original instrument
            transactions.Add(TestDataUtilities.BuildTransactionRequest(luidOriginal, 60000, 122.0M, "GBP", initialDate, "StockIn"));
            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, portfolioCode, transactions);

            // Upsert Corporate Action
            var identifierMappingInput = new Dictionary<string, string>();
            identifierMappingInput.Add("Instrument/default/LusidInstrumentId", luidOriginal);

            var identifierMappingOutputOriginal = new Dictionary<string, string>();
            identifierMappingOutputOriginal.Add("Instrument/default/LusidInstrumentId", luidOriginal);

            var identifierMappingOutputNew = new Dictionary<string, string>();
            identifierMappingOutputNew.Add("Instrument/default/LusidInstrumentId", luidNew);

            var stockSplitCorpActionRequest = new UpsertCorporateActionRequest(
                TestDataUtilities.TutorialScope,
                "Name Change",
                nameChangeDate,
                nameChangeDate,
                nameChangeDate,
                nameChangeDate,
                new List<CorporateActionTransitionRequest>()
                {
                    new CorporateActionTransitionRequest()
                    {
                        InputTransition = new CorporateActionTransitionComponentRequest (identifierMappingInput,1,1),
                        OutputTransitions = new List<CorporateActionTransitionComponentRequest>()
                        {
                            new CorporateActionTransitionComponentRequest(identifierMappingOutputOriginal,0,0),
                            new CorporateActionTransitionComponentRequest(identifierMappingOutputNew,1,1)
                        }
                    }
                });

            _corporateActionSourcesApi.BatchUpsertCorporateActions(TestDataUtilities.TutorialScope, _corpActionTestSource, new List<UpsertCorporateActionRequest>() { stockSplitCorpActionRequest });

            // Fetch our holdings before the name change date
            var holdingsPreNameChange = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: initialDate);
            var holdingsPreNameChangeList = holdingsPreNameChange.Values.OrderBy(h => h.InstrumentUid).ToList();

            Assert.That(holdingsPreNameChangeList[0].InstrumentUid, Is.EqualTo(luidOriginal));

            // Fetch our holdings after the name change date
            var holdingsPostNameChange = _transactionPortfoliosApi.GetHoldings(TestDataUtilities.TutorialScope, portfolioCode, effectiveAt: postNameChangeDate);
            var holdingsPostNameChangeList = holdingsPostNameChange.Values.OrderBy(h => h.InstrumentUid).ToList();

            Assert.That(holdingsPostNameChangeList[0].InstrumentUid, Is.EqualTo(luidNew));

            _apiFactory.Api<IInstrumentsApi>().DeleteInstrument("Figi", originalInstrument.Figi);
            _apiFactory.Api<IInstrumentsApi>().DeleteInstrument("Figi", newInstrument.Figi);
        }
    }
}