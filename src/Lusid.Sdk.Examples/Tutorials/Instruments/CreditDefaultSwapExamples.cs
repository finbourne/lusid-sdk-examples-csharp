using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class CreditDefaultSwapExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // POPULATE with required market data for valuation of the instruments
            CreditDefaultSwap cds = (CreditDefaultSwap) instrument;
          
            // CREATE a dictionary of complex market data to be upserted for the CDS. We always need a CDS spread curve.
            var cdsSpreadCurveUpsertRequest = TestDataUtilities.BuildCdsSpreadCurvesUpsertRequest(
                TestDataUtilities.EffectiveAt,
                cds.Ticker,
                cds.FlowConventions.Currency,
                cds.ProtectionDetailSpecification.Seniority,
                cds.ProtectionDetailSpecification.RestructuringType);

            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>()
            {
                {"CdsSpread", cdsSpreadCurveUpsertRequest}
            };

            // For models that is not ConstantTimeValueOfMoney, we require discount curves. We add them to the market data upsert.
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));
            }

            var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
            ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode, string instrumentID)
        {
            CreditDefaultSwap cds = (CreditDefaultSwap) instrument;
            var maturity = cds.MaturityDate;
            var start = cds.StartDate;
            ResourceListOfInstrumentCashFlow cashFlowsDuringCDS = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope,
                portfolioCode,
                maturity,
                start.AddYears(-5),
                maturity.AddYears(10),
                null,
                null,
                scope,
                recipeCode);
            
            var cashFlows = cashFlowsDuringCDS.Values.Select(cf => cf)
                .Select(cf => (cf.PaymentDate, cf.Amount, cf.Currency))
                .ToList();
            var allCashFlowsPositive = cashFlows.All(cf => cf.Amount > 0);
            Assert.That(allCashFlowsPositive, Is.True);

            // CHECK correct number of CDS premium leg cash flows: If CDS reaches maturity (that would be if no default event is triggered) there are 22 expected cash flows,
            var expectedNumberOfCouponCashFlows = 22;
            var couponCashFlows = cashFlowsDuringCDS.Values
                .Where(cf => cf.Diagnostics["CashFlowType"] == "Premium")
                .ToList();

            Assert.That(couponCashFlows.Count, Is.EqualTo(expectedNumberOfCouponCashFlows));
        }
        
        [LusidFeature("F5-4")]
        [Test]
        public void CreditDefaultSwapCreationAndUpsertionExample()
        {
            // CREATE CDS flow conventions for the credit default swap
            var cdsFlowConventions = new CdsFlowConventions(
                scope: null,
                code: null,
                currency: "GBP",
                paymentFrequency: "6M",
                rollConvention: "MF",
                dayCountConvention: "Act365",
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                rollFrequency: "6M",
                settleDays: 2,
                resetDays: 2
            );
            
            var cdsProtectionDetailSpecification = new CdsProtectionDetailSpecification(
                seniority: "SNR",
                restructuringType:"CR",
                protectStartDay: true,
                payAccruedInterestOnDefault: false);

            var cds = new CreditDefaultSwap(
                ticker: "ACME",
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 9, 18, 0, 0, 0, TimeSpan.Zero),
                flowConventions: cdsFlowConventions,
                couponRate: 0.5m,
                protectionDetailSpecification: cdsProtectionDetailSpecification,
                instrumentType: LusidInstrument.InstrumentTypeEnum.CreditDefaultSwap
            );
            // ASSERT that it was created
            Assert.That(cds, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = cds.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(cds, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            UpsertInstrumentsResponse upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.CreditDefaultSwap);
            var roundTripCds = retrieved as CreditDefaultSwap;
            Assert.That(roundTripCds, Is.Not.Null);
            Assert.That(roundTripCds.CouponRate, Is.EqualTo(cds.CouponRate));
            Assert.That(roundTripCds.Ticker, Is.EqualTo(cds.Ticker));
            Assert.That(roundTripCds.MaturityDate, Is.EqualTo(cds.MaturityDate));
            Assert.That(roundTripCds.StartDate, Is.EqualTo(cds.StartDate));
            Assert.That(roundTripCds.FlowConventions.Currency, Is.EqualTo(cds.FlowConventions.Currency));
            Assert.That(roundTripCds.FlowConventions.PaymentFrequency, Is.EqualTo(cds.FlowConventions.PaymentFrequency));
            Assert.That(roundTripCds.FlowConventions.ResetDays, Is.EqualTo(cds.FlowConventions.ResetDays));
            Assert.That(roundTripCds.FlowConventions.SettleDays, Is.EqualTo(cds.FlowConventions.SettleDays));
            Assert.That(roundTripCds.FlowConventions.PaymentCalendars.Count, Is.EqualTo(cds.FlowConventions.PaymentCalendars.Count));
            Assert.That(roundTripCds.FlowConventions.PaymentCalendars, Is.EquivalentTo(cds.FlowConventions.PaymentCalendars));
            
            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }
        
        [LusidFeature("F22-5")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void CreditDefaultSwapValuationExample(ModelSelection.ModelEnum model)
        {
            var cds = InstrumentExamples.CreateExampleCreditDefaultSwap();
            CallLusidGetValuationEndpoint(cds, model);
        }
        
        [LusidFeature("F22-6")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void CreditDefaultSwapInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var cds = InstrumentExamples.CreateExampleCreditDefaultSwap();
            CallLusidInlineValuationEndpoint(cds, model);
        }

        [LusidFeature("F22-31")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        public void CreditDefaultSwapGetPortfolioCashFlowsExample(ModelSelection.ModelEnum model)
        {
            var cds = InstrumentExamples.CreateExampleCreditDefaultSwap();
            CallLusidGetPortfolioCashFlowsEndpoint(cds, model);
        }

        [LusidFeature("F22-7")]
        [TestCase("2020-06-05T00:00:00.0000000+00:00")] // calculate upfront charge for cds contract
        public void CreditDefaultSwapIsdaCdsValuationExample(string sTestNow)
        {
            // Purpose: Demo the valuation of a credit default swap using the IsdaCds vendor library via Lusid
            var testScope = Guid.NewGuid().ToString();
            var testNow = DateTimeOffset.Parse(sTestNow);
            var cds = InstrumentExamples.CreateExampleCreditDefaultSwap() as CreditDefaultSwap;

            // CREATE the configuration Recipe
            var pricingOptions = new PricingOptions();
            // tell Lusid that CDS instruments should be valued with the default model in IsdaCds
            var modelRules = new List<VendorModelRule>
                {new VendorModelRule(VendorModelRule.SupplierEnum.IsdaCds, "VendorDefault", "CreditDefaultSwap")};
            var pricingContext = new PricingContext(options: pricingOptions, modelRules: modelRules);
            // IsdaCds will request market data corresponding to the key "Credit.IsdaCreditCurve.{Ticker}.{Currency}", in this case "Credit.IsdaCreditCurve.XYZCorp.USD".
            // Similarly, IsdaCds will request an Isda yield curve stored under the key "Rates.IsdaYieldCurve.{Currency}".
            // The following MarketDataKeyRules tell it to look for the data in Lusid in scope testScope. The * symbols are wildcards for the requested keys.
            var mktRules = new List<MarketDataKeyRule>
            {
                new MarketDataKeyRule("Credit.IsdaCreditCurve.*.*", "Lusid", testScope, MarketDataKeyRule.QuoteTypeEnum.Rate, "mid"),
                new MarketDataKeyRule("Rates.IsdaYieldCurve.*", "Lusid", testScope, MarketDataKeyRule.QuoteTypeEnum.Rate, "mid")
            };
            var mktContext = new MarketContext(mktRules);
            var recipeName = "IsdaCdsRecipe";
            var recipe = new ConfigurationRecipe(
                testScope,
                recipeName,
                mktContext,
                pricingContext,
                description: "ISDA CDS valuation demo"
            );

            // UPSERT the configuration recipe
            var upsertRecipeRequest = new UpsertRecipeRequest(recipe, null);
            var upsertRecipeResponse = _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);
            Assert.That(upsertRecipeResponse.Value, Is.Not.Null);

            // CREATE the required market data, in this case a credit curve and a yield curve
            // The IsdaYieldCurve for a currency can be sourced from the daily set published by ISDA
            // The credit spreads can be sourced from an appropriate market data provider or internal marks
            string ycXml = File.ReadAllText("../../../tutorials/Ibor/ExampleMarketData/IsdaYieldCurve_USD_20200605.xml");
            var ycOpaque = new OpaqueMarketData(ycXml, "xml", "Example isda yield curve", marketDataType: ComplexMarketData.MarketDataTypeEnum.OpaqueMarketData);
            var upsertYcId = new ComplexMarketDataId("Lusid", effectiveAt: testNow, marketAsset: "IsdaYieldCurve/USD");
            var ccData = new CreditSpreadCurveData(
                baseDate: testNow,
                domCcy: "USD",
                tenors: new List<string> {"6M", "1Y", "5Y", "10Y"},
                spreads: new List<decimal> {0.001m, 0.0011m, 0.0015m, 0.002m},
                recoveryRate: 0.4m,
                marketDataType: ComplexMarketData.MarketDataTypeEnum.CreditSpreadCurveData);
            var upsertCcId = new ComplexMarketDataId("Lusid", effectiveAt: testNow, marketAsset: "IsdaCreditCurve/XYZCorp/USD");

            // UPSERT market data
            var upsertYcRequest = new UpsertComplexMarketDataRequest(upsertYcId, ycOpaque);
            var upsertCcRequest = new UpsertComplexMarketDataRequest(upsertCcId, ccData);
            var upsertCmdResponse = _complexMarketDataApi.UpsertComplexMarketData(testScope,
                new Dictionary<string, UpsertComplexMarketDataRequest>
                { // the strings yc and cc are correlation ids that should be unique per call
                  // they serve no purpose other than for the client/user to identify the item(s) in the result.
                    {"yc", upsertYcRequest},
                    {"cc", upsertCcRequest}
                });
            Assert.That(upsertCmdResponse.Failed, Is.Empty);

            // CREATE a inline valuation request with an example CDS instrument
            var pvKey = "Holding/default/PV";
            var valRequest = new InlineValuationRequest(
                recipeId: new ResourceId(testScope, recipeName),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec("Analytic/default/InstrumentTag", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Analytic/default/ValuationDate", AggregateSpec.OpEnum.Value),
                    new AggregateSpec(pvKey, AggregateSpec.OpEnum.Value),
                },
                reportCurrency: "USD",
                valuationSchedule: new ValuationSchedule(effectiveAt: new DateTimeOrCutLabel(testNow)),
                instruments: new List<WeightedInstrument>
                {
                    new WeightedInstrument(1, $"{cds.Ticker}-{cds.FlowConventions.Currency}-TestCds", cds)
                });

            // GET the result
            var result = _aggregationApi.GetValuationOfWeightedInstruments(valRequest);

            // CHECK that the valuation was performed
            Assert.That(result.Data, Is.Not.Empty);
            Assert.That(result.AggregationFailures, Is.Empty);
        }
    }
}
