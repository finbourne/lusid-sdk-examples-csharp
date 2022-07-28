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
    public class FxForwardExamples: DemoInstrumentBase
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
            var upsertFxRateRequestReq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, TestDataUtilities.EffectiveAt, TestDataUtilities.EffectiveAt);
            var upsertQuoteResponse = _quotesApi.UpsertQuotes(scope, upsertFxRateRequestReq);
            ValidateQuoteUpsert(upsertQuoteResponse, upsertFxRateRequestReq.Count);

            if (model == ModelSelection.ModelEnum.Discounting)
            {
                Dictionary<string, UpsertComplexMarketDataRequest> upsertComplexMarketDataRequest =
                    new Dictionary<string, UpsertComplexMarketDataRequest>(); 
                upsertComplexMarketDataRequest.Add("discount_curve_USD", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1));
                upsertComplexMarketDataRequest.Add("discount_curve_JPY", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "JPY", "OIS", TestDataUtilities.ExampleDiscountFactors1));

                var upsertComplexMarketDataResponse = _complexMarketDataApi.UpsertComplexMarketData(scope, upsertComplexMarketDataRequest);
                ValidateComplexMarketDataUpsert(upsertComplexMarketDataResponse, upsertComplexMarketDataRequest.Count);
            }
        }

        /// <inheritdoc />
        protected override void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode, string instrumentID)
        {
            var fxForward = (FxForward) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: new DateTimeOrCutLabel(new DateTimeOffset(2000, 01, 01, 01, 0, 0, 0, TimeSpan.Zero)),
                windowEnd: new DateTimeOrCutLabel(new DateTimeOffset(2050, 01, 01, 01, 0, 0, 0, TimeSpan.Zero)),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;
            
            Assert.That(cashflows.Count, Is.EqualTo(fxForward.IsNdf ? 1 : 2)); // deliverable FxForward has 2 cashflows while non-delivered has 1.
        }

        [LusidFeature("F5-2")]
        [Test]
        public void FxForwardCreationAndUpsertionExample()
        {
            // CREATE an FxForward instrument (that can then be upserted into LUSID)
            var fxForward = (FxForward) InstrumentExamples.CreateExampleFxForward();

            // ASSERT that it was created
            Assert.That(fxForward, Is.Not.Null);
            
            // CAN NOW UPSERT TO LUSID
            var uniqueId = fxForward.InstrumentType+Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(fxForward, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            UpsertInstrumentsResponse upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            GetInstrumentsResponse getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse ,uniqueId);
            
            // CHECK contents
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.FxForward);
            var retrFxFwd = retrieved as FxForward;
            Assert.That(retrFxFwd, Is.Not.Null);
            Assert.That(retrFxFwd.DomAmount, Is.EqualTo(fxForward.DomAmount));
            Assert.That(retrFxFwd.FgnAmount, Is.EqualTo(fxForward.FgnAmount));
            Assert.That(retrFxFwd.DomCcy, Is.EqualTo(fxForward.DomCcy));
            Assert.That(retrFxFwd.FgnCcy, Is.EqualTo(fxForward.FgnCcy));
            
            // DELETE instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId); 
        }
        
        [LusidFeature("F22-15")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        public void FxForwardValuationExample(ModelSelection.ModelEnum model, bool isNdf)
        {
            var fxForward = InstrumentExamples.CreateExampleFxForward(isNdf);
            CallLusidGetValuationEndpoint(fxForward, model);
        }
        
        [LusidFeature("F22-16")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        public void FxForwardInlineValuationExample(ModelSelection.ModelEnum model, bool isNdf)
        {
            var fxForward = InstrumentExamples.CreateExampleFxForward(isNdf);
            CallLusidInlineValuationEndpoint(fxForward, model);
        }
        
        [LusidFeature("F22-17")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        public void FxForwardPortfolioCashFlowsExample(ModelSelection.ModelEnum model, bool isNdf)
        {
            var fxForward = InstrumentExamples.CreateExampleFxForward(isNdf);
            CallLusidGetPortfolioCashFlowsEndpoint(fxForward, model);
        }
        
        /// <summary>
        /// Lifecycle management of FX Forwards
        /// For deliverable FX Forwards, we expected conservation of PV (under CTVoM model) 
        /// i.e. (1) before maturity pv(FX forward) and (2) after maturity: pv(cash in domestic currency)
        /// to be the same numerically.
        /// </summary>
        [LusidFeature("F22-18")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney)]
        public void LifeCycleManagementForDeliverableFxForward(ModelSelection.ModelEnum model)
        {
            // CREATE FX Forward
            var fxForward = (FxForward) InstrumentExamples.CreateExampleFxForward(isNdf: false);
            
            // CREATE wide enough window to pick up all cashflows for the FX Forward
            var windowStart = fxForward.StartDate.AddMonths(-1);
            var windowEnd = fxForward.MaturityDate.AddMonths(1);
            
            // CREATE portfolio and add instrument to the portfolio
            var scope = Guid.NewGuid().ToString();
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, fxForward);
            
            // UPSERT FX Forward to portfolio and populating stores with required market data - use a constant FX rate USD/JPY = 150.
            var upsertFxRateRequestReq = TestDataUtilities.BuildFxRateRequest("USD", "JPY", 150, windowStart, windowEnd, true);
            
            var upsertQuoteResponse = _quotesApi.UpsertQuotes(scope, upsertFxRateRequestReq);
            ValidateQuoteUpsert(upsertQuoteResponse, upsertFxRateRequestReq.Count);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model, windowValuationOnInstrumentStartEnd: true);

            // GET all upsertable cashflows (transactions) for the FX Forward
            var allFxFwdCashFlows = _transactionPortfoliosApi.GetUpsertablePortfolioCashFlows(
                    scope,
                    portfolioCode,
                    TestDataUtilities.EffectiveAt,
                    windowStart,
                    windowEnd,
                    null,
                    null, 
                    scope,
                    recipeCode)
                .Values;

            // There are exactly two cashflows associated to FX forward (one in each currency) both at maturity.
            Assert.That(allFxFwdCashFlows.Count, Is.EqualTo(2));
            Assert.That(allFxFwdCashFlows.First().TotalConsideration.Currency, Is.EqualTo("USD"));
            Assert.That(allFxFwdCashFlows.Last().TotalConsideration.Currency, Is.EqualTo("JPY"));
            Assert.That(allFxFwdCashFlows.Select(c => c.TransactionDate).Distinct().Count(), Is.EqualTo(1));
            var cashFlowDate = allFxFwdCashFlows.First().TransactionDate;
            
            // CREATE valuation request for this FX Forward portfolio,
            // where the valuation schedule covers before, at and after the expiration of the FX Forward. 
            var valuationRequest = TestDataUtilities.CreateValuationRequest(
                scope,
                portfolioCode,
                recipeCode,
                effectiveAt: cashFlowDate.AddDays(5),
                effectiveFrom: cashFlowDate.AddDays(-5));
            
            // CALL GetValuation before upserting back the cashflows. We check that when the FX Forward has expired, the PV is zero.
            var valuationBeforeAndAfterExpirationOfFxForward = _aggregationApi.GetValuation(valuationRequest);
            TestDataUtilities.CheckNoCashPositionsInValuationResults(
                valuationBeforeAndAfterExpirationOfFxForward,
                fxForward.DomCcy);
            TestDataUtilities.CheckNonZeroPvBeforeMaturityAndZeroAfter(
                valuationBeforeAndAfterExpirationOfFxForward,
                fxForward.MaturityDate);

            // UPSERT the cashflows back into LUSID. We first populate the cashflow transactions with unique IDs.
            var upsertCashFlowTransactions = PortfolioCashFlows.PopulateCashFlowTransactionWithUniqueIds(allFxFwdCashFlows);
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, PortfolioCashFlows.MapToCashFlowTransactionRequest(upsertCashFlowTransactions));
            
            // HAVING upserted cashflow into lusid, we call GetValuation again.
            var valuationAfterUpsertingCashFlows = _aggregationApi.GetValuation(valuationRequest).Data;
            
            // ASSERT portfolio PV is constant across time by grouping the valuation result by date.
            // (constant because we upserted the cashflows back in with ConstantTimeValueOfMoney model and the FX rate is constant)
            // That is, we are checking instrument PV + cashflow PV = constant both before and after maturity  
            var resultsGroupedByDate = valuationAfterUpsertingCashFlows
                .GroupBy(d => (DateTime) d[TestDataUtilities.ValuationDateKey]);
            
            // CONVERT and AGGREGATE all results to USD
            var pvsInUsd = resultsGroupedByDate
                .Select(pvGroup => pvGroup.Sum(record =>
                {
                    var fxRate = ((string) record[TestDataUtilities.Currency]).Equals("JPY") ? 1.0/150 : 1;
                    return Convert.ToDouble(record[TestDataUtilities.ValuationPv]) * fxRate;
                }));

            // ASSERT portfolio PV is constant over time within a tolerance (so conversation of money)
            TestDataUtilities.ValuesWithinARelativeDiffTolerance(pvsInUsd);
            
            // CLEAN up.
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }
    }
}
