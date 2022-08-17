using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using NUnit.Framework;

namespace Lusid.Sdk.Tests
{
    [TestFixture]
    public class LusidApiTests
    {
        private ILusidApiFactory _apiFactory;
       
        [OneTimeSetUp]
        public void SetUp()
        {
            _apiFactory = TestLusidApiFactoryBuilder.CreateApiFactory("secrets.json");
        }

        [Test]
        public void Get_Schema_For_Response()
        {
            // GIVEN an instance of the LUSID client, and a portfolio created in LUSID

            const string scope = "finbourne";
            var code = $"id-{Guid.NewGuid()}";

            var request = new CreateTransactionPortfolioRequest($"Portfolio-{code}", code: code, baseCurrency: "GBP");

            _apiFactory.Api<ITransactionPortfoliosApi>().CreatePortfolio(scope, request);

            // WHEN the portfolio is queried
            var portfolioResponse = _apiFactory.Api<IPortfoliosApi>().GetPortfolio(scope, code);

            // THEN the result should include a schema Url
            var schemaUrl = portfolioResponse.Links.First(l => l.Relation == "EntitySchema").Href;

            // AND which we we can use to query for the schema of the entity
            // TODO: Too difficult to convert the returned Url into parameters for the call to GetSchema
            Regex regex = new Regex(".+/(\\w+)");
            var entityType = regex.Match(schemaUrl);

            var schema = _apiFactory.Api<ISchemasApi>().GetEntitySchema(entityType.Groups[1].Value);

            Assert.That(schema, Is.Not.Null);
            Assert.That(schema.Values, Is.Not.Empty);

            var fields = typeof(Portfolio).GetProperties().Select(p => p.Name).ToImmutableHashSet();
            foreach (var fieldSchema in schema.Values)
            {
                Assert.That(fields, Does.Contain(fieldSchema.Value.DisplayName));
            }
        }
        
    }
    
}
 