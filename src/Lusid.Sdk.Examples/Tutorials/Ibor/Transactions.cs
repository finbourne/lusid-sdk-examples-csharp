using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Client;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Ibor
{
    [TestFixture]
    public class Transactions: TutorialBase
    {
        private IList<string> _instrumentIds;
        

        [OneTimeSetUp]
        public void OnetimeSetUp()
        {

            
            var instrumentLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = instrumentLoader.LoadInstruments();
        }
        
        internal string _portfolioCode;
        [SetUp]
        public void SetUp()
        {
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
            _portfolioCode = portfolioRequest.Code;
            Assert.That(portfolio?.Id.Code, Is.EqualTo(_portfolioCode));
        }

        [TearDown]
        public void TearDown()
        {
            //_portfoliosApi.DeletePortfolio(_portfolioScope, _portfolioCode); 
        }
        
        [LusidFeature("F17")]
        [Test]
        public void Load_Listed_Instrument_Transaction()
        {
            //    create the transaction request
            var transaction = BuildTransactionRequest();
            
            //    add the transaction
            _transactionPortfoliosApi.UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode, new List<TransactionRequest> {transaction});
            
            //    get the transaction
            var transactions = _transactionPortfoliosApi.GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);
            
            Assert.That(transactions.Values, Has.Count.EqualTo(1));
            Assert.That(transactions.Values[0].TransactionId, Is.EqualTo(transaction.TransactionId));
        }

        [LusidFeature("F18")]
        [Test]
        public void Load_Cash_Transaction()
        {
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);

            //    create the transaction request
            var transaction = new TransactionRequest(

                //    unique transaction id
                transactionId: Guid.NewGuid().ToString(),

                instrumentIdentifiers: new Dictionary<string, string>
                {
                    [TestDataUtilities.LusidCashIdentifier] = "GBP"
                },

                type: "FundsIn",
                totalConsideration: new CurrencyAndAmount(0.0M, "GBP"),
                transactionPrice: new TransactionPrice(0.0M),
                transactionDate: effectiveDate,
                settlementDate: effectiveDate,
                units: 100,
                source: "Custodian");

            //    add the transaction
            _transactionPortfoliosApi.UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode,
                new List<TransactionRequest> { transaction });

            //    get the transaction
            var transactions =
                _transactionPortfoliosApi.GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);

            Assert.That(transactions.Values, Has.Count.EqualTo(1));
            Assert.That(transactions.Values[0].TransactionId, Is.EqualTo(transaction.TransactionId));
        }

        [LusidFeature("F31")]
        [Test]
        public void Cancel_Transactions()
        {
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
            
            //    create the portfolio
            //var portfolioCode = _testDataUtilities.CreateTransactionPortfolio(TestDataUtilities.TutorialScope);
            
            //    create the transaction requests
            var transactionRequests = new[]
            {
                new TransactionRequest(

                    //    unique transaction id
                    transactionId: Guid.NewGuid().ToString(),

                    //    instruments must already exist in LUSID and have a valid LUSID instrument id
                    instrumentIdentifiers: new Dictionary<string, string>
                    {
                        [TestDataUtilities.LusidInstrumentIdentifier] = _instrumentIds[0]
                    },

                    type: "Buy",
                    totalConsideration: new CurrencyAndAmount(1230, "GBP"),
                    transactionDate: effectiveDate,
                    settlementDate: effectiveDate,
                    units: 100,
                    transactionPrice: new TransactionPrice(12.3M),
                    source: "Custodian"),
                new TransactionRequest(

                    //    unique transaction id
                    transactionId: Guid.NewGuid().ToString(),

                    //    instruments must already exist in LUSID and have a valid LUSID instrument id
                    instrumentIdentifiers: new Dictionary<string, string>
                    {
                        [TestDataUtilities.LusidInstrumentIdentifier] = _instrumentIds[0]
                    },

                    type: "Sell",
                    totalConsideration: new CurrencyAndAmount(45, "GBP"),
                    transactionDate: effectiveDate,
                    settlementDate: effectiveDate,
                    units: 50,
                    transactionPrice: new TransactionPrice(20.4M),
                    source: "Custodian")
            };

            //    add the transactions
            _transactionPortfoliosApi.UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode, transactionRequests.ToList());
            
            //    get the transactions
            var transactions = _transactionPortfoliosApi.GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);
            
            Assert.That(transactions.Values, Has.Count.EqualTo(2));
            Assert.That(transactions.Values.Select(t => t.TransactionId), Is.EquivalentTo(transactionRequests.Select(t => t.TransactionId)));

            //    cancel the transactions
            _transactionPortfoliosApi.CancelTransactions(TestDataUtilities.TutorialScope, _portfolioCode, transactions.Values.Select(t => t.TransactionId).ToList());

            //    verify the portfolio is now empty
            var noTransactions = _transactionPortfoliosApi.GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);

            Assert.That(noTransactions.Values, Is.Empty);
        }

        [LusidFeature("F13-4")]
        [Test]
        public void Add_Transactions_To_Portfolio_With_Property()
        {
            // create a transaction request
            var transactionRequest = BuildTransactionRequest();

            // add the transaction property we are testing
            string code = "ExecutingTrader";
            string executingTraderKey = $"Transaction/{TestDataUtilities.TutorialScope}/{code}";
            string executingTraderValue = "Glyn Jagger";
            EnsurePropertyDefinition("Transaction", code);
            transactionRequest.Properties = new Dictionary<string, PerpetualProperty>
            {
                {
                    executingTraderKey,
                    new PerpetualProperty(executingTraderKey, new PropertyValue(labelValue: executingTraderValue))
                }
            };

            // add the transaction
            UpsertPortfolioTransactionsResponse upsertResp = _transactionPortfoliosApi.UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode, new List<TransactionRequest> { transactionRequest });

            // get the transaction and verify the reponse
            var transactions = _transactionPortfoliosApi.GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);

            Assert.That(transactions.Values, Has.Count.EqualTo(1));
            Assert.That(transactions.Values[0].TransactionId, Is.EqualTo(transactionRequest.TransactionId));

            // assert that the custom properties was upserted with the transaction and is returned
            Assert.IsTrue(transactions.Values[0].Properties.ContainsKey(executingTraderKey));
            Assert.That(transactions.Values[0].Properties[executingTraderKey].Value.LabelValue == executingTraderValue);
        }

        private TransactionRequest BuildTransactionRequest()
        {
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return new TransactionRequest(
                // unique transaction id
                transactionId: Guid.NewGuid().ToString(),

                instrumentIdentifiers: new Dictionary<string, string>
                {
                    [TestDataUtilities.LusidInstrumentIdentifier] = _instrumentIds[0]
                },

                type: "Buy",
                totalConsideration: new CurrencyAndAmount(1230, "GBP"),
                transactionDate: effectiveDate,
                settlementDate: effectiveDate,
                units: 100,
                transactionPrice: new TransactionPrice(12.3M),
                source: "Custodian"
            );
        }

        private void EnsurePropertyDefinition(string domain, string code)
        {
            var propertyApi = _apiFactory.Api<IPropertyDefinitionsApi>();

            if (!Enum.TryParse(domain, false, out CreatePropertyDefinitionRequest.DomainEnum domainEnum))
                throw new ArgumentException($"Specified domain:{domain} is invalid.", "domain");

            try
            {
                propertyApi.GetPropertyDefinition(domain, TestDataUtilities.TutorialScope, code);
            }
            catch (ApiException apiEx)
            {
                if (apiEx.ErrorCode == 404) {
                    //    Property definition doesn't exist (returns 404), so create one
                    //    Details of the property to be created
                    var propertyDefinition = new CreatePropertyDefinitionRequest(
                        domain: domainEnum,
                        scope: TestDataUtilities.TutorialScope,
                        lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual,
                        code: code,
                        valueRequired: false,
                        displayName: code,
                        dataTypeId: new ResourceId("system", "string")
                    );

                    //    Create the property
                    propertyApi.CreatePropertyDefinition(propertyDefinition);
                }
                else {  
                    throw apiEx; 
                }
            }
        }
    }
}