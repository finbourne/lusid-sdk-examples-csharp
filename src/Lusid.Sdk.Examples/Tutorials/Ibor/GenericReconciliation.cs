using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Client;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Lusid.Sdk.Tests.Tutorials.Ibor
{
    [TestFixture]
    public class GenericReconciliation : TutorialBase
    {
        private string _portfolioOneScope;
        private string _portfolioTwoScope;
        private string _portfolioCode;
        
        [SetUp]
        public void Setup()
        {
            _portfolioOneScope = "testPortfolio1" + Guid.NewGuid();
            _portfolioTwoScope = "testPortfolio2" + Guid.NewGuid();
            _portfolioCode = Guid.NewGuid().ToString();
        }

        #region Remapping Properties 

        /// <summary>
        /// Perform a reconciliation on two identical portfolios except for the Address Key for trader name being scope dependent.
        /// I.e., "Transaction/testPortfolio1/TraderName" = "John Doe" and "Transaction/testPortfolio2/TraderName" = "John Doe"
        /// Unless a mapping rule is provided between these two address keys in the reconciliation then both keys will appear
        /// as fail to match in the aggregation.
        /// </summary>
        [LusidFeature("F20-2")]
        [Test]
        public void Reconcile_By_Remapping_Properties()
        {
            var quotePrice = 105m;
            var units = "EUR";      
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var traderName = "John Doe";

            // Generate two identical portfolios.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestOne = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode, transactionDate,valuationDate,
                quotePrice, units, traderName);
            var valuationRequestTwo = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode, transactionDate,valuationDate,
                quotePrice, units, traderName);

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestOne, valuationRequestTwo);
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);

            // Get the reconciliation of the equity. 
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // The trader name properties on the two portfolios have different addresses and both addresses have been generated in the reconciliation 
            Assert.That(equityComparison.ResultComparison.ContainsKey($"Transaction/{_portfolioOneScope}/TraderName"));
            Assert.That(equityComparison.ResultComparison.ContainsKey($"Transaction/{_portfolioTwoScope}/TraderName"));

            // Assert that the reconciliation resulted in a Failed match for both properties.
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioOneScope}/TraderName"].ToString()
                .Contains("Failed"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString()
                .Contains("Failed"));

            // As we have a property which has a portfolio dependant Addresskey, we need to tell the reconciliation about this and how to map them together.
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // create the new reconciliation request with the mapping 
            reconciliation = new ReconciliationRequest(valuationRequestOne, valuationRequestTwo,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping});
            reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);

            equityComparison = reconciliationResponse.Comparisons.Single();

            // Check that all the comparisons return an exact match
            Assert.That(equityComparison.ResultComparison.All(x => x.Value.ToString().Equals("ExactMatch")));

            // Check that the property strings where successfully matched.
            Assert.That(equityComparison.Left.ContainsKey($"Transaction/{_portfolioOneScope}/TraderName"));
            Assert.That(equityComparison.Right.ContainsKey($"Transaction/{_portfolioTwoScope}/TraderName"));
            Assert.That(equityComparison.Difference.ContainsKey($"Transaction/{_portfolioTwoScope}/TraderName"));
            // Only the address key from the RIGHT hand result set is contained in the reconciliation
            Assert.That(!equityComparison.Difference.ContainsKey($"Transaction/{_portfolioOneScope}/TraderName"));
        }

        #endregion

        #region Numeric rules 

        /// <summary>
        /// Exact match is defined as being numerically equal to machine precision. In the case of decimals that is sufficient.
        /// In the case of result with attached units, e.g. currency and amount, the currency would also have to match in
        /// addition to exact numeric equality of the amount. 
        /// </summary>
        [LusidFeature("F20-3")]
        [Test]
        public void Reconcile_using_Numeric_Exact_Rule()
        {
            // The two portfolios disagree about the quote price.
            var quotePriceLeft = 105m;
            var quotePriceRight = 100m;
            var units = "EUR";
            var traderName = "John Doe";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction

            // Create two portfolios and their valuation requests with different quote prices.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate, quotePriceLeft, units, traderName);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate,quotePriceRight, units, traderName);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // Set the matching rules to use for each of the requested aggregates. 
            var rules = new List<ReconciliationRule>();

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation resulted in a failure to match for PV, and that the
            // difference is the absolute difference in the PV calculations (quote price * units).
            Assert.That(equityComparison.Difference["Valuation/PV"], Is.EqualTo(50));
            Assert.That(equityComparison.ResultComparison["Valuation/PV"].ToString(), Is.EqualTo("Failed"));
        }

        /// <summary>
        /// Numeric values can be compared within absolute tolerance. In this example two portfolios provide quotes of £105 and £100
        /// for an equity. The holding of 10 units results in "Valuation/PV" values of £1050 and £1000 respectively. These can be
        /// matched within a tolerance of £50.
        /// </summary>
        [LusidFeature("F20-4")]
        [Test]
        public void Reconcile_Using_Numeric_AbsoluteDifference_Rule()
        {
            // The two portfolios disagree about the quote price.
            var quotePriceLeft = 105m;
            var quotePriceRight = 100m;
            var units = "EUR";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var traderName = "John Doe";

            // Generate two portfolios and their valuation requests with different quote prices.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate,quotePriceLeft, units, traderName);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate,quotePriceRight, units, traderName);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // Instead we can set a rule which will allow a match within a provided tolerance. 

            var pvRule = new ReconcileNumericRule(ReconcileNumericRule.ComparisonTypeEnum.AbsoluteDifference, 50m,
                new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileNumericRule);

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, new List<ReconciliationRule>() {pvRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation resulted in a match within tolerance for PV, and that the difference
            // is the PV of the right hand portfolio minus the left hand portfolio 
            Assert.That(equityComparison.Difference["Valuation/PV"], Is.EqualTo(50));
            Assert.That(equityComparison.ResultComparison["Valuation/PV"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        /// <summary>
        /// Numeric values can be compared within relative tolerance. In this example two portfolios provide quotes of £105 and £100
        /// for an equity. The holding of 10 units results in "Valuation/PV" values of £1050 and £1000 respectively.
        /// The formula used to compute the relative difference is 1.0 - minimum(lhs,rhs)/maximum(lhs, rhs).
        /// For this example 1 - 1000/1050 is approx 0.047 (4.7%). This allows us to match within a 5% tolerance.
        /// </summary>
        [LusidFeature("F20-5")]
        [Test]
        public void Reconcile_using_Numeric_RelativeDifference_Rule()
        {
            // The two portfolios disagree about the quote price.
            var quotePriceLeft = 105m;
            var quotePriceRight = 100m;
            var units = "EUR";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var traderName = "John Doe";

            // Generate two portfolios and their valuation requests with different quote prices.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate,quotePriceLeft, units, traderName);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate,quotePriceRight, units, traderName);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // Instead we can set a rule which will allow a match within a provided relative tolerance. 
            var relativeRule = new ReconcileNumericRule(ReconcileNumericRule.ComparisonTypeEnum.RelativeDifference,
                0.05m,
                new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value));

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping},
                new List<ReconciliationRule>() {relativeRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation matches within tolerance for PV, and that the difference
            // is the PV of the right hand portfolio minus the left hand portfolio 
            Assert.That(equityComparison.Difference["Valuation/PV"], Is.LessThan(0.05));
            Assert.That(equityComparison.ResultComparison["Valuation/PV"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        /// <summary>
        /// In this example a Result0D is compared against a Decimal.
        /// The PV computed internally "Valuation/PV" using an upserted quote has a value and units.
        /// The "UnitResult/ClientCustomPV" upserted to the structured results store is a Decimal without units. 
        /// In the strictest sense these are incompatible types, however they can be successfully reconciled within tolerance 
        /// with a numeric difference rule.
        /// </summary>
        [LusidFeature("F20-6")]
        [Test]
        public void Reconcile_Numeric_Result_With_Units_Versus_Without()
        {
            var quotePrice = 101;
            var units = "EUR";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var traderName = "John Doe";

            // Generate two portfolios and their valuation requests, one has a unitless PV upserted to the structured result store.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate,quotePrice, units, traderName);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate,quotePrice, units, traderName, true);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");
            // Set the mapping between properties in the two portfolios. 
            var mappingNumeric = new ReconciliationLeftRightAddressKeyPair($"Valuation/PV",
                $"UnitResult/ClientCustomPV");

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping, mappingNumeric}, null,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Even though the values are identical we get a fail to match result by default.
            // This is due to the Result0D and decimal having different units (GBP vs no units).
            Assert.That(equityComparison.Left["Valuation/PV"],
                Is.EqualTo(equityComparison.Right["UnitResult/ClientCustomPV"]));
            Assert.That(equityComparison.Difference["UnitResult/ClientCustomPV"], Is.EqualTo(0.0));
            Assert.That(equityComparison.ResultComparison["UnitResult/ClientCustomPV"], Is.EqualTo("Failed"));

            // This can be handled by the introduction of a numeric tolerance comparison rule.
            var numericRule = new ReconcileNumericRule(ReconcileNumericRule.ComparisonTypeEnum.AbsoluteDifference, 0.1m,
                new AggregateSpec("UnitResult/ClientCustomPV", AggregateSpec.OpEnum.Value));

            // create the reconciliation request
            reconciliation = new ReconciliationRequest(
                valuationRequestLeft,
                valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping, mappingNumeric},
                new List<ReconciliationRule>() {numericRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            equityComparison = reconciliationResponse.Comparisons.Single();

            // With a numeric tolerance rule we no longer get a failed to match result and instead get a 
            // match within tolerance.
            Assert.That(equityComparison.Left["Valuation/PV"],
                Is.EqualTo(equityComparison.Right["UnitResult/ClientCustomPV"]));
            Assert.That(equityComparison.Difference["UnitResult/ClientCustomPV"], Is.EqualTo(0.0));
            Assert.That(equityComparison.ResultComparison["UnitResult/ClientCustomPV"],
                Is.EqualTo("MatchWithinTolerance"));
        }

        /// <summary>
        /// In addition to comparing Results with and without units the numeric difference rules allow
        /// for matching across result types with different units. For example two PVs which are £100 and $101 will
        /// return a MatchWithinTolerance when employing an absolute difference rule with a tolerance of 2.
        /// This arises due to the reconciliation engine internally casting to decimals. This may change in future to
        /// allow for tolerant matching to retain units. 
        /// </summary>
        [LusidFeature("F20-7")]
        [Test]
        public void Reconcile_Numeric_Values_With_Different_Units()
        {
            var quotePrice = 101;
            // Different units on the quote
            var unitsOne = "EUR";
            var unitsTwo = "USD";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var traderName = "John Doe";

            // Generate two portfolios and their valuation requests each has a quote in different units.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate,quotePrice, unitsOne, traderName);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate,quotePrice, unitsTwo, traderName);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // This can be handled by the introduction of a numeric tolerance comparison rule.
            var numericRule = new ReconcileNumericRule(ReconcileNumericRule.ComparisonTypeEnum.AbsoluteDifference, 2m,
                new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value));
            
            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, new List<ReconciliationRule>(){numericRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // The valuations were in GBP and USD respectively but can return a MatchWithinTolerance if a numeric difference rule is used
            // as this ignores units.
            Assert.That(equityComparison.Left["Valuation/PV"],
                Is.EqualTo(equityComparison.Right["Valuation/PV"]));
            Assert.That(equityComparison.Difference["Valuation/PV"], Is.EqualTo(0.0));
            Assert.That(equityComparison.ResultComparison["Valuation/PV"], Is.EqualTo("MatchWithinTolerance")); 
        }

        #endregion

        #region DateTime rules
        
        /// <summary>
        /// DateTimes can be tested either for exact matching or for matching within an absolute tolerance.
        /// In the case of absolute tolerance the tolerance is specified in number of days.
        /// For sub-day tolerances fractional values may be input. In this example two valuation datetimes an hour apart
        /// are successfully reconciled within a tolerance of 2 hours.
        /// </summary>
        [LusidFeature("F20-8")]
        [Test]
        public void Reconcile_Using_DateTime_AbsoluteDifference_Rule()
        {
            var quotePrice = 100m;
            var units = "EUR";
            var transactionDate=
                new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // datetime of transaction in portfolio one
            // Two valuations an hour apart
            var valuationDateLeft =
                new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); 
            var valuationDateRight =
                new DateTimeOffset(2022, 3, 1, 1, 0, 0, TimeSpan.Zero); 
            var traderName = "John Doe";

            
            // Generate two portfolios and their valuation requests each has a different valuation date.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDateLeft, quotePrice, units, traderName, true);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDateRight, quotePrice, units, traderName, true);

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // Set a absolute difference date time rule with a tolerance of 2 hours.
            var dateTimeRule = new ReconcileDateTimeRule(ReconcileDateTimeRule.ComparisonTypeEnum.AbsoluteDifference,
                2 / 24m,
                new AggregateSpec($"Analytic/default/ValuationDate", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileDateTimeRule);
            var rules = new List<ReconciliationRule>() {dateTimeRule};

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // The difference is the left datetime minus the right datetime
            Assert.That(equityComparison.Difference[$"Analytic/default/ValuationDate"],
                Is.EqualTo($"{valuationDateLeft - valuationDateRight}"));
            Assert.That(equityComparison.ResultComparison[$"Analytic/default/ValuationDate"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        #endregion

        #region String rules 
        
        /// <summary>
        /// Strings naturally have a distinct set of matching criteria to numeric types. This example demonstrates the possible matching patterns.
        /// The example here considers the case where the trader name is {first name} {last name} on the left-hand portfolio and {title} {first name} {last name}
        /// on the right-hand portfolio. The string contains rule allows for these to be considered a match if the right portfolio result is a sub string of the left.
        /// </summary>
        [Test]
        [LusidFeature("F20-9")]
        public void Reconcile_Using_String_Contains_Rule()
        {
            var quotePrice = 100m;
            var units = "EUR";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            // disagree about trader name
            var traderNameLeft = "Mr. John Doe";
            var traderNameRight = "John Doe";
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction

            // Generate two portfolios which contain a different trader name.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameLeft, true);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameRight, true);

            // Set the matching rules to use for each of the requested aggregates. Initially let this be the default values.
            var rules = new List<ReconciliationRule>();

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation resulted in a failed match between the trader name property
            // and that the difference is formatted correctly.
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioTwoScope}/TraderName"],
                Is.EqualTo($"-({traderNameLeft}, {traderNameRight})"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString(),
                Is.EqualTo("Failed"));

            // Reattempt the valuation expect this time apply a criteria rule for matching the strings.
            var stringComparisonRule = new ReconcileStringRule(ReconcileStringRule.ComparisonTypeEnum.Contains, null,
                new AggregateSpec($"Transaction/{_portfolioTwoScope}/TraderName", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileStringRule);

            // create the new reconciliation request with a "Contains" criteria
            reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping},
                new List<ReconciliationRule>() {stringComparisonRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation resulted in a match within tolerance for the trader name property
            // and that the difference is formatted correctly.
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioTwoScope}/TraderName"],
                Is.EqualTo($"{traderNameLeft} contains {traderNameRight}"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));

            // The contain rule only works for the case where the left-hand portfolio result contains the right-hand side result. 
            // If instead we create a new reconciliation request with a "Contains" criteria reversed. I.e, ask 
            // if "John Doe" contains "Mr John Doe".
            var swappedMapping = new ReconciliationLeftRightAddressKeyPair(
                $"Transaction/{_portfolioTwoScope}/TraderName",
                $"Transaction/{_portfolioOneScope}/TraderName");
            reconciliation = new ReconciliationRequest(valuationRequestRight, valuationRequestLeft,
                new List<ReconciliationLeftRightAddressKeyPair>() {swappedMapping},
                new List<ReconciliationRule>() {stringComparisonRule},
                new List<string>() {TestDataUtilities.InstrumentName});
            reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            equityComparison = reconciliationResponse.Comparisons.Single();

            // The reconciliation results in a failed match because John Doe does not contain Mr. John Doe
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioOneScope}/TraderName"],
                Is.EqualTo($"-({traderNameRight}, {traderNameLeft})"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioOneScope}/TraderName"].ToString(),
                Is.EqualTo("Failed"));
        }

        /// <summary>
        /// Another possible matching structure is when there is ambiguity in how one of the portfolios will label a property and several
        /// possible alternatives can arise. Continuing to use the example of a name, one portfolio might be consistent in using {first name}
        /// {last name} but a number of alternatives are possible. e.g. John Doe could be allowed to match any of Mr. John Doe, J. Doe, Mr. Doe.
        /// </summary>
        [Test]
        [LusidFeature("F20-10")]
        public void Reconcile_Using_String_IsOneOf_Rule()
        {
            var quotePrice = 100m;
            var units = "EUR";
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            // disagree about trader name
            var traderNameLeft = "John Doe";
            var traderNameRight = "J. Doe";

            // Generate two portfolios which contain a different trader name.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameLeft, true);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameRight, true);

            // Set the matching rules to use for each of the requested aggregates. 
            // Allow "John Doe" in the lhs to successfully match "Mr. John Doe", "J. Doe" or "Mr. Doe" in the rhs.
            var options = new Dictionary<string, List<string>>()
                {{"John Doe", new List<string>() {"Mr. John Doe", "J. Doe", "Mr. Doe"}}};
            var oneOfRule = new ReconcileStringRule(ReconcileStringRule.ComparisonTypeEnum.IsOneOf, options,
                new AggregateSpec($"Transaction/{_portfolioTwoScope}/TraderName", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileStringRule);
            var rules = new List<ReconciliationRule>() {oneOfRule};

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation succeeded for the trader name property, and that the difference contains the 
            // string in the rhs which matched the lhs. 
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioTwoScope}/TraderName"],
                Is.EqualTo($"{traderNameRight}"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        /// <summary>
        /// Like the contains case but where the case is also of no consequence. The example here demonstrated the
        /// successful matching of Mr. John Doe on the lhs and JOHN DOE on the rhs. 
        /// </summary>
        [Test]
        [LusidFeature("F20-11")]
        public void Reconcile_Using_String_ContainsAllCase_Rule()
        {
            var quotePrice = 100m;
            var units = "EUR";
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            // portfolios disagree but rhs is a substring of lhs if case is ignored.
            var traderNameLeft = "Mr. John Doe";
            var traderNameRight = "JOHN DOE";

            // Generate two portfolios which contain a different trader name.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate,quotePrice, units, traderNameLeft, true);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameRight, true);

            // Set the matching rules to use for each of the requested aggregates. Initially let this be the default values.
            var containsRule = new ReconcileStringRule(ReconcileStringRule.ComparisonTypeEnum.ContainsAnyCase, null,
                new AggregateSpec($"Transaction/{_portfolioTwoScope}/TraderName", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileStringRule);
            var rules = new List<ReconciliationRule>() {containsRule};

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation succeeds within tolerance for the trader name property
            // and that the difference is formatted correctly 
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioTwoScope}/TraderName"],
                Is.EqualTo($"{traderNameLeft} contains {traderNameRight}"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        /// <summary>
        /// The case insensitive rule allows strings to reconcile when they only disagree on case. The example here demonstrates this
        /// for "John Doe" and "john doe".
        /// </summary>
        [LusidFeature("F20-12")]
        [Test]
        public void Reconcile_Using_String_CaseInsensitive_Rule()
        {
            var quotePrice = 100m;
            var units = "EUR";
            var valuationDate = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            var transactionDate = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero); // date of transaction
            // The two portfolios disagree because of the case.
            var traderNameLeft = "John Doe";
            var traderNameRight = "john doe";

            // Generate two portfolios which contain a different trader name.
            // Calling a helper function which generates a valuation request on a
            // portfolio containing a single equity valued at the quote price with the provided units on the 
            // transaction date. 
            var valuationRequestLeft = ValuationRequestSetUp(_portfolioOneScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameLeft, true);
            var valuationRequestRight = ValuationRequestSetUp(_portfolioTwoScope, _portfolioCode,
                transactionDate, valuationDate, quotePrice, units, traderNameRight, true);

            // Set the matching rules to use for each of the requested aggregates. Initially let this be the default values.
            var containsRule = new ReconcileStringRule(ReconcileStringRule.ComparisonTypeEnum.CaseInsensitive, null,
                new AggregateSpec($"Transaction/{_portfolioTwoScope}/TraderName", AggregateSpec.OpEnum.Value),
                ReconciliationRule.RuleTypeEnum.ReconcileStringRule);
            var rules = new List<ReconciliationRule>() {containsRule};

            // Set the mapping between properties in the two portfolios. 
            var mapping = new ReconciliationLeftRightAddressKeyPair($"Transaction/{_portfolioOneScope}/TraderName",
                $"Transaction/{_portfolioTwoScope}/TraderName");

            // create the reconciliation request
            var reconciliation = new ReconciliationRequest(valuationRequestLeft, valuationRequestRight,
                new List<ReconciliationLeftRightAddressKeyPair>() {mapping}, rules,
                new List<string>() {TestDataUtilities.InstrumentName});
            var reconciliationResponse = _apiFactory.Api<ReconciliationsApi>().ReconcileGeneric(reconciliation);
            var equityComparison = reconciliationResponse.Comparisons.Single();

            // Assert that the reconciliation succeeds within tolerance for the trader name property
            // and that the difference is formatted correctly 
            Assert.That(equityComparison.Difference[$"Transaction/{_portfolioTwoScope}/TraderName"],
                Is.EqualTo($"{traderNameLeft}=={traderNameRight}"));
            Assert.That(equityComparison.ResultComparison[$"Transaction/{_portfolioTwoScope}/TraderName"].ToString(),
                Is.EqualTo("MatchWithinTolerance"));
        }

        #endregion
        
        #region TestSetup
        
        /// <summary>
        /// We are going to construct a simple portfolio with a single equity for demonstrating the capabilities of the reconciliation engine.
        /// This consists of a single equity whose value on a given valuation date is upserted.
        /// The quote price is a provided price for the value of the equity on the valuation date.
        /// Units are also provided for the currency of the quote.
        /// The trader name is a sub-holding key for the portfolio and has a portfolio specific address key.
        /// The upsertedUnitlessPv bool controls whether the valuation request will
        /// ask for the PV to be returned as "Valuation/PV" as Result0D (x, GBP) or as a Decimal x without units.
        /// </summary>
        /// <param name="scope"> The scope of the portfolio </param>
        /// <param name="code"> the code of the portfolio </param>
        /// <param name="transactionDate"> Date on which the portfolio was created.</param>
        /// <param name="valuationDate"> Date on which the valuation is performed </param>
        /// <param name="quotePrice"> The price quote for the equity</param>
        /// <param name="units"> Units used for purchase and quoting of the instrument </param>
        /// <param name="traderName"> The name of the trader who booked the transactions.</param>
        /// <param name="upsertedUnitlessPv"> If this is true the provided PV value is upserted to the SRS as a decimal</param>
        private ValuationRequest ValuationRequestSetUp(
            string scope,
            string code,
            DateTimeOffset transactionDate,
            DateTimeOffset valuationDate,
            decimal quotePrice,
            string units,
            string traderName,
            bool upsertedUnitlessPv = false)
        {
            Assert.That(transactionDate, Is.LessThan(valuationDate));

            // Create an equity on which to compare
            var instrumentId = CreateInstrumentReturnLuid();

            // Create a new property on the transactions for who booked them. 
            var propertyCode = "TraderName";
            CreateTraderProperty(scope, propertyCode, propertyCode);

            // Create a transaction portfolio
            var portfolioRequest = new CreateTransactionPortfolioRequest(
                code: _portfolioCode,
                displayName: $"Portfolio-{_portfolioCode}",
                baseCurrency: units,
                created: transactionDate,
                subHoldingKeys: new List<string>() {$"Transaction/{scope}/{propertyCode}"}
            );
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);
            Assert.That(portfolio?.Id.Code, Is.EqualTo(_portfolioCode));

            // Upsert transaction with trader name property 
            string traderNameKey = $"Transaction/{scope}/{propertyCode}";
            UpsertTransactionOnEquity(instrumentId, transactionDate, units, traderNameKey, traderName, scope, code);

            // Upsert a quote on the equity.
            var quoteScope = UpsertQuoteOnEquity(valuationDate, instrumentId, quotePrice, units);

            // Upsert unitless PV value to the SRS 
            var pricingContext = upsertedUnitlessPv ? UpsertSrsDecimal(instrumentId, valuationDate, quotePrice) : null;

            // Create the valuation recipe
            var (recipeScope, recipeCode) = CreateValuationRecipe(quoteScope, upsertedUnitlessPv, pricingContext);

            // Create and return the Valuation request
            return CreateValuationRequest(instrumentId, scope, code, propertyCode, recipeScope, recipeCode,
                upsertedUnitlessPv, valuationDate);
        }

        /// <summary>
        /// Create a simple equity instrument. Upsert it to Lusid and return the LusidInstrumentId.
        /// </summary>
        /// <returns></returns>
        private string CreateInstrumentReturnLuid()
        {
            var instruments = new List<(string Id, string Name)>
            {
                (Id: "SSE", Name: "Scottish Power PLC."),
            };            
            var upsertResponse = _apiFactory.Api<IInstrumentsApi>().UpsertInstruments(instruments.ToDictionary(
                k => k.Id,
                v => new InstrumentDefinition(
                    name: v.Name,
                    identifiers: new Dictionary<string, InstrumentIdValue> {["ClientInternal"] = new InstrumentIdValue(v.Id) }
                )
            ));
            Assert.That(upsertResponse.Failed.Count, Is.EqualTo(0));
            var ids = _apiFactory.Api<IInstrumentsApi>().GetInstruments("ClientInternal", instruments.Select(i => i.Id).ToList());
            return ids.Values.First().Value.LusidInstrumentId;
        }
        
        /// <summary>
        /// Create a property at "Transaction/{scope}/{propertyCode}"
        /// </summary>
        private void CreateTraderProperty(string scope, string propertyCode, string propertyName)
        {
            try
            {
                _apiFactory.Api<PropertyDefinitionsApi>()
                    .GetPropertyDefinition("Transaction", scope, propertyCode);
            }
            catch (ApiException apiEx)
            {
                if (apiEx.ErrorCode == 404)
                {
                    // Property definition doesn't exist (returns 404), so create one
                    // Details of the property to be created
                    var propertyDefinition = new CreatePropertyDefinitionRequest(
                        domain: CreatePropertyDefinitionRequest.DomainEnum.Transaction,
                        scope: scope,
                        lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual,
                        code: propertyCode,
                        valueRequired: false,
                        displayName: propertyName,
                        dataTypeId: new ResourceId("system", "string"));
                    _apiFactory.Api<PropertyDefinitionsApi>().CreatePropertyDefinition(propertyDefinition);
                }
                else
                {
                    throw apiEx;
                }
            } 
        }

        /// <summary>
        /// Upsert a transaction on the equity with the provided units and with the provided trader name.
        /// </summary>
        private void UpsertTransactionOnEquity(
            string instrumentId,
            DateTimeOffset transactionDate,
            string units,
            string traderNameKey,
            string traderName,
            string portfolioScope,
            string portfolioCode)
        {
            var transactionSpecs = new[]
            {
                (Id: instrumentId, Price: 100, Units: 10,
                    TradeDate: transactionDate),
            };

            var properties = new Dictionary<string, PerpetualProperty>
            {
                {traderNameKey, new PerpetualProperty(traderNameKey, new PropertyValue(traderName))}
            };
            var newTransactions = transactionSpecs.Select(id =>
                BuildTransactionRequest(id.Id, id.Units, id.Price, units, id.TradeDate, "Buy", properties));
            _apiFactory.Api<ITransactionPortfoliosApi>()
                .UpsertTransactions(portfolioScope, portfolioCode, newTransactions.ToList());
        }

        /// <summary>
        /// Create a request to book a transaction on an instrument.
        /// </summary>
        private static TransactionRequest BuildTransactionRequest(
            string instrumentId,
            decimal units,
            decimal price,
            string currency,
            DateTimeOrCutLabel tradeDate,
            string transactionType,
            Dictionary<string, PerpetualProperty> properties)
        {
            string LusidInstrumentIdentifier = "Instrument/default/LusidInstrumentId";
            return new TransactionRequest(
                transactionId: Guid.NewGuid().ToString(),
                type: transactionType,
                instrumentIdentifiers: new Dictionary<string, string>
                {
                    [LusidInstrumentIdentifier] = instrumentId
                },
                transactionDate: tradeDate,
                settlementDate: tradeDate,
                units: units,
                transactionPrice: new TransactionPrice(price, TransactionPrice.TypeEnum.Price),
                totalConsideration: new CurrencyAndAmount(price * units, currency),
                source: "Broker",
                properties: properties);
        }

        /// <summary>
        /// Upsert a quote on the equity with the given price and units.
        /// </summary>
        private string UpsertQuoteOnEquity(DateTimeOffset valuationDate, string instrumentId, decimal quotePrice,
            string units)
        {
            // create and upsert quote for the price of the instrument 
            var quoteScope = "Reconcile-Scope" + Guid.NewGuid();
            var quote = new UpsertQuoteRequest(
                new QuoteId(
                    new QuoteSeriesId(
                        provider: "Lusid",
                        priceSource: "",
                        instrumentId: instrumentId,
                        instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                        quoteType: QuoteSeriesId.QuoteTypeEnum.Price, field: "mid"
                    ),
                    effectiveAt: valuationDate
                ),
                metricValue: new MetricValue(
                    value: quotePrice,
                    unit: units
                )
            );

            // Upload the quote
            var result = _apiFactory.Api<IQuotesApi>().UpsertQuotes(quoteScope,
                new Dictionary<string, UpsertQuoteRequest>() {{"cor_id_one", quote}});
            Assert.That(result.Failed.Count, Is.EqualTo(0));
            return quoteScope;
        }

        /// <summary>
        ///  Upsert a decimal quote for the equity against the AddressKey "UnitResult/ClientCustomPV".
        /// </summary>
        private PricingContext UpsertSrsDecimal(string instrumentId, DateTimeOffset valuationDate, decimal quotePrice)
        {
            string dataScope = "scope-" + Guid.NewGuid();
            string resultType = "UnitResult/Analytic";
            string documentCode = "document-1";
            var upsertedPvValue = quotePrice * 10.0m;

            DataMapKey dataMapKey = new DataMapKey("1.0.0", "test-code");
            DataMapping dataMapping = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("UnitResult/LusidInstrumentId", "LusidInstrumentId", "string", "Unique"),
                new DataDefinition("UnitResult/ClientCustomPV", "ClientVal", "decimal", "Leaf"),
            });
            var request = new CreateDataMapRequest(dataMapKey, dataMapping);
            _structuredResultDataApi.CreateDataMap(dataScope,
                new Dictionary<string, CreateDataMapRequest> {{"dataMapKey", request}});
            string document = $"LusidInstrumentId, ClientVal\n" +
                              $"{instrumentId}, {upsertedPvValue}"; // Note the LusidInstrumentId the previously defined instrument.
            StructuredResultData structuredResultData =
                new StructuredResultData("csv", "1.0.0", documentCode, document, dataMapKey);
            StructuredResultDataId structResultDataId =
                new StructuredResultDataId("Client", documentCode, valuationDate, resultType);
            var upsertDataRequest = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData);
            _structuredResultDataApi.UpsertStructuredResultData(dataScope,
                new Dictionary<string, UpsertStructuredResultDataRequest> {{documentCode, upsertDataRequest}});
            string resourceKey = "UnitResult/*";
            var resultDataKeyRule = new ResultDataKeyRule(structResultDataId.Source, dataScope, structResultDataId.Code,
                resourceKey: resourceKey, documentResultType: resultType,
                resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.ResultDataKeyRule);
            var pricingContext = new PricingContext(resultDataRules: new List<ResultKeyRule>() {resultDataKeyRule});

            return pricingContext;
        }

        /// <summary>
        /// Create and upsert a valuation recipe.
        /// </summary>
        private (string, string) CreateValuationRecipe(string quoteScope, bool upsertedUnitlessPv,
            PricingContext pricingContext = null)
        {
            // CREATE and UPSERT recipe for valuation
            string recipeScope = "ReconRecipe_" + Guid.NewGuid();
            var codeExtension = upsertedUnitlessPv ? "_SRS" : "_Quote";
            var recipeCode = "Recipe" + codeExtension;
            var recipe = new ConfigurationRecipe
            (
                scope: recipeScope,
                code: recipeCode,
                market: new MarketContext
                {
                    Options = new MarketOptions(
                        defaultScope: quoteScope,
                        defaultSupplier: "Lusid",
                        defaultInstrumentCodeType: "LusidInstrumentId"
                    ),
                },
                pricing: pricingContext
            );

            //    Upload recipe to Lusid 
            var upsertRecipeRequest = new UpsertRecipeRequest(recipe);
            var response = _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            return (recipeScope, recipeCode);
        }

        /// <summary>
        /// Create and return a valuation request to retrieve the equity Pv (Result0D/decimal), valuation date (DateTimeOffset) and trader name (string). 
        /// </summary>
        private ValuationRequest CreateValuationRequest(
            string instrumentId,
            string portfolioScope,
            string portfolioCode,
            string propertyCode,
            string recipeScope,
            string recipeCode,
            bool upsertedUnitlessPv,
            DateTimeOffset valuationDate)
        {
            // Create the Valuation request
            var metrics = new List<AggregateSpec>
            {
                new AggregateSpec(TestDataUtilities.InstrumentName, AggregateSpec.OpEnum.Value),
                new AggregateSpec("Analytic/default/ValuationDate", AggregateSpec.OpEnum.Value),
                new AggregateSpec($"Transaction/{portfolioScope}/{propertyCode}", AggregateSpec.OpEnum.Value),
            };

            // Whether want to retrieve the quote or value from the SRS
            if (upsertedUnitlessPv)
            {
                metrics.Add(new AggregateSpec("UnitResult/ClientCustomPV", AggregateSpec.OpEnum.Value));
            }
            else
            {
                metrics.Add(new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value));
            }

            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(recipeScope, recipeCode),
                metrics: metrics,
                valuationSchedule: new ValuationSchedule(effectiveAt: valuationDate),
                groupBy: new List<string> {"Instrument/default/Name"},
                filters: new List<PropertyFilter>
                {
                    new PropertyFilter(TestDataUtilities.LusidInstrumentIdentifier, PropertyFilter.OperatorEnum.Equals,
                        instrumentId, PropertyFilter.RightOperandTypeEnum.Absolute)
                },
                portfolioEntityIds: new List<PortfolioEntityId> {new PortfolioEntityId(portfolioScope, portfolioCode)}
            );

            return valuationRequest;
        }
        
        #endregion 
        
        [TearDown]
        public void TearDown()
        {
            _portfoliosApi.DeletePortfolio(_portfolioOneScope, _portfolioCode);
            _portfoliosApi.DeletePortfolio(_portfolioTwoScope, _portfolioCode);
        }
    }
}