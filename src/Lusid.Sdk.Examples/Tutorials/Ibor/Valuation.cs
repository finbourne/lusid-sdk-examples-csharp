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
    [TestFixture]
    public class Valuations: TutorialBase
    {
        private InstrumentLoader _instrumentLoader;
        private IList<string> _instrumentIds;


        [OneTimeSetUp]
        public void SetUp()
        {
            _instrumentLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = _instrumentLoader.LoadInstruments();
        }

        [LusidFeature("F36")]
        [Test]
        public void Run_Valuation()
        {
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);

            //    Create the transaction portfolio
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
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
            _apiFactory.Api<ITransactionPortfoliosApi>().UpsertTransactions(TestDataUtilities.TutorialScope, portfolioRequest.Code, newTransactions.ToList());

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
            string recipeScope = "some-recipe-scope";
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
            var response = _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            //    Upload the quote
            _apiFactory.Api<IQuotesApi>().UpsertQuotes(scope, quotes);

            //    Create the valuation request, this example calculates the percentage of total portfolio value and value by instrument
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(recipeScope, "DataScope_Recipe"),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec(TestDataUtilities.InstrumentName, AggregateSpec.OpEnum.Value),
                    new AggregateSpec(TestDataUtilities.ValuationPv, AggregateSpec.OpEnum.Proportion),
                    new AggregateSpec(TestDataUtilities.ValuationPv, AggregateSpec.OpEnum.Sum)
                },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveDate),
                groupBy: new List<string> { "Instrument/default/Name" },
                portfolioEntityIds: new List<PortfolioEntityId> { new PortfolioEntityId(TestDataUtilities.TutorialScope, portfolioRequest.Code) }
                );

            //    Do the aggregation
            var results = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);

            Assert.That(results.Data, Has.Count.EqualTo(4));
            Assert.That(results.Data[0]["Sum(Valuation/PV/Amount)"], Is.EqualTo(10000));
            Assert.That(results.Data[2]["Sum(Valuation/PV/Amount)"], Is.EqualTo(20000));
            Assert.That(results.Data[3]["Sum(Valuation/PV/Amount)"], Is.EqualTo(30000));
        }

        [Test]
        public void InlineMultiDateValuationOfABond()
        {
            // CREATE a bond instrument inline
            var instruments = new List<WeightedInstrument>
            {
                new WeightedInstrument(1, "bond", InstrumentExamples.CreateExampleBond())
            };

            // CREATE inline valuation request asking for instruments PV using a "default" recipe
            var scope = Guid.NewGuid().ToString();
            var valuationSchedule = new ValuationSchedule(effectiveFrom: TestDataUtilities.StartDate.AddDays(13), effectiveAt: TestDataUtilities.StartDate.AddDays(20));
            var inlineValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, "default"),
                metrics: TestDataUtilities.ValuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(TestDataUtilities.ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: valuationSchedule,
                instruments: instruments);

            // Values the bond for each day in between 2020-02-16 and 2020-02-23 (inclusive)
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuationOfWeightedInstruments(inlineValuationRequest);
            Assert.That(valuation, Is.Not.Null);
            // 6 valuation days (Given Sun-Sun (see effectiveFrom|To), rolls forward to Monday and generates schedule, rolling to appropriate GBD)
            Assert.That(valuation.Data.Count, Is.EqualTo(6));

            // GET the present values of the bond
            var presentValues = valuation.Data
                .Select(data => (double) data[TestDataUtilities.ValuationPv])
                .ToList();

            // CHECK pvs are positive (true for bonds)
            var allPositivePvs = presentValues.All(pv => pv >= 0);
            Assert.That(allPositivePvs, Is.EqualTo(true));

            // CHECK pvs are unique as they are valued everyday
            var uniquePvs = presentValues.Distinct().Count();
            // 6 valuation days (Given Sun-Sun (see effectiveFrom|To), rolls forward to Monday and generates schedule, rolling to appropriate GBD)
            Assert.That(uniquePvs, Is.EqualTo(6));
        }

        [Test]
        public void InlineSingleDateValuationOfInstrumentPortfolio()
        {
            // CREATE a portfolio of instruments inline
            var instruments = new List<WeightedInstrument>
            {
                new WeightedInstrument(1, nameof(FxForward), InstrumentExamples.CreateExampleFxForward()),
                new WeightedInstrument(2, nameof(FxOption), InstrumentExamples.CreateExampleFxOption()),
                new WeightedInstrument(3, nameof(Bond), InstrumentExamples.CreateExampleBond()),
            };

            // POPULATE with required market data for valuation of the instruments
            var scope = Guid.NewGuid().ToString();
            var upsertFxRateRequestreq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, TestDataUtilities.EffectiveAt, TestDataUtilities.EffectiveAt);
            _quotesApi.UpsertQuotes(scope, upsertFxRateRequestreq);

            // CREATE and upsert recipe for pricing the portfolio of instruments
            var constantTimeValueOfMoneyRecipeCode = "ConstantTimeValueOfMoneyRecipe";
            CreateAndUpsertRecipe(constantTimeValueOfMoneyRecipeCode, scope, ModelSelection.ModelEnum.ConstantTimeValueOfMoney);

            // CREATE inline valuation request asking for the inline instruments' PV
            var valuationSchedule = new ValuationSchedule(effectiveAt: TestDataUtilities.EffectiveAt);
            var inlineValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, constantTimeValueOfMoneyRecipeCode),
                metrics: TestDataUtilities.ValuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(TestDataUtilities.ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: valuationSchedule,
                instruments: instruments);

            // CALL valuation and check the PVs makes sense.
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuationOfWeightedInstruments(inlineValuationRequest);
            Assert.That(valuation, Is.Not.Null);

            foreach (var result in valuation.Data)
            {
                var pv = (double) result[TestDataUtilities.ValuationPv];
                Assert.That(pv, Is.Not.EqualTo(0).Within(1e-5));

                var instrumentTag = (string) result[TestDataUtilities.InstrumentTag];
                if (instrumentTag != nameof(FxForward))
                {
                    Assert.That(pv, Is.GreaterThanOrEqualTo(0));
                }
            }
        }

        [Test]
        public void TestDemonstratingFxForwardPricingWithDifferentPricingModels()
        {
            // CREATE and upset two recipe to price Fx-Forward - one by ConstantTimeValueOfMoney and one by Discounting
            var scope = Guid.NewGuid().ToString();

            var discountingRecipeCode = "DiscountingRecipe";
            CreateAndUpsertRecipe(discountingRecipeCode, scope, ModelSelection.ModelEnum.Discounting);

            var constantTimeValueOfMoneyRecipeCode = "ConstantTimeValueOfMoneyRecipe";
            CreateAndUpsertRecipe(constantTimeValueOfMoneyRecipeCode, scope, ModelSelection.ModelEnum.ConstantTimeValueOfMoney);

            // POPULATE stores with required market data to value Fx-Forward using discounting model
            // Fx rates are upserted for both models
            // Rate curves are upserted for the discounting pricing model
            var upsertFxRateRequestReq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, TestDataUtilities.EffectiveAt, TestDataUtilities.EffectiveAt);
            _quotesApi.UpsertQuotes(scope, upsertFxRateRequestReq);

            Dictionary<string, UpsertComplexMarketDataRequest> complexMarketUpsertRequests =
                new Dictionary<string, UpsertComplexMarketDataRequest>();
            complexMarketUpsertRequests.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
            complexMarketUpsertRequests.Add("discount_curve_JPY", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "OIS", TestDataUtilities.ExampleDiscountFactors1));
            complexMarketUpsertRequests.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));

            var upsertmarketResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, complexMarketUpsertRequests);
            ValidateComplexMarketDataUpsert(upsertmarketResponse, complexMarketUpsertRequests.Count);

            // CREATE a Fx-Forward as an inline instrument
            var instruments = new List<WeightedInstrument>
            {
                new WeightedInstrument(100, "fx-forward", InstrumentExamples.CreateExampleFxForward())
            };

            // CREATE valuation schedule
            var valuationSchedule = new ValuationSchedule(effectiveAt: TestDataUtilities.EffectiveAt);

            // CREATE inline valuation request for Simple Static and Discounting pricing model
            var discountingInlineValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, discountingRecipeCode),
                metrics: TestDataUtilities.ValuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(TestDataUtilities.ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: valuationSchedule,
                instruments: instruments);

            var constantTimeValueOfMoneyValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, constantTimeValueOfMoneyRecipeCode),
                metrics: TestDataUtilities.ValuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(TestDataUtilities.ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: valuationSchedule,
                instruments: instruments);

            // CALL valuation for Fx-Forward with each recipe
            var discountingValuation = _apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(discountingInlineValuationRequest);
            var constantTimeValueOfMoneyValuation = _apiFactory.Api<IAggregationApi>()
                .GetValuationOfWeightedInstruments(constantTimeValueOfMoneyValuationRequest);

            // ASSERT that the PV differs between the models and are not null
            Assert.That(discountingValuation, Is.Not.Null);
            Assert.That(constantTimeValueOfMoneyValuation, Is.Not.Null);
            var diff = (double) discountingValuation.Data.First()[TestDataUtilities.ValuationPv]
                       - (double) constantTimeValueOfMoneyValuation.Data.First()[TestDataUtilities.ValuationPv];
            Assert.That(diff, Is.Not.EqualTo(0).Within(1e-3));
        }

        [TestCase(nameof(Bond))]
        [TestCase(nameof(FxForward))]
        [TestCase(nameof(FxOption))]
        [TestCase(nameof(InterestRateSwap))]
        public void TestDemonstratingTheValuationOfInstruments(string instrumentName)
        {
            // CREATE a portfolio with instrument
            var scope = Guid.NewGuid().ToString();
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));
            LusidInstrument instrument = InstrumentExamples.GetExampleInstrument(instrumentName);

            // UPSERT the above instrument to portfolio as well as populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                scope,
                portfolioRequest.Code,
                TestDataUtilities.EffectiveAt,
                TestDataUtilities.EffectiveAt,
                new List<LusidInstrument> {instrument});

            // CREATE and upsert recipe specifying discount pricing model
            var discountingRecipeCode = "DiscountingRecipe";
            CreateAndUpsertRecipe(discountingRecipeCode, scope, ModelSelection.ModelEnum.Discounting);

            // CREATE valuation request
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioRequest.Code,
                discountingRecipeCode,
                TestDataUtilities.EffectiveAt);

            // CALL valuation
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);
            Assert.That(valuation, Is.Not.Null);
            Assert.That(valuation.Data.Count, Is.EqualTo(1));

            // CHECK PV - note that swaps/forwards can have negative PV
            var pv = (double) valuation.Data.First()[TestDataUtilities.ValuationPv];
            Assert.That(pv, Is.Not.Null);
            if (instrumentName != nameof(InterestRateSwap) && instrumentName != nameof(FxForward))
            {
                Assert.That(pv, Is.GreaterThanOrEqualTo(0));
            }
        }

        [TestCase("Bus252", true)]
        [TestCase("Act360", false)]
        [TestCase("Act365", false)]
        [TestCase("ActAct", true)]
        [TestCase("Thirty360", false)]
        [TestCase("ThirtyE360", false)]
        public void TestDemonstratingTheUseOfDifferentCalendarsAndDayCountConventions(string dayCountConvention, bool useCalendarFromCoppClark)
        {
            // GIVEN the payment calendars to use - real calendars e.g. those from Copp Clark can be used, or an
            // empty list can be provided to use the default calendar. The default calendar has no holidays but
            // Saturdays and Sundays are treated as weekends. More than one calendar code can be provided, to combine
            // their holidays. For example, when using two calendars, for a day to be a good business day it must be
            // a good business day in both.
            var paymentCalendars = useCalendarFromCoppClark ? new List<string>{"GBP"} : new List<string>();

            // CREATE the flow conventions with the desired DayCountConvention. The DayCountConvention determines
            // how the elapsed time between two datetime points is calculated.
            var flowConventions = new FlowConventions(
                scope: null,
                code: null,
                currency: "GBP",
                paymentFrequency: "6M",
                rollConvention: "MF",
                dayCountConvention: dayCountConvention,
                paymentCalendars: paymentCalendars,
                resetCalendars: new List<string>(),
                settleDays: 2,
                resetDays: 2
            );

            // CREATE a bond instrument inline
            const decimal principal = 1_000_000m;
            var instruments = new List<WeightedInstrument>
            {
                new WeightedInstrument(1, "bond", new Bond(
                    startDate: TestDataUtilities.EffectiveAt,
                    maturityDate: TestDataUtilities.EffectiveAt.AddYears(1),
                    domCcy: "GBP",
                    principal: principal,
                    couponRate: 0.05m,
                    flowConventions: flowConventions,
                    identifiers: new Dictionary<string, string>(),
                    instrumentType: LusidInstrument.InstrumentTypeEnum.Bond
                ))
            };

            // DEFINE the response we want
            const string valuationDateKey = "Analytic/default/ValuationDate";
            const string pvKey = "Holding/default/PV";
            var valuationSpec = new List<AggregateSpec>
            {
                new AggregateSpec(valuationDateKey, AggregateSpec.OpEnum.Value),
                new AggregateSpec(pvKey, AggregateSpec.OpEnum.Value),
            };

            // CREATE inline valuation request asking for instruments PV using a "default" recipe
            var scope = Guid.NewGuid().ToString();
            var inlineValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, "default"),
                metrics: valuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(valuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: new ValuationSchedule(effectiveAt: TestDataUtilities.EffectiveAt),
                instruments: instruments);

            // CALL valuation
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuationOfWeightedInstruments(inlineValuationRequest);
            var presentValue = valuation.Data[0][pvKey];

            // CHECK that the PV makes sense
            Assert.That(presentValue, Is.GreaterThanOrEqualTo(principal));
        }

        [Test]
        public void SingleDateValuationOfAnInstrumentPortfolio()
        {
            // CREATE a portfolio
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest();
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));
            // CREATE our instrument set
            var instruments = new List<LusidInstrument>
            {
                InstrumentExamples.CreateExampleFxForward(),
                InstrumentExamples.CreateExampleBond(),
                InstrumentExamples.CreateExampleFxOption(),
                InstrumentExamples.CreateExampleInterestRateSwap(InstrumentExamples.InterestRateSwapType.Vanilla)
            };

            // UPSERT the above instrument set to portfolio as well as populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                TestDataUtilities.TutorialScope,
                portfolioRequest.Code,
                TestDataUtilities.EffectiveAt,
                TestDataUtilities.EffectiveAt,
                instruments);

            // CREATE and upsert recipe specifying discount pricing model
            var discountingRecipeCode = "DiscountingRecipe";
            CreateAndUpsertRecipe(discountingRecipeCode, TestDataUtilities.TutorialScope, ModelSelection.ModelEnum.Discounting);

            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                TestDataUtilities.TutorialScope,
                portfolioRequest.Code,
                discountingRecipeCode,
                TestDataUtilities.EffectiveAt);

            // CALL valuation
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);
            Assert.That(valuation, Is.Not.Null);
            Assert.That(valuation.Data.Count, Is.EqualTo(instruments.Count));

            // CHECK PV results make sense
            foreach (var result in valuation.Data)
            {
                var inst = (string) result[TestDataUtilities.InstrumentName];
                var pv = (double) result[TestDataUtilities.ValuationPv];
                Assert.That(pv, Is.Not.Null);

                if (inst != nameof(FxForward) && inst != nameof(InterestRateSwap))
                {
                    Assert.That(pv, Is.GreaterThanOrEqualTo(0));
                }
            }
        }

        [Test]
        public void MultiDateValuationOfAnInstrumentPortfolio()
        {
            // CREATE a portfolio
            var effectiveDate = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest(effectiveDate);
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(TestDataUtilities.TutorialScope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));

            var instruments = new List<LusidInstrument>
            {
                InstrumentExamples.CreateExampleBond(),
                InstrumentExamples.CreateExampleFxOption(),
            };

            // Upsert instrument set to portfolio as well as populating stores with required market data
            AddInstrumentsTransactionPortfolioAndPopulateRequiredMarketData(
                TestDataUtilities.TutorialScope,
                portfolioRequest.Code,
                TestDataUtilities.StartDate.AddDays(13),
                TestDataUtilities.StartDate.AddDays(20),
                instruments,
                equityIdentifier: "ABC Corporation");

            // CREATE and upsert recipe for pricing the portfolio of instruments
            var constantTimeValueOfMoneyRecipeCode = "ConstantTimeValueOfMoneyRecipe";
            CreateAndUpsertRecipe(constantTimeValueOfMoneyRecipeCode, TestDataUtilities.TutorialScope, ModelSelection.ModelEnum.ConstantTimeValueOfMoney);

            // CREATE valuation schedule and request
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                TestDataUtilities.TutorialScope,
                portfolioRequest.Code,
                constantTimeValueOfMoneyRecipeCode,
                TestDataUtilities.StartDate.AddDays(20),
                TestDataUtilities.StartDate.AddDays(13));

            // CALL valuation
            var valuation = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);
            Assert.That(valuation, Is.Not.Null);
            // 6 valuation days (Given Sun-Sun (see effectiveFrom|To), rolls forward to Monday and generates schedule, rolling to appropriate GBD)
            // 3 instruments: bond, fx option, equity
            // So 6x3.
            Assert.That(valuation.Data.Count, Is.EqualTo(18));
            TestDataUtilities.CheckPvResultsMakeSense(valuation);
        }
    }
}
