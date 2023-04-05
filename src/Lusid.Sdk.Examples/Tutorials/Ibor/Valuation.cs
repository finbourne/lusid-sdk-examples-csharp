using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Examples.Utilities;
using Lusid.Sdk.Model;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Examples.Tutorials.Ibor
{
    [TestFixture]
    public class Valuations: TutorialBase
    {
        private InstrumentLoader _instrumentLoader;
        private IList<string> _instrumentIds;

        [OneTimeSetUp]
        public void SetUp()
        {
            _instrumentLoader = new InstrumentLoader(ApiFactory);
            _instrumentIds = _instrumentLoader.LoadInstruments();
        }
        
        [LusidFeature("F36")]
        [Test]
        public void Run_Valuation()
        {
            var effectiveDate = new DateTimeOffset(2022, 6, 10, 0, 0, 0, TimeSpan.Zero);

            //    Create the transaction portfolio
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = TransactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));
            
            var transactionSpecs = new[]
                {
                    (Id: _instrumentIds[0], Price: 101, TradeDate: effectiveDate),
                    (Id: _instrumentIds[1], Price: 102, TradeDate: effectiveDate),
                    (Id: _instrumentIds[2], Price: 103, TradeDate: effectiveDate)
                }
                .OrderBy(i => i.Id);

            var newTransactions = transactionSpecs.Select(id => TestDataUtilities.BuildTransactionRequest(id.Id, 100.0M, id.Price, "GBP", id.TradeDate, "Buy"));

            //    Add transactions to the portfolio
            ApiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, portfolioRequest.Code, newTransactions.ToList());

            var scope = Guid.NewGuid().ToString();

            var quotes = new List<(string InstrumentId, decimal Price)>
                {
                    (_instrumentIds[0], 100),
                    (_instrumentIds[1], 200),
                    (_instrumentIds[2], 300)
                }
                .Select(x => new UpsertQuoteRequest(
                    new QuoteId(
                        new QuoteSeriesId(
                            provider: "DataScope",
                            instrumentId: x.InstrumentId,
                            instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                            quoteType: QuoteSeriesId.QuoteTypeEnum.Price, field: "mid"
                        ),
                        effectiveAt: effectiveDate
                    ),
                    metricValue: new MetricValue(
                        value: x.Price,
                        unit: "GBP"
                    )
                ))
                .ToDictionary(k => Guid.NewGuid().ToString());

            //    Create the quotes
            const string recipeScope = "some-recipe-scope";
            var recipe = new ConfigurationRecipe
            (
                scope: recipeScope,
                code: "DataScope_Recipe",
                market: new MarketContext
                {
                    Suppliers = new MarketContextSuppliers
                    {
                        Equity = "DataScope"
                    },
                    Options = new MarketOptions(
                        defaultScope:  scope,
                        defaultSupplier: "DataScope",
                        defaultInstrumentCodeType: "LusidInstrumentId"
                    )
                }
            );

            //    Upload recipe to Lusid (only need to do once, i.e. no need to repeat in non-demo code.)
            var upsertRecipeRequest = new UpsertRecipeRequest(recipe);
            RecipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            //    Upload the quote
            ApiFactory.Api<IQuotesApi>().UpsertQuotes(scope, quotes);

            //    Create the valuation request, this example calculates the percentage of total portfolio value and value by instrument 
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(recipeScope, "DataScope_Recipe"),
                metrics: new List<AggregateSpec>
                {
                    new(TestDataUtilities.InstrumentName, AggregateSpec.OpEnum.Value),
                    new(TestDataUtilities.ValuationPv, AggregateSpec.OpEnum.Proportion),
                    new(TestDataUtilities.ValuationPv, AggregateSpec.OpEnum.Sum),
                    new(TestDataUtilities.ValuationDateKey, AggregateSpec.OpEnum.Value)
                },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveDate),
                groupBy: new List<string> { "Instrument/default/Name" },
                portfolioEntityIds: new List<PortfolioEntityId> { new PortfolioEntityId(TestDataUtilities.TutorialScope, portfolioRequest.Code) }
                );

            //    Do the aggregation
            var results = ApiFactory.Api<AggregationApi>().GetValuation(valuationRequest);

            Assert.That(results.Data, Has.Count.EqualTo(4));
            Assert.That(results.Data[0]["Sum(Valuation/PV/Amount)"], Is.EqualTo(10000));
            Assert.That(results.Data[2]["Sum(Valuation/PV/Amount)"], Is.EqualTo(20000));
            Assert.That(results.Data[3]["Sum(Valuation/PV/Amount)"], Is.EqualTo(30000));
            
            Assert.That(results.Data[0][TestDataUtilities.ValuationDateKey], Is.EqualTo(effectiveDate.DateTime));
        }

    }
}
