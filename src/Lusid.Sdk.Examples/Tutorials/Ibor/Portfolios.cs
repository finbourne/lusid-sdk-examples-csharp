using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Ibor
{
    /// <summary>
    /// Set up to create a ILusidApiFactory which is used to make calls to the
    /// LUSID API.  A
    /// </summary>
    [TestFixture]
    public class Portfolios: TutorialBase
    {
        private IList<string> _instrumentIds;
        private InstrumentLoader _instrumentLoader;
        //    This defines the scope that entities will be created in
        
        internal string _portfolioCode;
        internal string _portfolioScope;
        
        [SetUp]
        public void SetUp()
        {
            _portfolioScope = TestDataUtilities.TutorialScope;
            _instrumentLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = _instrumentLoader.LoadInstruments();
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));
            _portfolioCode = portfolioRequest.Code;
        }

        [TearDown]
        public void TearDown()
        {
            _portfoliosApi.DeletePortfolio(_portfolioScope, _portfolioCode); 
        }
        
        [LusidFeature("F8")]
        [Test]
        public void Create_Transaction_Portfolio()
        {
            var uuid = Guid.NewGuid().ToString();
            
            //    Details of the new portfolio to be created, created here with the minimum set of mandatory fields 
            var request = new CreateTransactionPortfolioRequest(
                
                //    Unique portfolio code, portfolio codes must be unique across scopes
                code: $"id-{uuid}",
                
                //    Descriptive name for the portfolio
                displayName: $"Portfolio-{uuid}",                 
                
                baseCurrency: "GBP"
            );

            //    Create the portfolio in LUSID
            var portfolio = _apiFactory.Api<ITransactionPortfoliosApi>().CreatePortfolio(_portfolioScope, request);

            //    Confirm that the portfolio was successfully created.  Any failures will result in
            //    a ApiException being thrown which contain the relevant response code and error message
            Assert.That(portfolio.Id.Code, Is.EqualTo(request.Code));
        }
        
        [LusidFeature("F9")]
        [Test]
        public void Create_Transaction_Portfolio_With_Properties()
        {
            var uuid = Guid.NewGuid().ToString();
            var propertyName = $"fund-style-{uuid}";
            var dataTypeId = new ResourceId("system", "string");

            //    Property definition
            var propertyDefinition = new CreatePropertyDefinitionRequest(
                domain: CreatePropertyDefinitionRequest.DomainEnum.Portfolio,
                scope: TestDataUtilities.TutorialScope,
                code: propertyName,
                valueRequired: false,
                displayName: "Fund Style",
                dataTypeId: dataTypeId,
                lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual);

            //    Create the property definition
            var propertyDefinitionResult =
                _apiFactory.Api<IPropertyDefinitionsApi>().CreatePropertyDefinition(propertyDefinition);
            
            //    Property value
            var propertyValue = "Active";
            
            var portfolioProperty = new Property(
                key: propertyDefinitionResult.Key,
                value: new PropertyValue(labelValue: propertyValue));
            
            //    Properties to add to portfolio on creation
            var properties = new Dictionary<String, Property>
            {
                [propertyDefinitionResult.Key] = portfolioProperty
            };
            
            //    Details of the portfolio to be created
            var request = new CreateTransactionPortfolioRequest(
                displayName: $"portfolio-{uuid}",
                code: $"id-{uuid}",
                baseCurrency: "GBP",
                //    Set the property value when creating the portfolio
                properties: properties
                );

            var portfolio = _apiFactory.Api<ITransactionPortfoliosApi>().CreatePortfolio(
                scope: TestDataUtilities.TutorialScope, 
                createTransactionPortfolioRequest: request);

            var portfolioCode = portfolio.Id.Code;

            Assert.That(portfolioCode, Is.EqualTo(request.Code));

            var portfolioProperties = _apiFactory.Api<IPortfoliosApi>().GetPortfolioProperties(
                scope: TestDataUtilities.TutorialScope,
                code: portfolioCode);
            
            Assert.That(portfolioProperties.Properties.Count, Is.EqualTo(1));
            Assert.That(portfolioProperties.Properties[propertyDefinitionResult.Key].Value.LabelValue, Is.EqualTo(propertyValue));
            
        }
            
            
        [LusidFeature("F10")]
        [Test]
        public void Add_Transactions_To_Portfolio()
        {
            //    Effective date of the trades. All dates/times must be supplied in UTC
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);

            //    Create a portfolio
            //var portfolioId = _testDataUtilities.CreateTransactionPortfolio(TutorialScope);

            //    Details of the transaction to be added       
            var transaction = new TransactionRequest(
                
                //    Unique transaction id
                transactionId: Guid.NewGuid().ToString(),
                
                //    Transaction type, configured during system setup
                type: "Buy",
                
                //    Instrument identifier for the trnasaction
                instrumentIdentifiers: new Dictionary<string, string>
                {
                    ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                },  
                
                transactionDate: effectiveDate,
                settlementDate: effectiveDate,
                units: 100,
                transactionPrice: new TransactionPrice(12.3M, TransactionPrice.TypeEnum.Price),
                totalConsideration: new CurrencyAndAmount(1230, "GBP"),
                source: "Broker"
            );
            //    Add the transaction to the portfolio
            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode, new List<TransactionRequest> {transaction});
            
            //    Retrieve the transaction
            var transactions = _apiFactory.Api<ITransactionPortfoliosApi>().GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);                
            
            Assert.That(transactions.Values.Count, Is.EqualTo(1));
            Assert.That(transactions.Values[0].InstrumentUid, Is.EqualTo(transaction.InstrumentIdentifiers.First().Value));           
        }
        
        [LusidFeature("F11")]
        [Test]
        public void Add_Transactions_To_Portfolio_With_Property()
        {
            var uuid = Guid.NewGuid().ToString();
            var propertyName = $"fund-style-{uuid}";
            var labelValue = "A Trader";

            //    Effective date of the trades. All dates/times must be supplied in UTC
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
            
            //    Details of the property to be created
            var propertyDefinition = new CreatePropertyDefinitionRequest(
                
                //    The domain the property is to be applied to
                domain: CreatePropertyDefinitionRequest.DomainEnum.Transaction,
                
                //    The scope the property will be created in
                scope: TestDataUtilities.TutorialScope,
                
                //    When the property value is set it will be valid forever and cannot be changed.
                //    Properties whose values can change over time should be created with LifeTimeEnum.TIMEVARIANT
                lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual,
                
                code: propertyName,
                valueRequired: false,
                displayName: "Trader Id",
                dataTypeId: new ResourceId("system", "string")
            );
            
            //    Create the property definition
            var propertyDefinitionResult = _apiFactory.Api<IPropertyDefinitionsApi>().CreatePropertyDefinition(propertyDefinition);
            
            //    Create the property value
            var propertyValue = new PerpetualProperty(propertyDefinitionResult.Key, new PropertyValue(labelValue));

            //    Create a portfolio
            //var portfolioId = _testDataUtilities.CreateTransactionPortfolio(TutorialScope);

            //    Details of the transaction to be added       
            var transaction = new TransactionRequest(
                
                //    Unique transaction id
                transactionId: Guid.NewGuid().ToString(),
                
                //    Transaction type, configured during system setup
                type: "Buy",
                
                //    Instrument identifier for the trnasaction
                instrumentIdentifiers: new Dictionary<string, string>
                {
                    ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                },  
                
                //    The properties to be added to the transaction
                properties: new Dictionary<string, PerpetualProperty>
                {
                    [propertyDefinitionResult.Key] = propertyValue
                },
                
                transactionDate: effectiveDate,
                settlementDate: effectiveDate,
                units: 100,
                transactionPrice: new TransactionPrice(12.3M, TransactionPrice.TypeEnum.Price),
                totalConsideration: new CurrencyAndAmount(1230, "GBP"),
                source: "Custodian"
            );

            //    Add the transaction to the portfolio
            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, _portfolioCode, new List<TransactionRequest> {transaction});
            
            //    Retrieve the transaction
            var transactions = _apiFactory.Api<ITransactionPortfoliosApi>().GetTransactions(TestDataUtilities.TutorialScope, _portfolioCode);                
            
            Assert.That(transactions.Values.Count, Is.EqualTo(1));
            Assert.That(transactions.Values[0].InstrumentUid, Is.EqualTo(transaction.InstrumentIdentifiers.First().Value));
            Assert.That(transactions.Values[0].Properties[propertyDefinitionResult.Key].Value.LabelValue, Is.EqualTo(labelValue));
        }
        
        [LusidFeature("F2-4")]
        [Test]
        public void List_Portfolios()
        {
            //    This defines the scope that the portfolios will be retrieved from
            var scope = $"{TestDataUtilities.TutorialScope}-{Guid.NewGuid().ToString()}";
            
            //    Set up some sample portfolios
            for (var i = 0; i < 10; i++)
            {
                //_testDataUtilities.CreateTransactionPortfolio(scope);
                var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
                var portfolio = _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);
                Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));
            }
            
            //    Retrieve the list of portfolios from a given scope           
            var portfolios = _apiFactory.Api<IPortfoliosApi>().ListPortfoliosForScope(scope);
            
            Assert.That(portfolios.Values.Count(), Is.EqualTo(10));

        }
        
        [LusidFeature("F12")]
        [Test]
        public void List_Scopes()
        {
            //    Get the list of scopes across all entities
            var scopes = _apiFactory.Api<IScopesApi>().ListScopes();

            Assert.That(scopes.Values.Count(), Is.GreaterThan(0));
        }

    }
}
