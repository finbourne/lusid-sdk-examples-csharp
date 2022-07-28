using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.tutorials.Ibor;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class InterestRateSwaptionExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // The price of a swaption depends on its swap underlying which in turn
            // itself is determined by the price of the fixed leg and floating leg.
            // The price of a floating leg is determined by historic resets rates and projected rates.
            // In this method, we upsert reset rates.
            // For LUSID to pick up these quotes, we have added a RIC rule to the recipe (see BuildRecipeRequest in TestDataUtilities.cs)
            // The RIC rule has a large quote interval, this means that we can use one reset quote for all the resets.
            // For accurate pricing, one would want to upsert a quote per reset.

            var d =  new DateTimeOffset(2019, 12, 31, 0, 0, 0, TimeSpan.Zero);
            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "UniqueKeyForDictionary",
                TestDataUtilities.VanillaSwapFixingReference,
                QuoteSeriesId.InstrumentIdTypeEnum.RIC,
                0.05m,
                "InterestRate",
                d,
                QuoteSeriesId.QuoteTypeEnum.Price);
            var upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));

            // For models requiring discount curves, we upsert them below. ConstantTimeValueOfMoney does not require any discount curves.
            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("projection_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "LIBOR", TestDataUtilities.ExampleDiscountFactors2, "6M"));
            }
            if (model == ModelSelection.ModelEnum.BlackScholes || model == ModelSelection.ModelEnum.Bachelier)
            {
                var volatility = (model == ModelSelection.ModelEnum.BlackScholes) ? 0.2m : 10m;
                upsertComplexMarketDataRequest.Add("VolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, instrument, model, volatility));
            }

            if(upsertComplexMarketDataRequest.Any())
            {
                var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(
            LusidInstrument instrument,
            string scope,
            string portfolioCode,
            string recipeCode,
            string instrumentID)
        {
            var swaption = (InterestRateSwaption) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: swaption.StartDate.AddDays(-3),
                windowEnd: swaption.Swap.MaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            Assert.That(cashflows.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void InterestRateSwaptionCreationAndUpsertionExample()
        {
            // CREATE an interest rate swaption (that can then be upserted into LUSID)
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaptionWithNamedConventions();

            // ASSERT that it was created
            Assert.That(swaption, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = swaption.InstrumentType+Guid.NewGuid().ToString();
            var instrumentsIds = new List<(LusidInstrument, string)>{(swaption, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);

            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);

            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.InterestRateSwaption);
            var roundTripSwaption = retrieved as InterestRateSwaption;
            Assert.That(roundTripSwaption, Is.Not.Null);
            Assert.That(roundTripSwaption.DeliveryMethod, Is.EqualTo(swaption.DeliveryMethod));
            Assert.That(roundTripSwaption.StartDate, Is.EqualTo(swaption.StartDate));
            Assert.That(roundTripSwaption.PayOrReceiveFixed, Is.EqualTo(swaption.PayOrReceiveFixed));
            Assert.That(roundTripSwaption.Swap, Is.Not.Null);
            Assert.That(roundTripSwaption.Swap.InstrumentType, Is.EqualTo(LusidInstrument.InstrumentTypeEnum.InterestRateSwap));

            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }

        [TestCase(ModelSelection.ModelEnum.SimpleStatic)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        [TestCase(ModelSelection.ModelEnum.Bachelier)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes)]
        public void InterestRateSwaptionValuationExample(ModelSelection.ModelEnum model)
        {
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaption();
            CallLusidGetValuationEndpoint(swaption, model);
        }

        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        [TestCase(ModelSelection.ModelEnum.Discounting)]
        [TestCase(ModelSelection.ModelEnum.Bachelier)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes)]
        public void InterestRateSwaptionInlineValuationExample(ModelSelection.ModelEnum model)
        {
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaption();
            CallLusidInlineValuationEndpoint(swaption, model);
        }

        /// <summary>
        /// Lifecycle management of swaption
        /// For both cash and physically settled swaption, we expected conservation
        /// of PV (under CTVoM model) and in particular, equals to the payoff
        ///
        /// Cash-settled case: works the same as the others. This means, we get the cashflow
        /// and upsert that back into LUSID.
        ///
        /// Physically-settled case: Cashflow would be paying the strike to obtain the underlying.
        /// There is additional code to get the underlying in the GetValuation call as well as then
        /// upserting the underlying back into the portfolio.
        /// </summary>
        [LusidFeature("F22-35")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney,"Cash")]
        public void LifeCycleManagementForCashSettledInterestRateSwaption(
            ModelSelection.ModelEnum model,
            string deliveryMethod
        )
        {
            var DomCcy = "USD";
            var swaption = InstrumentExamples.CreateExampleInterestRateSwaption(
                deliveryMethod:deliveryMethod,
                currency:DomCcy
            );

            // CREATE wide enough window to pick up all cashflows
            var windowStart = swaption.StartDate.AddMonths(-1);
            var windowEnd = swaption.Swap.MaturityDate.AddMonths(1);

            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, swaption);

            // UPSERT to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, swaption);

            // CREATE recipe to price the portfolio
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);

            // GET all upsertable cashflows (transactions)
            var effectiveAt = swaption.Swap.StartDate;
            var allCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
                    scope,
                    portfolioCode,
                    effectiveAt,
                    windowStart,
                    windowEnd,
                    null,
                    null,
                    scope,
                    recipeCode)
                .Values;

            // We expect exactly one cashflow associated to a cash settled swaption and it occurs at expiry.
            Assert.That(allCashFlows.Count, Is.EqualTo(1));
            Assert.That(allCashFlows.Last().TotalConsideration.Currency, Is.EqualTo("USD"));
            var cashFlowDate = allCashFlows.First().TransactionDate;

            // CREATE valuation request for this portfolio consisting of the swaption,
            // with valuation dates a few days before, day of and a few days after the swaption expiration = cashflow date.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(1),
                effectiveFrom: cashFlowDate.AddDays(-1));

            // CALL GetValuation before upserting back the cashflows. We check
            // (1) there is no cash holdings in the portfolio prior to expiration
            // (2) that when the Swaption has expired, the PV is zero.
            var valuationBeforeAndAfterExpiration = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpiration,
                DomCcy);
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                // The CTVoM is not interesting for a Swaption. It is either in or out of the money (no change).
                // The underlying also doesn't get its rates at present. Hence values are zero.
                TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                    valuationBeforeAndAfterExpiration,
                    swaption.Swap.StartDate);
            }

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allCashFlows);

            _transactionPortfoliosApi.UpsertTransactions(
                scope,
                portfolioCode,
                PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));

            // HAVING upserted cashflow into LUSID, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest);

            // ASSERT that we have some cash in the portfolio
            var containsCashAfterUpsertion = valuationAfterUpsertingCashFlows
                .Data
                .Select(d => (string) d[TestDataUtilities.Luid])
                .Any(luid => luid != $"CCY_{DomCcy}");
            Assert.That(containsCashAfterUpsertion, Is.True);

            // ASSERT portfolio PV is constant for each valuation date.
            // We expect this to be true since we upserted the cashflows back in.
            // That is instrument pv + cashflow = option payoff = = constant for each valuation date.
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                // The CTVoM is not interesting for a Swaption. It is either in or out of the money (no change).
                // The underlying also doesn't get its rates at present. Hence values are zero.
                TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationAfterUpsertingCashFlows, relativeDifferenceTolerance: 1e-10);
            }

            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
