using System;
using System.Collections.Generic;
using System.Linq;
using Castle.Core.Internal;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using NUnit.Framework;

namespace Lusid.Sdk.Utilities
{
    public class TutorialBase
    {
        internal readonly ILusidApiFactory _apiFactory;
        internal readonly ITransactionPortfoliosApi _transactionPortfoliosApi;
        internal readonly IInstrumentsApi _instrumentsApi;
        internal readonly IQuotesApi _quotesApi;
        internal readonly IConfigurationRecipeApi _recipeApi;
        internal readonly IComplexMarketDataApi _complexMarketDataApi;
        internal readonly IAggregationApi _aggregationApi;
        internal readonly IPortfoliosApi _portfoliosApi;
        internal readonly ICutLabelDefinitionsApi _cutLabelDefinitionsApi;
        internal readonly IOrdersApi _ordersApi;
        internal readonly ICorporateActionSourcesApi _corporateActionSourcesApi;
        internal readonly IConventionsApi _conventionsApi;
        internal readonly IStructuredResultDataApi _structuredResultDataApi;
        internal readonly IPropertyDefinitionsApi _propertyDefinitionsApi;
        
        public TutorialBase()
        {
            // Initialize all the API end points
            _apiFactory = TestLusidApiFactoryBuilder.CreateApiFactory("secrets.json");
            _portfoliosApi = _apiFactory.Api<IPortfoliosApi>();
            _transactionPortfoliosApi = _apiFactory.Api<ITransactionPortfoliosApi>();
            _instrumentsApi = _apiFactory.Api<IInstrumentsApi>();
            _quotesApi = _apiFactory.Api<IQuotesApi>();
            _complexMarketDataApi = _apiFactory.Api<IComplexMarketDataApi>();
            _recipeApi = _apiFactory.Api<IConfigurationRecipeApi>();
            _aggregationApi = _apiFactory.Api<IAggregationApi>();
            _cutLabelDefinitionsApi = _apiFactory.Api<CutLabelDefinitionsApi>();
            _ordersApi = _apiFactory.Api<IOrdersApi>();
            _corporateActionSourcesApi = _apiFactory.Api<ICorporateActionSourcesApi>();
            _conventionsApi = _apiFactory.Api<IConventionsApi>();
            _structuredResultDataApi = _apiFactory.Api<IStructuredResultDataApi>();
            _propertyDefinitionsApi = _apiFactory.Api<IPropertyDefinitionsApi>();
        }
        
        internal void ValidateUpsertInstrumentResponse(UpsertInstrumentsResponse response)
        {
            Assert.That(response.Failed.Count, Is.EqualTo(0));
            Assert.That(response.Values.Count, Is.EqualTo(1));
        }
        
        internal void ValidateInstrumentResponse(GetInstrumentsResponse response, string uniqueId)
        {
            Assert.That(response.Failed.Count, Is.EqualTo(0));
            Assert.That(response.Values.Count, Is.EqualTo(1));
            Assert.That(response.Values.First().Key, Is.EqualTo(uniqueId));
        }

        internal void ValidateQuoteUpsert(UpsertQuotesResponse upsertResponse, int count)
        {
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(count));
        }
        
        internal void ValidateComplexMarketDataUpsert(UpsertStructuredDataResponse upsertResponse, int count)
        {
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(count));
        }

        internal void CreateAndUpsertRecipe(string code, string scope, ModelSelection.ModelEnum model, bool windowValuationOnInstrumentStartEnd = false)
        {
            // CREATE pricing context for recipe
            var pricingOptions = new PricingOptions(new ModelSelection(ModelSelection.LibraryEnum.Lusid, model));
            pricingOptions.WindowValuationOnInstrumentStartEnd = windowValuationOnInstrumentStartEnd;
            
            // DEFINE rules to pick up reset quotes
            var resetRule = new MarketDataKeyRule("Quote.RIC.*", "Lusid", scope , MarketDataKeyRule.QuoteTypeEnum.Price, "mid", quoteInterval: "1Y");
            
            // CREATE recipe
            var recipe = new ConfigurationRecipe(
                scope,
                code,
                market: new MarketContext(
                    marketRules: new List<MarketDataKeyRule>{resetRule},
                    options: new MarketOptions(defaultScope: scope)),
                pricing: new PricingContext(options: pricingOptions),
                description: $"Recipe for {model} pricing");

            UpsertRecipe(recipe);
        }

        private void UpsertRecipe(ConfigurationRecipe recipe)
        {
            // UPSERT recipe and check upsert was successful
            var upsertRecipeRequest = new UpsertRecipeRequest(recipe);
            var response = _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);
            Assert.That(response.Value, Is.Not.Null);
        }
        
        /// <summary>
        /// This method adds the instruments to the portfolio and populates required market data for the pricing for examples.
        /// This method contains a several steps:
        ///
        /// 1) We first upsert our instruments by UpsertInstrumentSetAndReturnResponseValues.
        /// Inside this method, it first creates the instruments and upsert them using the InstrumentApi.
        /// Then it returns the upsert response.
        ///
        /// 2) The upsert response contains the LUIDs for the instruments upserted.
        /// Using the LUIDs, we add them to the portfolio by the method AddInstrumentsTransactionToPortfolio.
        /// The effective from argument is to specify when we want the instruments to be effective date from
        ///
        /// 3) To value the instrument in our example set, we populate with relevant market data.
        /// Fx rates (JPY/USD, UDS/JPY) are upserted for Fx-Forward, Fx-Option pricing
        /// Rate Curves are upserted for discounting pricing of Fx-Forward as well as for pricing swaps.
        /// Equity price quote is also upserted if an identifier is supplied.
        /// </summary> 
        public void AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
            string scope,
            string portfolioCode,
            DateTimeOffset effectiveFrom,
            DateTimeOffset effectiveAt,
            List<LusidInstrument> instruments,
            string equityIdentifier = null,
            bool useConstantFxRate = false)
        {
            // UPSERT instruments and return the upsert response to attain LusidInstrumentIds
            var instrumentID = Guid.NewGuid().ToString();

            var instrumentsIds = instruments.Select(x => (x, x.InstrumentType + instrumentID)).ToList();
            
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            if (!equityIdentifier.IsNullOrEmpty())
            {
                // MERGE into one upsert dictionary of instruments to upsert
                var equityRequest = new Dictionary<string, InstrumentDefinition>()
                {
                    {equityIdentifier, new InstrumentDefinition(
                        equityIdentifier,
                        new Dictionary<string, InstrumentIdValue>
                            {{"ClientInternal", new InstrumentIdValue(equityIdentifier)}})}
                };
                equityRequest.ToList().ForEach(r => definitions.Add(r.Key, r.Value));
            }
            var response = _instrumentsApi.UpsertInstruments(definitions).Values; 
            var luids = response
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();
            
            // CREATE transaction request to book the instrument onto the portfolio via their LusidInstrumentId
            var transactionRequest = TestDataUtilities.BuildTransactionRequest(luids, effectiveFrom);
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest);

            // UPSERT FX quotes and rate curves required pricing instruments
            var upsertFxRateRequestReq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, effectiveFrom, effectiveAt, useConstantFxRate);
            var upsertQuoteResponse = _quotesApi.UpsertQuotes(scope, upsertFxRateRequestReq);
            
            ValidateQuoteUpsert(upsertQuoteResponse, upsertFxRateRequestReq.Count);

            var upsertQuoteRequests = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                upsertQuoteRequests,
                "resetQuote",
                TestDataUtilities.VanillaSwapFixingReference,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                150,
                "InterestRate",
                effectiveAt.AddDays(-4),
                QuoteSeriesId.QuoteTypeEnum.Price);
            
            var upsertResponse = _quotesApi.UpsertQuotes(scope, upsertQuoteRequests);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(upsertQuoteRequests.Count));

            Dictionary<string, UpsertComplexMarketDataRequest> complexMarketUpsertRequests =
                new Dictionary<string, UpsertComplexMarketDataRequest>(); 
            complexMarketUpsertRequests.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
            complexMarketUpsertRequests.Add("discount_curve_JPY", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "OIS", TestDataUtilities.ExampleDiscountFactors1));
            complexMarketUpsertRequests.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));

            var upsertMarketResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, complexMarketUpsertRequests);
            ValidateComplexMarketDataUpsert(upsertMarketResponse, complexMarketUpsertRequests.Count);

            // UPSERT equity quotes, if an equityIdentifier is present
            if (!equityIdentifier.IsNullOrEmpty())
            {
                var luid = response[equityIdentifier].LusidInstrumentId;
                var upsertEquityQuoteRequests = TestDataUtilities.BuildEquityQuoteRequest(luid, effectiveFrom, effectiveAt, QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId);
                var upsertEquityResponse = _quotesApi.UpsertQuotes(scope, upsertEquityQuoteRequests);
                Assert.That(upsertEquityResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertEquityResponse.Values.Count, Is.EqualTo(upsertEquityQuoteRequests.Count));
            }

            // For equity options, we upsert the reset price in which the option payoff is computed.
            var equityOptions = instruments
                .Where(inst => inst.InstrumentType == LusidInstrument.InstrumentTypeEnum.EquityOption)
                .Select(eqOpt => (EquityOption) eqOpt);
            foreach (var eqOpt in equityOptions)
            {
                // CREATE equity quotes for the desired date range
                var upsertEqOptionQuoteRequests = new Dictionary<string, UpsertQuoteRequest>();
                var numberOfDaysBetween = (effectiveAt - effectiveFrom).Days;

                for (var days = 0; days != numberOfDaysBetween + 1; ++days)
                {
                    var date = effectiveFrom.AddDays(days);
                    var quote = new UpsertQuoteRequest(
                        new QuoteId(
                            new QuoteSeriesId("Lusid", null, eqOpt.Code, QuoteSeriesId.InstrumentIdTypeEnum.RIC, QuoteSeriesId.QuoteTypeEnum.Price,
                                "mid"),
                            date
                        ),
                        new MetricValue(100 + days, "USD"));
                        
                    upsertEqOptionQuoteRequests.Add($"day_{days}_equity_quote", quote);
                }

                // CHECK quotes upsert was successful for all the quotes
                var upsertEqOptionResponse = _quotesApi.UpsertQuotes(scope, upsertEqOptionQuoteRequests);
                Assert.That(upsertEqOptionResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertEqOptionResponse.Values.Count, Is.EqualTo(upsertEqOptionQuoteRequests.Count));
            }
        }
    }
}
