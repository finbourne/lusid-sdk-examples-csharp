using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Utilities;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Utilities
{
    public abstract class DemoInstrumentBase: TutorialBase
    {
        /// <summary>
        /// Creates and upsert market data to LUSID required to price the instrument.
        /// Each inheritor is for a different instrument type and hence requires different set of market data.
        /// </summary>
        protected abstract void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument);

        /// <summary>
        /// In order to price some instruments need resets. This is typically those with interest rate payments such as equity or interest rate swaps
        /// and floating rate bonds.
        /// Put the required information into LUSID for use in pricing.
        /// </summary>
        protected abstract void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument);

        /// <summary>
        /// Get portfolio cashflows specific to that instrument.
        /// </summary>
        protected abstract void GetAndValidatePortfolioCashFlows(LusidInstrument instrument, string scope, string portfolioCode, string recipeCode, string instrumentID);

        /// <summary>
        /// This method "wraps" around GetAndValidatePortfolioCashFlows method to provide scope and recipe etc.
        /// </summary>
        internal void CallLusidGetPortfolioCashFlowsEndpoint(LusidInstrument instrument, ModelSelection.ModelEnum model)
        {
            var scope = Guid.NewGuid().ToString();

            // CREATE portfolio and book instrument to the portfolio
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, instrument);

            // UPSERT sufficient market data to get cashflow for the instrument
            CreateAndUpsertMarketDataToLusid(scope, model, instrument);

            // UPSERT recipe - this is the configuration used in pricing
            var recipeCode = CreateAndUpsertRecipe(scope, model);

            GetAndValidatePortfolioCashFlows(instrument, scope, portfolioCode, recipeCode, instrumentID);

            // CLEAN up
            _instrumentsApi.DeleteInstrument("ClientInternal", instrumentID);
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }

        /// <summary>
        /// Create and upsert a recipe specifying the scope in which to look for the data and the
        /// model to be used to price the instrument.
        /// If windowValuationOnInstrumentStartEnd is true, this sets the price of instruments to be zero after maturity
        /// </summary>
        protected string CreateAndUpsertRecipe(
            string scope,
            ModelSelection.ModelEnum model,
            bool windowValuationOnInstrumentStartEnd = false)
        {
            var recipeCode = Guid.NewGuid().ToString();
            var recipeReq = TestDataUtilities.BuildRecipeRequest(recipeCode, scope, model, windowValuationOnInstrumentStartEnd);
            var response = _recipeApi.UpsertConfigurationRecipe(recipeReq);
            Assert.That(response.Value, Is.Not.Null);
            return recipeCode;
        }

        /// <summary>
        /// Utility method to create a new portfolio that contains one transaction against the instrument.
        /// </summary>
        /// <returns>Returns a tuple of instrumentId and portfolio code</returns>
        protected (string, string) CreatePortfolioAndInstrument(string scope, LusidInstrument instrument)
        {
            // CREATE portfolio
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest(TestDataUtilities.EffectiveAt);
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(portfolioRequest.Code));

            // Book the instrument against the given portfolio scope and code
            var instrumentID = BookInstrumentToPortfolio(instrument, scope, portfolioRequest.Code);

            return (instrumentID, portfolioRequest.Code);
        }

        /// <summary>
        /// Given an instrument, we book this into the portfolio provided.
        /// We return the instrumentId.
        /// </summary>
        private string BookInstrumentToPortfolio(
            LusidInstrument instrument,
            string portfolioScope,
            string portfolioCode)
        {
            // BUILD upsert instrument request
            var instrumentID = instrument.InstrumentType + Guid.NewGuid().ToString();
            var instrumentsIds = new List<(LusidInstrument, string)>{(instrument, instrumentID)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);

            // UPSERT the instrument and validate it was successful
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            var luids = upsertResponse.Values
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();

            // CREATE transaction to book the instrument onto the portfolio via their LusidInstrumentId
            var transactionRequest = TestDataUtilities.BuildTransactionRequest(luids, TestDataUtilities.EffectiveAt);
            _transactionPortfoliosApi.UpsertTransactions(portfolioScope, portfolioCode, transactionRequest);

            return instrumentID;
        }

        // UPSERT market data sufficient to price the instrument depending on the model.
        private void UpsertMarketDataForInstrument(
            LusidInstrument instrument,
            ModelSelection.ModelEnum model,
            string instrumentID,
            string scope)
        {
            if (model == ModelSelection.ModelEnum.SimpleStatic)
            {
                // SimpleStatic pricing is lookup pricing. As such, we upsert a quote.
                // Note that inside CreatePortfolioAndInstrument, the method TestDataUtilities.BuildInstrumentUpsertRequest books the instrument using "ClientInternal".
                // Hence upsert a quote using ClientInternal as the instrumentIdType.
                var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
                TestDataUtilities.BuildQuoteRequest(
                    quoteRequest,
                    "UniqueKeyForDictionary",
                    instrumentID,
                    QuoteSeriesId.InstrumentIdTypeEnum.ClientInternal,
                    100m,
                    "USD",
                    TestDataUtilities.EffectiveAt,
                    QuoteSeriesId.QuoteTypeEnum.Price);
                var upsertResponse = _quotesApi.UpsertQuotes(scope, quoteRequest);
                Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
                Assert.That(upsertResponse.Values.Count, Is.EqualTo(quoteRequest.Count));

                // Whilst the price comes from lookup, accrued interest requires resets if calculated.
                CreateAndUpsertInstrumentResetsToLusid(scope, model, instrument);
            }
            else // upsert complex market data
            {
                CreateAndUpsertMarketDataToLusid(scope, model, instrument);
            }
        }

        /// <summary>
        /// Perform a valuation of a portfolio consisting of the instrument.
        /// In the below code, we create a portfolio and book the instrument onto the portfolio via a transaction.
        /// </summary>
        internal void CallLusidGetValuationEndpoint(LusidInstrument instrument, ModelSelection.ModelEnum model)
        {
            var scope = Guid.NewGuid().ToString();

            // CREATE portfolio and add instrument to the portfolio
            var (instrumentID, portfolioCode) = CreatePortfolioAndInstrument(scope, instrument);

            // UPSERT market data sufficient to price the instrument depending on the model.
            UpsertMarketDataForInstrument(instrument, model, instrumentID, scope);

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model);

            // CREATE valuation request
            var valuationRequest = TestDataUtilities.CreateValuationRequest(scope, portfolioCode, recipeCode, TestDataUtilities.EffectiveAt);

            // CALL valuation and assert that the PVs makes sense.
            var result = _aggregationApi.GetValuation(valuationRequest);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Data.Count, Is.GreaterThanOrEqualTo(1));
            TestDataUtilities.CheckPvResultsMakeSense(result, instrument.InstrumentType);

            // CLEAN up
            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
            _portfoliosApi.DeletePortfolio(scope, portfolioCode);
        }

        /// <summary>
        /// Perform an inline valuation of a given instrument.
        /// Inline valuation means that we do not need to create a portfolio and book an instrument onto it.
        /// In particular, the instrument is also not persisted into any portfolio nor database, it gets deleted at the end.
        /// This endpoint makes it easy to experiment with pricing with less overhead.
        /// </summary>
        internal void CallLusidInlineValuationEndpoint(LusidInstrument instrument, ModelSelection.ModelEnum model)
        {
            var scope = Guid.NewGuid().ToString();

            // CREATE recipe to price the portfolio with
            var recipeCode = CreateAndUpsertRecipe(scope, model);

            // UPSERT market data sufficient to price the instrument
            CreateAndUpsertMarketDataToLusid(scope, model, instrument);

            // CREATE valuation request
            var valuationSchedule = new ValuationSchedule(effectiveAt: TestDataUtilities.EffectiveAt);
            var instruments = new List<WeightedInstrument> {new WeightedInstrument(1, "some-holding-identifier", instrument)};

            // CONSTRUCT valuation request
            var inlineValuationRequest = new InlineValuationRequest(
                recipeId: new ResourceId(scope, recipeCode),
                metrics: TestDataUtilities.ValuationSpec,
                sort: new List<OrderBySpec> {new OrderBySpec(TestDataUtilities.ValuationDateKey, OrderBySpec.SortOrderEnum.Ascending)},
                valuationSchedule: valuationSchedule,
                instruments: instruments);

            // CALL LUSID's inline GetValuationOfWeightedInstruments endpoint
            var result = _aggregationApi.GetValuationOfWeightedInstruments(inlineValuationRequest);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Data.Count, Is.GreaterThanOrEqualTo(1));
            TestDataUtilities.CheckPvResultsMakeSense(result, instrument.InstrumentType);

            _recipeApi.DeleteConfigurationRecipe(scope, recipeCode);
        }
    }
}
