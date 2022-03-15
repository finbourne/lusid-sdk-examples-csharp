using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Examples.Ibor;
using Lusid.Sdk.Examples.Utilities;
using Lusid.Sdk.Model;
using LusidFeatures;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lusid.Sdk.Examples.Instruments
{
    [TestFixture]
    public class EquityOptionExamples: DemoInstrumentBase
    {
        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument option)
        {
            // UPSERT quote for pricing of the equity option. This quote is understood to be the reset price. 
            // In other words, this is the price of the underlying at the expiration of the equity option.
            var quoteRequest = TestDataUtilities.BuildQuoteRequest("ACME", QuoteSeriesId.InstrumentIdTypeEnum.RIC, 135m, "USD", TestDataUtilities.EffectiveAt);
            var upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));
            
            // TODO: For physically settled options, we manually upsert the equity underlying back into LUSID.
            // TODO: To price the underlying (on the days specified in the valuation schedule), we require
            // TODO: to upsert a quote for this date.
            var quoteRequestForUnderlying = TestDataUtilities.BuildQuoteRequest(
                "ACME",
                QuoteSeriesId.InstrumentIdTypeEnum.ClientInternal,
                135m,
                "USD",
                new DateTimeOffset(2020, 12, 16, 0, 0, 0, 0, TimeSpan.Zero));

            var upsertResponseForUnderlying = _quotesApi.UpsertQuotes(scope, quoteRequestForUnderlying);
            Assert.That(upsertResponseForUnderlying.Failed.Count, Is.EqualTo(0));
            Assert.That(upsertResponseForUnderlying.Values.Count, Is.EqualTo(quoteRequestForUnderlying.Count));

            var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>();
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                upsertComplexMarketDataRequest.Add("discountCurve", TestDataUtilities.BuildOisCurveRequest(TestDataUtilities.EffectiveAt, "USD"));
            }
            if (model == ModelSelection.ModelEnum.BlackScholes)
            {
                upsertComplexMarketDataRequest.Add("BlackScholesVolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, option, model, 0.2m));
            }
            if (model == ModelSelection.ModelEnum.Bachelier)
            { 
                upsertComplexMarketDataRequest.Add("BachelierVolSurface", TestDataUtilities.ConstantVolatilitySurfaceRequest(TestDataUtilities.EffectiveAt, option, model, 10m));
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
            var option = (EquityOption) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: option.StartDate.AddDays(-3),
                windowEnd: option.OptionMaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;
            
            Assert.That(cashflows.Count, Is.EqualTo(1));
        }

        [LusidFeature("F5-19")]
        [Test]
        public void EquityOptionCreationAndUpsertionExample()
        {
            // CREATE an Equity-Option (that can then be upserted into LUSID)
            var equityOption = (EquityOption) InstrumentExamples.CreateExampleEquityOption();
            
            // ASSERT that it was created
            Assert.That(equityOption, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = equityOption.InstrumentType + Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(equityOption, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId });
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.EquityOption);
            var roundTripEquityOption = retrieved as EquityOption;
            Assert.That(roundTripEquityOption, Is.Not.Null);
            Assert.That(roundTripEquityOption.Code, Is.EqualTo(equityOption.Code));
            Assert.That(roundTripEquityOption.Strike, Is.EqualTo(equityOption.Strike));
            Assert.That(roundTripEquityOption.DeliveryType, Is.EqualTo(equityOption.DeliveryType));
            Assert.That(roundTripEquityOption.DomCcy, Is.EqualTo(equityOption.DomCcy));
            Assert.That(roundTripEquityOption.OptionType, Is.EqualTo(equityOption.OptionType));
            Assert.That(roundTripEquityOption.StartDate, Is.EqualTo(equityOption.StartDate));
            Assert.That(roundTripEquityOption.OptionMaturityDate, Is.EqualTo(equityOption.OptionMaturityDate));
            Assert.That(roundTripEquityOption.OptionSettlementDate, Is.EqualTo(equityOption.OptionSettlementDate));
            Assert.That(roundTripEquityOption.UnderlyingIdentifier, Is.EqualTo(equityOption.UnderlyingIdentifier));
            
            // DELETE instrument
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId);
        }
        
        [LusidFeature("F22-8")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void EquityOptionValuationExample(ModelSelection.ModelEnum model, bool isCashSettled)
        {
            var equityOption = InstrumentExamples.CreateExampleEquityOption(isCashSettled);
            CallLusidGetValuationEndpoint(equityOption, model);
        }

        [LusidFeature("F22-9")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void EquityOptionInlineValuationExample(ModelSelection.ModelEnum model, bool isCashSettled)
        {
            var equityOption = InstrumentExamples.CreateExampleEquityOption(isCashSettled);
            CallLusidInlineValuationEndpoint(equityOption, model);
        }

        [LusidFeature("F22-30")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, false)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.Bachelier, true)]
        [TestCase(ModelSelection.ModelEnum.BlackScholes, true)]
        public void EquityOptionPortfolioCashFlowsExample(ModelSelection.ModelEnum model, bool isCashSettled)
        {
            var equityOption = InstrumentExamples.CreateExampleEquityOption(isCashSettled);
            CallLusidGetPortfolioCashFlowsEndpoint(equityOption, model);
        }
        
        /// <summary>
        /// Lifecycle management of equity option
        /// For both cash and physically settled equity option, we expected conservation
        /// of PV (under CTVoM model) and in particular, equals to the payoff
        /// i.e. pv(equity option) = max(spot - strike, 0) before maturity and
        ///      pv(underlying) + pv(cashflow) = max(spot - strike, 0) after maturity
        ///
        /// Cash-settled case: works the same as the others. This means, we get the cashflow
        /// and upsert that back into LUSID.
        ///
        /// Physically-settled case: Cashflow would be paying the strike to obtain the underlying.
        /// There is additional code to get the underlying in the GetValuation call as well as then
        /// upserting the underlying back into the portfolio. 
        /// </summary>
        [LusidFeature("F22-10")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        public void LifeCycleManagementForCashSettledEquityOption(ModelSelection.ModelEnum model)
        {
            // CREATE EquityOption
            var equityOption = (EquityOption) InstrumentExamples.CreateExampleEquityOption(isCashSettled: true);
            
            // CREATE wide enough window to pick up all cashflows associated to the EquityOption
            var windowStart = equityOption.StartDate.AddMonths(-1);
            var windowEnd = equityOption.OptionSettlementDate.AddMonths(1);
            
            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, equityOption);

            // UPSERT EquityOption to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, equityOption);
            
            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);
            
            // GET all upsertable cashflows (transactions) for the EquityOption
            var effectiveAt = equityOption.OptionSettlementDate;
            var allEquityOptionCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
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

            // We expect exactly one cashflow associated to a cash settled EquityOption and it occurs at expiry.
            Assert.That(allEquityOptionCashFlows.Count, Is.EqualTo(1));
            Assert.That(allEquityOptionCashFlows.Last().TotalConsideration.Currency, Is.EqualTo("USD"));
            var cashFlowDate = allEquityOptionCashFlows.First().TransactionDate;
            
            // CREATE valuation request for this portfolio consisting of the EquityOption,
            // with valuation dates a few days before, day of and a few days after the option expiration = cashflow date.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(5),
                effectiveFrom: cashFlowDate.AddDays(-5));
            
            // CALL GetValuation before upserting back the cashflows. We check
            // (1) there is no cash holdings in the portfolio prior to expiration
            // (2) that when the EquityOption has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationEquityOption = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationEquityOption,
                equityOption.DomCcy);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationEquityOption,
                equityOption.OptionMaturityDate);

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allEquityOptionCashFlows,
                equityOption.DomCcy);
            
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
                .Any(luid => luid != $"CCY_{equityOption.DomCcy}");
            Assert.That(containsCashAfterUpsertion, Is.True);

            // ASSERT portfolio PV is constant for each valuation date.
            // We expect this to be true since we upserted the cashflows back in.
            // That is instrument pv + cashflow = option payoff = = constant for each valuation date.
            TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationAfterUpsertingCashFlows, relativeDifferenceTolerance: 0.0);
    
            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
        
        /// <summary>
        /// Lifecycle management of equity option
        /// For both cash and physically settled equity option, we expected conservation
        /// of PV (under CTVoM model) and in particular, equals to the payoff
        /// i.e. pv(equity option) = max(spot - strike, 0) before maturity and
        ///      pv(underlying) + pv(cashflow) = max(spot - strike, 0) after maturity
        ///
        /// Cash-settled case: works the same as the others. This means, we get the cashflow
        /// and upsert that back into LUSID.
        ///
        /// Physically-settled case: Cashflow would be paying the strike to obtain the underlying.
        /// There is additional code to get the underlying in the GetValuation call as well as then
        /// upserting the underlying back into the portfolio. 
        /// </summary>
        [LusidFeature("F22-11")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        public void LifeCycleManagementForPhysicallySettledEquityOption(ModelSelection.ModelEnum model)
        {
            // CREATE EquityOption
            var equityOption = (EquityOption) InstrumentExamples.CreateExampleEquityOption(isCashSettled: false);
            
            // CREATE wide enough window to pick up all cashflows associated to the EquityOption
            var windowStart = equityOption.StartDate.AddMonths(-1);
            var windowEnd = equityOption.OptionSettlementDate.AddMonths(1);
            
            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, equityOption);

            // UPSERT EquityOption to portfolio and populating stores with required market data.
            CreateAndUpsertMarketDataToLusid(scope, model, equityOption);
            
            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);
            
            // GET all upsertable cashflows (transactions) for the EquityOption
            var effectiveAt = equityOption.OptionSettlementDate;
            var allEquityOptionCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
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

            // We expect exactly one cashflow associated to a physically settled EquityOption and it occurs at expiry.
            Assert.That(allEquityOptionCashFlows.Count, Is.EqualTo(1));
            Assert.That(allEquityOptionCashFlows.Last().TotalConsideration.Currency, Is.EqualTo(equityOption.DomCcy));
            var cashFlowDate = allEquityOptionCashFlows.First().TransactionDate;
            
            // CREATE valuation request for this portfolio consisting of the EquityOption,
            // with valuation dates a few days before, day of and a few days after the option expiration = cashflow date.
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(5),
                effectiveFrom: cashFlowDate.AddDays(-5),
                additionalRequestsKeys: new List<string>{"Analytic/default/OnExercise"});
            
            // CALL GetValuation before upserting back the cashflows. We check
            // (1) there is no cash holdings in the portfolio prior to expiration
            // (2) that when the EquityOption has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationEquityOption = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationEquityOption,
                equityOption.DomCcy);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationEquityOption,
                equityOption.OptionMaturityDate);

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(
                allEquityOptionCashFlows,
                equityOption.DomCcy);
            
            _transactionPortfoliosApi.UpsertTransactions(
                scope,
                portfolioCode,
                PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));
            
            // TODO: Cashflow endpoints returns cashflows, not instruments. As such, in terms of lifecycle management,
            // TODO: it is not immediate that have a hold of the underlying that we upsert/book into lusid. 
            // TODO: Consequently, in the valuation we request OnExercise to get the underlying.
            var jObject =(JObject) valuationBeforeAndAfterExpirationEquityOption.Data.First()["Analytic/default/OnExercise"];
            var underlying = jObject.ToObject<Equity>();
            Assert.That(underlying is null, Is.False);
            
            // NOTE that while we have exposed Equity, one is not able to book it. 
            // Hence the below code is to extract the equity option underlying and book the equity as one would do "usually". (see other examples)
            // NOTE also that the although pricing EquityOption require the underlying to be a RIC.
            // When upserting the underlying, we do permit/want the underlying type to be a RIC. Pricing the underlying 
            // would again be as "usual" (as above).
            var equityRequest = new Dictionary<string, InstrumentDefinition>
            {
                {equityOption.Code, new InstrumentDefinition(
                    equityOption.Code,
                    new Dictionary<string, InstrumentIdValue>
                        {{"ClientInternal", new InstrumentIdValue(equityOption.Code)}})}
            };
            var upsertResponse = _instrumentsApi.UpsertInstruments(equityRequest);
            
            // CREATE transaction to book the underlying onto the portfolio via its LusidInstrumentId
            // NOTE: we book it at the cashflow date i.e. we upsert the underlying into the portfolio at option expiry 
            var underlyingLuids = upsertResponse.Values
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();
            var transactionRequest = TestDataUtilities.BuildTransactionRequest(underlyingLuids, cashFlowDate);
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest);

            // HAVING upserted both cashflow and underlying into LUSID, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest);

            // ASSERT that we have some cash in the portfolio
            var containsCashAfterUpsertion = valuationAfterUpsertingCashFlows
                .Data
                .Select(d => (string) d[TestDataUtilities.Luid])
                .Any(luid => luid != $"CCY_{equityOption.DomCcy}");
            Assert.That(containsCashAfterUpsertion, Is.True);

            // ASSERT portfolio PV is constant for each valuation date.
            // We expect this to be true since we upserted the cashflows back in.
            // That is instrument pv + cashflow = option payoff = = constant for each valuation date.
            TestDataUtilities.CheckPvIsConstantAcrossDatesWithinTolerance(valuationAfterUpsertingCashFlows, relativeDifferenceTolerance: 0.0);
    
            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
