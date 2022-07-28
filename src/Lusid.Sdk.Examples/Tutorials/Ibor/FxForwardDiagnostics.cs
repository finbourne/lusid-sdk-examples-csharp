using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.tutorials.Ibor
{
    public class FxForwardDiagnostics
    {
        [LusidFeature("F22-55")]
        [Test]
        public void DemoInterpolationApproachesForFxForwardValuation()
        {
            // Goal: Value an fx forward using three models and show that they are consistent in their implied fx forward rates
            // The first model values the fx forward using discount curves in the two currencies
            // The second model uses the domestic discount curve, plus specified fx forward rates
            // The third model uses the domestic discount curve, plus a set of fx forward rates that can be interpolated against
            // In the first model, the "Valuation/Diagnostics/FxForwardRate" key shows the implied fx forward rate from the two discount curves
            // In the second model, the key stores the fx forward rate that was specified to be used in the valuation
            // In the third model, the key stores the fx forward rate that was interpolated from the fx forward curve

            var apiFactory = LusidApiFactoryBuilder.Build("secrets.json");

            IQuotesApi quotesApi = apiFactory.Api<IQuotesApi>();
            IComplexMarketDataApi complexMarketDataApi = apiFactory.Api<IComplexMarketDataApi>();
            IConfigurationRecipeApi recipeApi = apiFactory.Api<IConfigurationRecipeApi>();

            string ValuationDateKey = "Analytic/default/ValuationDate";
            string InstrumentTag = "Analytic/default/InstrumentTag";
            string HoldingPvKey = "Holding/default/PV";
            string ImpliedFwdRateKey = "Valuation/Diagnostics/FxForwardRate";
            List<AggregateSpec> valuationSpec = new List<AggregateSpec>
            {
                new AggregateSpec(ValuationDateKey, AggregateSpec.OpEnum.Value),
                new AggregateSpec(InstrumentTag, AggregateSpec.OpEnum.Value),
                new AggregateSpec(HoldingPvKey, AggregateSpec.OpEnum.Value), // fx forward rate diagnostic data is not populated unless we request pricing for the fx forward
                new AggregateSpec(ImpliedFwdRateKey, AggregateSpec.OpEnum.Value)
            };

            var scope = "DemoInterpolationOfFxForwardRatesDuringValuation";
            var testEffectiveAt = new DateTimeOffset(2021, 09, 01, 0, 0, 0, TimeSpan.Zero);

            // UPSERT three recipes for valuation, each instructing fx forward curves to be valued using a particular model.
            // The first the discount curves of the two currencies to interpolate the fair fx forward rate at the valuation date.
            // The second uses the domestic discount curve, plus an fx forward rate that we will specify.
            // The last uses the domestic discount curve, plus a set of fx forward rates that can be interpolated against.
            var discountingRecipeCode = "DiscountingRecipe";
            CreateAndUpsertRecipe(discountingRecipeCode, ModelSelection.ModelEnum.Discounting);
            var specifiedRateRecipeCode = "SpecifiedRateRecipe";
            CreateAndUpsertRecipe(specifiedRateRecipeCode, ModelSelection.ModelEnum.ForwardSpecifiedRate);
            var interpolateFromCurveRecipeCode = "InterpolateFromCurveRecipe";
            CreateAndUpsertRecipe(interpolateFromCurveRecipeCode, ModelSelection.ModelEnum.ForwardFromCurve);

            // POPULATE stores with the required market data to value our fx forward
            CreateAndUpsertFx(testEffectiveAt, 81);
            CreateAndUpsertRateCurves(testEffectiveAt);
            CreateAndUpsertSpecifiedRates(testEffectiveAt); // provides rates for Nov 1 2021 and Jan 1 2022
            CreateAndUpsertFxForwardCurve(testEffectiveAt); // provides a curve with the same rates as in the specified rates

            // CREATE some Fx-Forwards starting on June 1 as an inline instrument: (we will be valuing on September 1)
            var fxFwdStartDate = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var example5MFxForward = CreateFxForward(fxFwdStartDate, fxFwdStartDate.AddMonths(5), 80); // expires Nov 1, 2021
            var example7MFxForward = CreateFxForward(fxFwdStartDate, fxFwdStartDate.AddMonths(7), 79); // expires Jan 1, 2022
            var instruments = new List<WeightedInstrument>
            {
                new WeightedInstrument(1, "5M-fx-forward", example5MFxForward),
                new WeightedInstrument(1, "7M-fx-forward", example7MFxForward)
            };

            // CREATE valuation schedule to value our fx forward on September 1
            var valuationSchedule = new ValuationSchedule(effectiveAt: testEffectiveAt);

            // CREATE inline valuation requests and CALL valuation for Fx-Forward with each recipe
            var discountingInlineValuationRequest = CreateInlineValuationRequest(discountingRecipeCode);
            var discountingValuation = apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(discountingInlineValuationRequest);

            var specifiedRateInlineValuationRequest = CreateInlineValuationRequest(specifiedRateRecipeCode);
            var specifiedRateValuation = apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(specifiedRateInlineValuationRequest);

            var fromCurveInlineValuationRequest = CreateInlineValuationRequest(interpolateFromCurveRecipeCode);
            var fromCurveValuation = apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(fromCurveInlineValuationRequest);

            // ASSERT that the PVs are not null and give the same result (including implied fx fwd rate) regardless of the model
            Assert.That(discountingValuation, Is.Not.Null);
            Assert.That(specifiedRateValuation, Is.Not.Null);
            Assert.That(fromCurveValuation, Is.Not.Null);
            Assert.That(discountingValuation.Data.First()[HoldingPvKey], Is.EqualTo(specifiedRateValuation.Data.First()[HoldingPvKey]).Within(1e-8));
            Assert.That(discountingValuation.Data.First()[HoldingPvKey], Is.EqualTo(fromCurveValuation.Data.First()[HoldingPvKey]).Within(1e-3));
            Assert.That(discountingValuation.Data.First()[ImpliedFwdRateKey], Is.EqualTo(specifiedRateValuation.Data.First()[ImpliedFwdRateKey]).Within(1e-8));

            // CREATE a 6M forward for which we don't explicitly know the current fx forward rate
            var example6MFxForward = CreateFxForward(fxFwdStartDate, fxFwdStartDate.AddMonths(6), 79.5m);
            instruments.Add(new WeightedInstrument(1, "6M-fx-forward", example6MFxForward));

            // The specifiedRate pricer will not be able to find an Fx forward rate to price the 6M forward with
            // But the discounting model is able to imply the appropriate rate from the two discount curves (via linear interpolation of the discount factors for each currency)
            // Note that the implied 6M fwd rate is not the exact average of the given 5M and 7M rates because we are interpolating the two discount factors, NOT the fwd rate itself
            var interpolatedDiscountingValuation = apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(discountingInlineValuationRequest);
            Assert.That(interpolatedDiscountingValuation, Is.Not.Null);

            // The fx forward curve can also be interpolated, giving slightly different results due to differences in the interpolation methodology, day count, and the interpretation of tenors
            // However, the result is quite close to the average of the 5M and 7M rates
            var interpolateFromCurveValuation = apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(fromCurveInlineValuationRequest);
            Assert.That(fromCurveValuation, Is.Not.Null);

            #region testhelpers
            FxForward CreateFxForward(DateTimeOffset startDate, DateTimeOffset maturityDate, decimal agreedRate)
            {
                return new FxForward(
                    domAmount: 1m,
                    fgnAmount: -agreedRate,
                    domCcy: "AUD",
                    fgnCcy: "JPY",
                    refSpotRate: 82m,
                    startDate: startDate,
                    maturityDate: maturityDate,
                    fixingDate: maturityDate.AddDays(-2),
                    isNdf: true,
                    instrumentType: LusidInstrument.InstrumentTypeEnum.FxForward
                );
            }

            void CreateAndUpsertRecipe(string code, ModelSelection.ModelEnum model)
            {
                // CREATE a rule for finding fx forward curves
                var fxFwdCurveRule = new MarketDataKeyRule(key: "FxForwards.*.*.*", supplier: "Lusid", dataScope: scope,
                    quoteType: MarketDataKeyRule.QuoteTypeEnum.Rate, field: "mid", quoteInterval: "1Y", mask: "AUD/JPY/FxFwdCurve");

                // CREATE recipe for pricing
                var pricingOptions = new PricingOptions(new ModelSelection(ModelSelection.LibraryEnum.Lusid, model));
                var recipe = new ConfigurationRecipe(
                    scope: scope,
                    code: code,
                    market: new MarketContext(marketRules: new List<MarketDataKeyRule> {fxFwdCurveRule},
                        options: new MarketOptions(defaultScope: scope)),
                    pricing: new PricingContext(options: pricingOptions),
                    description: $"Recipe for {model} pricing");

                var upsertRecipeRequest = new UpsertRecipeRequest(recipe);
                var response = recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);
                Assert.That(response.Value, Is.Not.Null);
            }

            void CreateAndUpsertFx(DateTimeOffset effectiveAt, decimal rate)
            {
                var fxRate = new UpsertQuoteRequest(
                    new QuoteId(
                        new QuoteSeriesId("Lusid", null, "AUD/JPY", QuoteSeriesId.InstrumentIdTypeEnum.CurrencyPair,
                            QuoteSeriesId.QuoteTypeEnum.Price,
                            "mid"),
                        effectiveAt
                    ),
                    new MetricValue(rate, "AUD"));
                var upsertQuoteRequests = new Dictionary<string, UpsertQuoteRequest>()
                {
                    {"fxRate_atValuation", fxRate}
                };
                var upsertResponse = quotesApi.UpsertQuotes(scope, upsertQuoteRequests);
                Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertResponse.Values.Count, Is.EqualTo(upsertQuoteRequests.Count));
            }

            void CreateAndUpsertRateCurves(DateTimeOffset effectiveAt)
            {
                var discountDates = new List<DateTimeOffset>
                { effectiveAt, effectiveAt.AddMonths(3), effectiveAt.AddMonths(6), effectiveAt.AddMonths(9), effectiveAt.AddMonths(12) };
                var jpyOisRates = new List<decimal>
                { 1.0m, 0.992548449440757m, 0.985152424487251m, 0.977731146620901m, 0.970365774179742m };
                var audOisRates = new List<decimal>
                { 1.0m, 0.995026109593975m, 0.990076958773721m, 0.985098445011387m, 0.980144965261876m };

                var jpyCurve = new DiscountFactorCurveData(baseDate: effectiveAt, dates: discountDates, discountFactors: jpyOisRates, marketDataType: ComplexMarketData.MarketDataTypeEnum.DiscountFactorCurveData);
                var audCurve = new DiscountFactorCurveData(baseDate: effectiveAt, dates: discountDates, discountFactors: audOisRates, marketDataType: ComplexMarketData.MarketDataTypeEnum.DiscountFactorCurveData);

                var upsertRequests = new Dictionary<string, UpsertComplexMarketDataRequest>
                {
                    {"jpy_ois", UpsertRequestFromDiscountCurveData(jpyCurve, "JPY")},
                    {"aud_ois", UpsertRequestFromDiscountCurveData(audCurve, "AUD")}
                };

                // CHECK upsert was successful
                var upsertResponse = complexMarketDataApi.UpsertComplexMarketData(scope, upsertRequests);
                Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertResponse.Values.Values.Count, Is.EqualTo(upsertRequests.Count));

                UpsertComplexMarketDataRequest UpsertRequestFromDiscountCurveData(ComplexMarketData curveData, string currency)
                {
                    var complexMarketDataId = new ComplexMarketDataId(
                        provider: "Lusid",
                        effectiveAt: effectiveAt.ToString("o"),
                        marketAsset: $"{currency}/{currency}OIS",
                        priceSource: "",
                        lineage: "FxForwardCurveInterpolationDemo");
                    return new UpsertComplexMarketDataRequest(complexMarketDataId, curveData);
                }
            }

            void CreateAndUpsertSpecifiedRates(DateTimeOffset effectiveAt)
            {
                var quotes = new List<(string InstrumentId, decimal Price)>
                    {
                        ("AUD/JPY/FxFwdRate/20211101", 81.135483043337885m),
                        ("AUD/JPY/FxFwdRate/20220101", 81.271959646081811m),
                    };
                var upsertRequests = quotes
                    .Select(x => new UpsertQuoteRequest(
                        new QuoteId(
                            new QuoteSeriesId(
                                provider: "Lusid",
                                instrumentId: x.InstrumentId,
                                instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                                quoteType: QuoteSeriesId.QuoteTypeEnum.Price, field: "mid"
                            ),
                            effectiveAt: effectiveAt
                        ),
                        metricValue: new MetricValue(
                            value: x.Price,
                            unit: "JPY"
                        )
                    ))
                    .ToDictionary(k => Guid.NewGuid().ToString());

                var upsertResponse = quotesApi.UpsertQuotes(scope, upsertRequests);
                Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertResponse.Values.Values.Count, Is.EqualTo(upsertRequests.Count));
            }

            void CreateAndUpsertFxForwardCurve(DateTimeOffset effectiveAt)
            {
                var tenors = new List<string>
                {
                    "2M", // i.e. Nov 1 2021, the expiry of the first fx forward
                    "4M" // i.e. Jan 1 2022, the expiry of the second fx forward
                };
                var rates = new List<decimal>
                {
                    81.135483043337885m,
                    81.271959646081811m
                };

                var curveData = new FxForwardTenorCurveData(baseDate: effectiveAt, domCcy: "AUD", fgnCcy: "JPY", tenors: tenors, rates: rates, marketDataType: ComplexMarketData.MarketDataTypeEnum.FxForwardTenorCurveData);
                var upsertRequest = new UpsertComplexMarketDataRequest
                (
                    marketDataId: new ComplexMarketDataId
                    (
                        provider: "Lusid",
                        priceSource: "",
                        lineage: "FxForwardCurveInterpolationDemo",
                        effectiveAt: effectiveAt.ToString("o"),
                        marketAsset: "AUD/JPY/FxFwdCurve"
                    ),
                    marketData: curveData
                );
                var upsertRequests = new Dictionary<string, UpsertComplexMarketDataRequest> { {"0", upsertRequest} };

                var upsertResponse = complexMarketDataApi.UpsertComplexMarketData(
                    scope,
                    upsertRequests
                );
                Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertResponse.Values.Values.Count, Is.EqualTo(upsertRequests.Count));
            }

            InlineValuationRequest CreateInlineValuationRequest(string recipeCode)
            {
                return new InlineValuationRequest(
                    recipeId: new ResourceId(scope, recipeCode),
                    metrics: valuationSpec,
                    sort: new List<OrderBySpec> {new OrderBySpec(ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                    valuationSchedule: valuationSchedule,
                    instruments: instruments);
            }

            #endregion
        }
    }
}
