using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.MarketData
{
    public class MarketDataRules : TutorialBase
    {
        /// <summary>
        /// See <see cref="MarketDataSpecificRule"/>.
        ///
        /// In this test we demonstrate the use of market data specific rules in order to instruct LUSID to use
        /// different market data sources in instrument valuation, depending on the properties of the instrument.
        /// In particular, we show how the inclusion of a market data specific rule can cause LUSID to prefer
        /// data in a special scope whenever it needs an underlying asset price for USD-denominated equity options.
        /// </summary>
        [LusidFeature("F11-8")]
        [Test]
        public void DemoMarketDataSpecificRules()
        {
            var recipeScope = nameof(DemoMarketDataSpecificRules);
            
            // set up scopes: one for recipes, one for holding quotes from a generic source, and one for holding specific quotes that will override ones from the generic scope
            var genericScope = nameof(DemoMarketDataSpecificRules) + "genericScope"; // this might be a general scope for storing equity prices
            var specificScope = nameof(DemoMarketDataSpecificRules) + "specificScope";

            // create an equity call option for TSLA to value
            var testNow = new DateTimeOffset(2019, 01, 01, 0, 0, 0, TimeSpan.Zero);
            var instrument = new EquityOption(startDate: testNow.AddMonths(-1), optionMaturityDate: testNow.AddMonths(+1), optionSettlementDate: testNow.AddMonths(+1), deliveryType: "Cash",
                optionType: "Call", strike: 90m, domCcy: "USD", underlyingIdentifier: "RIC", code: "TSLA", instrumentType: LusidInstrument.InstrumentTypeEnum.EquityOption);

            // upsert two quotes with different values: one is upserted to the generic scope, one is upserted to the specific scope
            var quotesToPutInGenericScope = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(quotesToPutInGenericScope, "TSLA-fallback", "TSLA", QuoteSeriesId.InstrumentIdTypeEnum.RIC, 100m, "USD", testNow, QuoteSeriesId.QuoteTypeEnum.Price);
            var genericQuoteResponse = _quotesApi.UpsertQuotes(genericScope, quotesToPutInGenericScope);
            ValidateQuoteUpsert(genericQuoteResponse, quotesToPutInGenericScope.Count);
            var quotesToPutInSpecificScope = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(quotesToPutInSpecificScope, "TSLA-override", "TSLA", QuoteSeriesId.InstrumentIdTypeEnum.RIC, 120m, "USD", testNow, QuoteSeriesId.QuoteTypeEnum.Price);
            var specificQuoteResponse = _quotesApi.UpsertQuotes(specificScope, quotesToPutInSpecificScope);
            ValidateQuoteUpsert(specificQuoteResponse, quotesToPutInSpecificScope.Count);

            // instruct lusid to value equity options with ConstantTimeValueOfMoney (i.e. intrinsic value)
            var modelRules = new VendorModelRule(VendorModelRule.SupplierEnum.Lusid, "ConstantTimeValueOfMoney", "EquityOption", "{}");
            var pricingContext = new PricingContext(modelRules: new List<VendorModelRule> {modelRules});

            // make two market data rules:
            // the first is a generic rule that all RIC prices should be looked for in the generic scope
            // the second is a specific rule that all RIC prices requested by USD-denominated EquityOption instruments should be looked for in the specific scope
            var genericRule = new MarketDataKeyRule("Quote.RIC.*", "Lusid", genericScope, MarketDataKeyRule.QuoteTypeEnum.Price, "mid");
            var specificRuleForUsdEquityOptions = new MarketDataSpecificRule("Quote.RIC.*", "Lusid", specificScope, MarketDataSpecificRule.QuoteTypeEnum.Price, "mid",
                dependencySourceFilter: new DependencySourceFilter(instrumentType: "EquityOption", assetClass: null, domCcy: "USD"));

            // Upsert a generic recipe containing out generic rule that will find the equity spot from the generic scope
            var mktContextWithNoSpecificRule = new MarketContext(options: new MarketOptions(defaultScope: genericScope), marketRules: new List<MarketDataKeyRule> {genericRule});
            var recipeWithNoSpecificRule = new ConfigurationRecipe(
                recipeScope,
                "WithNoSpecificRules",
                mktContextWithNoSpecificRule,
                pricingContext,
                description: $"Should use market data contained in {genericScope}"
            ); ;
            var genericRecipeResponse = _recipeApi.UpsertConfigurationRecipe(new UpsertRecipeRequest(recipeWithNoSpecificRule));
            Assert.That(genericRecipeResponse.Value, Is.Not.Null);

            // Upsert a recipe additionally containing our specific rule that will find the equity spot from the specific scope instead
            // The MarketDataSpecificRule takes priority over all MarketDataKeyRules;
            // if the requested quote is not found via specific rules, then quote will be resolved by the generic rules as a fallback
            var mktContextWithSpecificRule = new MarketContext(options: new MarketOptions(defaultScope: genericScope), marketRules: new List<MarketDataKeyRule> {genericRule},
                specificRules: new List<MarketDataSpecificRule> {specificRuleForUsdEquityOptions});
            var recipeWithSpecificRule = new ConfigurationRecipe(
                recipeScope,
                "ContainsSpecificRules",
                mktContextWithSpecificRule,
                pricingContext,
                description: $"Should override the market data contained in {genericScope} with a quote contained in {specificScope}"
            );
            var specificRecipeResponse = _recipeApi.UpsertConfigurationRecipe(new UpsertRecipeRequest(recipeWithSpecificRule));
            Assert.That(specificRecipeResponse.Value, Is.Not.Null);

            // Get PVs according to our two recipes, and check that the appropriate values were computed for each recipe
            var pvWithNoSpecificRule = PerformValuation(recipeWithNoSpecificRule.Code);
            var pvWithSpecificRule = PerformValuation(recipeWithSpecificRule.Code);
            Assert.That(pvWithNoSpecificRule, Is.EqualTo(10)); // strike is 90, spot quote is 100 in the generic scope
            Assert.That(pvWithSpecificRule, Is.EqualTo(30)); // strike is 90, spot quote is 120 in the specific scope

            double? PerformValuation(string recipeName)
            {
                // CREATE the aggregation request
                var aggReq = new InlineValuationRequest(
                    new ResourceId(recipeScope, recipeName),
                    valuationSchedule: new ValuationSchedule(effectiveAt: testNow.ToString("o")),
                    metrics: TestDataUtilities.ValuationSpec,
                    instruments: new List<WeightedInstrument> {new WeightedInstrument(1m, "myOption", instrument)}
                );

                // GET aggregation results
                var aggResults = _aggregationApi.GetValuationOfWeightedInstruments(aggReq);
                Assert.That(aggResults.AggregationFailures, Is.Empty);
                var pv = aggResults.Data.First()[TestDataUtilities.ValuationPv] as double?;
                return pv;
            }
    }

    }
}
