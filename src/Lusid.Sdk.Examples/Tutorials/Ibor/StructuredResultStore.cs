using System;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Lusid.Sdk.Api;
using Lusid.Sdk.Client;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;


namespace Lusid.Sdk.Tests.tutorials.Ibor
{
    public class StructuredResultStore : TutorialBase
    {
        // In this example Structured Result Store is used to demonstrate how one can upsert document which contains accruals
        // but is missing PVs as such a valuation is done to calculate PVs based on the accruals provided.
        [LusidFeature("F10-10")]
        [Test]
        public void CalculatePvForBondOfAccruedOverriden()
        {
            // Setting up basic parameters
            string scope = "scope-" + Guid.NewGuid();
            // Using Analytic type, as we will be overriding accruals and doing valuation.
            string resultType = "UnitResult/Analytic";
            string documentCode = "document-1";
            // If the scope is fixed, the data map key should be of different version when loading new data map.
            DataMapKey dataMapKey = new DataMapKey("1.0.0", "test-code");
            DateTimeOffset effectiveAt = new DateTimeOffset(2022, 01, 19, 0, 0, 0, 0, TimeSpan.Zero);

            // Create and upsert the portfolio
            var portfolioRequest = TestDataUtilities.BuildTransactionPortfolioRequest(effectiveAt);
            var portfolio = _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);
            string portfolioCode = portfolio.Id.Code;

            // Create a bond with a given principal, coupon rate and flow convention.
            DateTimeOffset startDate = new DateTimeOffset(2019, 01, 15, 0, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset maturityDate = new DateTimeOffset(2023, 01, 15, 0, 0, 0, 0, TimeSpan.Zero);
            var bond = new Bond(
                startDate: startDate,
                maturityDate: maturityDate,
                domCcy: "GBP",
                principal: 100m,
                couponRate: 0.06m,
                flowConventions: new FlowConventions(
                    currency: "GBP",
                    paymentFrequency: "6M",
                    rollConvention: "MF",
                    dayCountConvention: "Act365",
                    paymentCalendars: new List<string>(),
                    resetCalendars: new List<string>(),
                    settleDays: 2,
                    resetDays: 2),
                identifiers: new Dictionary<string, string>(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Bond
            );

            // Upsert the created bond.
            var instrumentsIds = new List<(LusidInstrument, string)>
            {
                (bond, bond.InstrumentType + Guid.NewGuid().ToString())
            };
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);

            // Create transaction and upsert transcation onto the portfolio.
            List<string> luids = upsertResponse.Values
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();

            var transactionRequest = luids
                .Select(luid => TestDataUtilities.BuildTransactionRequest(luid, 1000, 0.0m, "USD", effectiveAt, "Buy"))
                .ToList();

            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest);

            // Create and upsert data mapping, indicating what data will be passed in via the document.
            // CompositeLeaf is an abstraction that allows the user to specify which keys should be connected.
            // In the case below it is used to link the amount of accrual and its currency, this is essential for valuation to be done as the amount and currency are necessary.
            // CompositeLeaf should be used when wanting to create a connected decimal-string pair (i.e. an amount and its currency).
            // It is important to note the name must be null. The CompositeLeaf does not actually appear in the output (the table).
            // Result0D is a decimal-string pair data type.
            DataMapping dataMapping = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("UnitResult/LusidInstrumentId", "LusidInstrumentId", "string", "Unique"),
                new DataDefinition("UnitResult/Valuation/InstrumentAccrued", dataType: "Result0D", keyType: "CompositeLeaf"),
                new DataDefinition("UnitResult/Valuation/InstrumentAccrued/Amount", "Accrual", "decimal", "Leaf"),
                new DataDefinition("UnitResult/Valuation/InstrumentAccrued/Ccy", "AccrualCcy", "string", "Leaf"),
                new DataDefinition("UnitResult/ClientCustomValue", "ClientVal", "decimal", "Leaf"),
            });
            var request = new CreateDataMapRequest(dataMapKey, dataMapping);
            _structuredResultDataApi.CreateDataMap(scope,
                new Dictionary<string, CreateDataMapRequest> {{"dataMapKey", request}});

            // Upsert Document containing client data. This data contains normalised accrual but not PV.
            // The document one upserts here is of CSV format. Json format can also be used.
            string document = $"LusidInstrumentId, Accrual, AccrualCcy, ClientVal\n" +
                              $"{luids.First()}, 0.0123456, GBP, 1.7320508"; // Note the LusidInstrumentId the previously defined instrument.
            StructuredResultData structuredResultData = new StructuredResultData("csv", "1.0.0", documentCode, document, dataMapKey);
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", documentCode, effectiveAt, resultType);
            var upsertDataRequest = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData);
            _structuredResultDataApi.UpsertStructuredResultData(scope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentCode, upsertDataRequest}});

            // Create result data key rule specifying which resource, model and bond to apply it to.
            string resourceKey = "UnitResult/*";
            var resultDataKeyRule = new ResultDataKeyRule(structResultDataId.Source, scope, structResultDataId.Code, resourceKey: resourceKey, documentResultType: resultType, resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.ResultDataKeyRule);
            var pricingContext = new PricingContext(modelRules: new List<VendorModelRule>
                {new VendorModelRule(VendorModelRule.SupplierEnum.Lusid, "ConstantTimeValueOfMoney","Bond")}, resultDataRules: new List<ResultKeyRule>{resultDataKeyRule} );

            // Create and upsert the recipe
            var configurationRecipe = new ConfigurationRecipe(scope, portfolioCode, new MarketContext(), pricingContext);
            var upsertRecipeRequest = new UpsertRecipeRequest(configurationRecipe, null);
            _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);



            // Create a valuation request requesting for:
            // - Lusid instrument id
            // - Accrual as is held in UnitResults (one from the document)
            // - Pv that was valued based on the overriden accrual
            // - The scaled accrual.
            // - Client Custom value that gets carried on.
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(scope, portfolioCode),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec(TestDataUtilities.Luid, AggregateSpec.OpEnum.Value),
                    // Obtain InstrumentAccrued from the store. Since we are overriding the InstrumentAccrued this is the same as calling Valuation/Accrued.
                    new AggregateSpec("UnitResult/Valuation/InstrumentAccrued", AggregateSpec.OpEnum.Value),
                    new AggregateSpec(TestDataUtilities.ValuationPv, AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/ClientCustomValue", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Valuation/InstrumentAccrued", AggregateSpec.OpEnum.Value),
                },
                portfolioEntityIds: new List<PortfolioEntityId> { new PortfolioEntityId(scope, portfolioCode) },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveAt));

            // Perform valuation and obtain results
            var results = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);

            // We expect the following output
            // | LusidInstrumentId  | Accrual - Not Scaled (from Valuation/InstrumentAccrued) | Accrual - Scaled (from UnitResult/Valuation/InstrumentAccrued) | PV Amount | Client Custom Value |
            // | ------------------ | ------------------------------------------------------- | -------------------------------------------------------------- | --------- | ------------------- |
            // |  <Generated-Luid>  | 0.0123456                                               | 12.3456                                                        | 107234.56 | 1.7320508           |
            Assert.That(results.Data[0]["Instrument/default/LusidInstrumentId"], Is.EqualTo(luids.First()));
            Assert.That(results.Data[0]["UnitResult/Valuation/InstrumentAccrued"], Is.EqualTo(12.3456));
            Assert.That(results.Data[0]["Valuation/InstrumentAccrued"], Is.EqualTo(0.0123456));
            Assert.That(results.Data[0]["Valuation/PV/Amount"], Is.EqualTo(107234.56));
            Assert.That(results.Data[0]["UnitResult/ClientCustomValue"], Is.EqualTo(1.7320508));
        }

        // In this example grouped level structured result store example is shown. Showing how document containing multiple portfolios can be upserted and queried.
        [LusidFeature("F10-11")]
        [Test]
        public void GetValuationGroupedUnitResultKeys()
        {
            // Setting up basic parameters
            string documentId = "document-1";
            string documentScope = "document-scope" + Guid.NewGuid();
            // Will be considering documents with multiple portfolios and querying the data on grouped level
            string resultType = "UnitResult/Grouped";
            DataMapKey dataMapKey = new DataMapKey("1.0.0", "test-code");
            DateTimeOffset effectiveAt = new DateTimeOffset(2022, 01, 19, 0, 0, 0, 0, TimeSpan.Zero);
            // Will be creating two portfolios
            string scope1 = "scope-" + Guid.NewGuid();
            string portfolioCode1 = "pf-1";
            string scope2 = "scope-" + Guid.NewGuid();
            string portfolioCode2 = "pf-2";


            // Create portfolios
            var portfolioRequest1 = new CreateTransactionPortfolioRequest(
                code: portfolioCode1,
                displayName: $"Portfolio-{portfolioCode1}",
                baseCurrency: "USD",
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope1, portfolioRequest1);
            var portfolioRequest2 = new CreateTransactionPortfolioRequest(
                code: portfolioCode2,
                displayName: $"Portfolio-{portfolioCode2}",
                baseCurrency: "USD",
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope2, portfolioRequest2);

            // Create and upsert two instruments
            string instrumentName1 = "AClientName1";
            string instrumentName2 = "AClientName2";
            string clientInstId1 = "clientInstId1";
            string clientInstId2 = "clientInstId2";
            var instruments = new Dictionary<string, InstrumentDefinition>
            {
                {
                    "an-inst1", new InstrumentDefinition(instrumentName1,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId1)
                        }}
                    )
                },
                {
                    "an-inst2", new InstrumentDefinition(instrumentName2,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId2)
                        }}
                    )
                },
            };
            var upsertResponse = _instrumentsApi.UpsertInstruments(instruments);

            // Retrive LusidInstrumentId's of the upserted instruments
            List<string> luids = upsertResponse.Values
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();

            // Create and upsert two transactions, one per portfolio.
            var transactionRequest1 = new List<TransactionRequest>
            {
                TestDataUtilities.BuildTransactionRequest(luids[0], 1000, 1m, "USD", effectiveAt, "Buy"),
            };
            _transactionPortfoliosApi.UpsertTransactions(scope1, portfolioCode1, transactionRequest1);

            var transactionRequest2 = new List<TransactionRequest>
            {
                TestDataUtilities.BuildTransactionRequest(luids[1], 1000, 10m, "USD", effectiveAt, "Buy")
            };
            _transactionPortfoliosApi.UpsertTransactions(scope2, portfolioCode2, transactionRequest2);

            // Create and upsert a datamap for the upcoming document.
            DataMapping dataMapping = new DataMapping(new List<DataDefinition>
                    {
                        new DataDefinition("UnitResult/PortfolioScope", "pfscope", "string", "PartOfUnique"),
                        new DataDefinition("UnitResult/PortfolioCode", "pfcode", "string", "PartOfUnique"),
                        new DataDefinition("UnitResult/Portfolio/Returns/YtD", "retYtD", "decimal", "Leaf"),
                        new DataDefinition("UnitResult/Portfolio/UserDefinedKey", "UserData", "string", "Leaf"),
                    });
            var request = new CreateDataMapRequest(dataMapKey, dataMapping);
            _structuredResultDataApi.CreateDataMap(documentScope,new Dictionary<string, CreateDataMapRequest> {{"documentKey", request}});

            // Create and upsert the document.
            // Note the document is compatible with the map upserted above, it only contains portfolios that were already created and upserted.
            var document = $"pfscope,pfcode,retYtD,UserData\n" +
                           $"{scope1},{portfolioCode1},0.123456,\"test1\"\n" +
                           $"{scope2},{portfolioCode2},1,\"test2\"";
            StructuredResultData structuredResultData = new StructuredResultData("csv", "1.0.0", documentId, document, dataMapKey);
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", documentId, effectiveAt, resultType);
            var upsertDataRequest = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData);
            _structuredResultDataApi.UpsertStructuredResultData(documentScope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest}});

            // Create result data key rule specifying what resource we want it to affect.
            string resourceKey = "UnitResult/Portfolio/*";
            var resultDataKeyRule = new ResultDataKeyRule(
                "Client",
                documentScope,
                documentId,
                resourceKey: resourceKey,
                documentResultType: resultType,
                resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.ResultDataKeyRule);

            // Create and upsert a recipe with the result data key rule
            var pricingOptions = new PricingOptions {AllowAnyInstrumentsWithSecUidToPriceOffLookup = false, AllowPartiallySuccessfulEvaluation = true};
            var pricingContext = new PricingContext(null, null, pricingOptions, new List<ResultKeyRule>{resultDataKeyRule} );
            var configurationRecipe = new ConfigurationRecipe(documentScope, "recipe", new MarketContext(), pricingContext);
            var upsertRecipeRequest = new UpsertRecipeRequest(configurationRecipe, null);
            _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            // Creating a valuation request, in which we request portfolio id, YtD, and some user key.
            // Sorting by UserDefinedKey for reproducible order of the output.
            // This Valuation request is applied over both portfolios.
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(documentScope, "recipe"),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec("Portfolio/default/Id", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/Portfolio/Returns/YtD", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/Portfolio/UserDefinedKey", AggregateSpec.OpEnum.Value),
                },
                portfolioEntityIds: new List<PortfolioEntityId>
                {
                    new PortfolioEntityId(scope1, portfolioCode1),
                    new PortfolioEntityId(scope2, portfolioCode2)
                },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveAt),
                sort: new List<OrderBySpec>
            {
                new OrderBySpec("UnitResult/Portfolio/UserDefinedKey", OrderBySpec.SortOrderEnum.Ascending)
            });

            // Perform valuation and obtain results
            var results = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);

            // We expect the following results
            // | PortfolioId | YtD      | UserDefinedKey |
            // | ----------- | -------- | -------------- |
            // | pf-1        | 0.123456 | test1          |
            // | pf-1        | 0.123456 | test1          |
            // | pf-2        | 1        | test2          |
            // | pf-2        | 1        | test2          |
            // We observer double results because transactions are of type "Buy"
            Assert.That(results.Data[0]["Portfolio/default/Id"], Is.EqualTo(portfolioCode1));
            Assert.That(results.Data[0]["UnitResult/Portfolio/Returns/YtD"], Is.EqualTo(0.123456));
            Assert.That(results.Data[0]["UnitResult/Portfolio/UserDefinedKey"], Is.EqualTo("test1"));
            Assert.That(results.Data[0], Is.EqualTo(results.Data[1]));

            Assert.That(results.Data[2]["Portfolio/default/Id"], Is.EqualTo(portfolioCode2));
            Assert.That(results.Data[2]["UnitResult/Portfolio/Returns/YtD"], Is.EqualTo(1));
            Assert.That(results.Data[2]["UnitResult/Portfolio/UserDefinedKey"], Is.EqualTo("test2"));
            Assert.That(results.Data[2], Is.EqualTo(results.Data[3]));
        }

        // Showing how one can use Structured Result Store on holding level with custom properties, allowing for extra information being available on per holding basis.
        // In this example two properties are created, Strategy and Country. In principle it's up to the client which properties they would like.
        [LusidFeature("F10-12")]
        [Test]
        public void GetValuationHoldingUnitResultKeys()
        {
            // Setting up basic parameters
            string documentId = "document-1";
            string documentScope = "document-scope-" + Guid.NewGuid();
            string resultType = "UnitResult/Holding";
            DataMapKey dataMapKey = new DataMapKey("1.0.0", "test-code");
            DateTimeOffset effectiveAt = new DateTimeOffset(2022, 01, 19, 0, 0, 0, 0, TimeSpan.Zero);
            var dataTypeId = new ResourceId("system", "string");
            string scope1 = "scope1-" + Guid.NewGuid();
            string scope2 = "scope2-" + Guid.NewGuid();

            // Creating and upserting the property definition.
            // Demonstrating how custom properties can be made.
            var propertyDefinition1 = new CreatePropertyDefinitionRequest(
                domain: CreatePropertyDefinitionRequest.DomainEnum.Transaction,
                scope: scope1,
                code: "Strategy",
                valueRequired: true,
                displayName: "Strategy",
                dataTypeId: dataTypeId,
                lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual);
            _propertyDefinitionsApi.CreatePropertyDefinition(propertyDefinition1);

            var propertyDefinition2 = new CreatePropertyDefinitionRequest(
                domain: CreatePropertyDefinitionRequest.DomainEnum.Transaction,
                scope: scope2,
                code: "Country",
                valueRequired: true,
                displayName: "Country",
                dataTypeId: dataTypeId,
                lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual);
            _propertyDefinitionsApi.CreatePropertyDefinition(propertyDefinition2);

            // Create and upsert the portfolios.
            // The portfolios have two sub-holding keys which represent the properties defined above.
            string portfolioCode1 = "pf1-" + Guid.NewGuid();
            var portfolioRequest1 = new CreateTransactionPortfolioRequest(
                code: portfolioCode1,
                displayName: $"Portfolio-{portfolioCode1}",
                baseCurrency: "USD",
                subHoldingKeys: new List<string>
                {
                    $"Transaction/{scope1}/Strategy",
                    $"Transaction/{scope2}/Country"
                },
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope1, portfolioRequest1);

            string portfolioCode2 = "pf2-" + Guid.NewGuid().ToString();
            var portfolioRequest2 = new CreateTransactionPortfolioRequest(
                code: portfolioCode2,
                displayName: $"Portfolio-{portfolioCode2}",
                baseCurrency: "USD",
                subHoldingKeys: new List<string>
                {
                    $"Transaction/{scope1}/Strategy",
                    $"Transaction/{scope2}/Country"
                },
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope1, portfolioRequest2);

            // Create and upsert a simple instrument instrument
            var instrumentName1 = "AClientName1";
            var clientInstId1 = "clientInstId1";
            var instruments = new Dictionary<string, InstrumentDefinition>
            {
                {
                    "an-inst1", new InstrumentDefinition(instrumentName1,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId1)
                        }}
                    )
                }
            };
            var upsertResponse = _instrumentsApi.UpsertInstruments(instruments);

            // Obtain LusidInstrumentId
            string luid = upsertResponse.Values.First().Value.LusidInstrumentId;

            // CREATE and UPSERT transactions on the instrument
            // 2 tranasction in pf1 (Strategy1 and Strategy2) => 4 holdings
            // 1 transaction in pf2 (Strategy1) => 2 holdings
            // These transactions are created to include the desired properties, the identifier (LusidInstrumentId) and general transaction details.
            var transactionRequest1 = new List<TransactionRequest>
            {
                new TransactionRequest(
                    transactionId: Guid.NewGuid().ToString(),
                    type: "Buy",
                    instrumentIdentifiers: new Dictionary<string, string>
                    {
                        ["Instrument/default/LusidInstrumentId"] = luid
                    },
                    transactionDate: effectiveAt,
                    settlementDate: effectiveAt,
                    units: 1000,
                    transactionPrice: new TransactionPrice(1, TransactionPrice.TypeEnum.Price),
                    totalConsideration: new CurrencyAndAmount(1*1000, "USD"),
                    properties: new Dictionary<string, PerpetualProperty>
                    {
                        {$"Transaction/{scope1}/Strategy", new PerpetualProperty($"Transaction/{scope1}/Strategy", new PropertyValue("Strategy1"))},
                        {$"Transaction/{scope2}/Country", new PerpetualProperty($"Transaction/{scope2}/Country", new PropertyValue("England"))}
                    },
                    source: "Broker"),
                new TransactionRequest(
                    transactionId: Guid.NewGuid().ToString(),
                    type: "Buy",
                    instrumentIdentifiers: new Dictionary<string, string>
                    {
                        ["Instrument/default/LusidInstrumentId"] = luid
                    },
                    transactionDate: effectiveAt,
                    settlementDate: effectiveAt,
                    units: 1000,
                    transactionPrice: new TransactionPrice(1, TransactionPrice.TypeEnum.Price),
                    totalConsideration: new CurrencyAndAmount(10000, "USD"),
                    properties: new Dictionary<string, PerpetualProperty>
                    {
                        {$"Transaction/{scope1}/Strategy", new PerpetualProperty($"Transaction/{scope1}/Strategy", new PropertyValue("Strategy2"))},
                        {$"Transaction/{scope2}/Country", new PerpetualProperty($"Transaction/{scope2}/Country", null)}
                    },
                    source: "Broker")
            };
            _transactionPortfoliosApi.UpsertTransactions(scope1, portfolioCode1, transactionRequest1);


            var transactionRequest2 = new List<TransactionRequest>
            {
                new TransactionRequest(
                    transactionId: Guid.NewGuid().ToString(),
                    type: "Buy",
                    instrumentIdentifiers: new Dictionary<string, string>
                    {
                        ["Instrument/default/LusidInstrumentId"] = luid
                    },
                    transactionDate: effectiveAt,
                    settlementDate: effectiveAt,
                    units: 1000,
                    transactionPrice: new TransactionPrice(1, TransactionPrice.TypeEnum.Price),
                    totalConsideration: new CurrencyAndAmount(10*1000, "USD"),
                    properties: new Dictionary<string, PerpetualProperty>
                    {
                        {$"Transaction/{scope1}/Strategy", new PerpetualProperty($"Transaction/{scope1}/Strategy", new PropertyValue("Strategy1"))},
                    },
                    source: "Broker")
            };
            _transactionPortfoliosApi.UpsertTransactions(scope1, portfolioCode2, transactionRequest2);

            // Create and upsert a data mapping
            // Note the data mapping contains the two custom made fields, Strategy and Country.
            DataMapping dataMapping = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("UnitResult/Instrument/default/LusidInstrumentId", "luid", "string",
                    "PartOfUnique"),
                new DataDefinition("UnitResult/Holding/default/Currency", "holdingccy", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/Portfolio/Id", "pfid", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/Portfolio/Scope", "pfscope", "string", "PartOfUnique"),
                new DataDefinition($"UnitResult/Transaction/{scope1}/Strategy", "strat", "string", "PartOfUnique"),
                new DataDefinition($"UnitResult/Transaction/{scope2}/Country", "country", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/Returns/YtD", "retYtD", "decimal", "Leaf"),
                new DataDefinition("UnitResult/UserDefinedKey", "UserData", "string", "Leaf"),
            });
            var request = new CreateDataMapRequest(dataMapKey, dataMapping);
            _structuredResultDataApi.CreateDataMap(documentScope,
                new Dictionary<string, CreateDataMapRequest> {{"dataMapKey", request}});

            // Create document which has properties filled in.
            // We allowed for Strategy1 and Strategy2 for the Strategy field. For Country field we allowed for England and empty.
            var document = $"luid,holdingccy,pfscope,pfid,strat,country,retYtD,UserData\n" +
                           $"{luid},USD,{scope1},{portfolioCode1},Strategy1,England,0.123456,\"test1\"\n" +
                           $"{luid},USD,{scope1},{portfolioCode1},Strategy2,,1,\"test2\"\n" +
                           $"CCY_USD,USD,{scope1},{portfolioCode1},Strategy1,England,10,\"test_ccy1\"\n" +
                           $"CCY_USD,USD,{scope1},{portfolioCode1},Strategy2,,100,\"test_ccy2\"\n";
            StructuredResultData structuredResultData = new StructuredResultData("csv", "1.0.0", documentId, document, dataMapKey);
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", documentId, effectiveAt, resultType);
            var upsertDataRequest = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData);
            _structuredResultDataApi.UpsertStructuredResultData(documentScope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest}});

            // Create result data key rule specifying result type, quote interval and the resources we want to affect.
            string resourceKey = "UnitResult/*";
            var resultDataKeyRule = new ResultDataKeyRule("Client", documentScope, documentId, resourceKey: resourceKey, documentResultType: resultType, resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.ResultDataKeyRule, quoteInterval: "1D");

            // Create and upsert a recipe with the result data key rule
            var pricingOptions = new PricingOptions {AllowAnyInstrumentsWithSecUidToPriceOffLookup = false, AllowPartiallySuccessfulEvaluation = true};
            var pricingContext = new PricingContext(null, null, pricingOptions, new List<ResultKeyRule>{resultDataKeyRule} );
            var configurationRecipe = new ConfigurationRecipe(documentScope, "recipe", new MarketContext(), pricingContext);
            var upsertRecipeRequest = new UpsertRecipeRequest(configurationRecipe, null);
            _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            // Create a valuation request, requesting multiple results including Strategy and Country.
            // Applying this request to two portfolios.
            // Sorting by portfolio id for reliable reproducibility.
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(documentScope, "recipe"),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec(TestDataUtilities.Luid, AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Portfolio/Id", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Portfolio/Scope", AggregateSpec.OpEnum.Value),
                    new AggregateSpec($"Transaction/{scope1}/Strategy", AggregateSpec.OpEnum.Value),
                    new AggregateSpec($"Transaction/{scope2}/Country", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/Returns/YtD", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/UserDefinedKey", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Holding/Units", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value),

                },
                portfolioEntityIds: new List<PortfolioEntityId>
                {
                    new PortfolioEntityId(scope1, portfolioCode1),
                    new PortfolioEntityId(scope1, portfolioCode2)
                },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveAt),
                sort: new List<OrderBySpec>
                {
                    new OrderBySpec("Portfolio/Id", OrderBySpec.SortOrderEnum.Ascending)
                }
            );

            // Perform valuation and obtain results
            var results = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);


            // We expect the following results
            // | LusidInstrumentId | PortfolioId | PortfolioScope | Strategy  | Country | YtD      | UserDefinedKey | PV Amount | Units  |
            // | ----------------- | ----------- | -------------- | --------- | ------- | -------- | -------------- | --------- | ------ |
            // | generated luid    | pf1-xxxx    | scope1-xxxx    | Strategy1 | England | 0.123456 | test1          |           | 1000   |
            // | generated luid    | pf1-xxxx    | scope1-xxxx    | Strategy2 |         | 1        | test2          |           | 1000   |
            // | CCY_USD           | pf1-xxxx    | scope1-xxxx    | Strategy1 | England | 10       | test_ccy1      | -1000     | -1000  |
            // | CCY_USD           | pf1-xxxx    | scope1-xxxx    | Strategy2 |         | 100      | test_ccy2      | -10000    | -10000 |
            // | generated luid    | pf2-xxxx    | scope1-xxxx    | Strategy1 |         |          |                |           | 1000   |
            // | CCY_USD           | pf2-xxxx    | scope1-xxxx    | Strategy1 |         |          |                | -10000    | -10000 |
            Assert.That(results.Data[0]["UnitResult/Returns/YtD"], Is.EqualTo(0.123456m));
            Assert.That(results.Data[1]["UnitResult/Returns/YtD"], Is.EqualTo(1m));
            Assert.That(results.Data[2]["UnitResult/Returns/YtD"], Is.EqualTo(10m));
            Assert.That(results.Data[3]["UnitResult/Returns/YtD"], Is.EqualTo(100m));
            Assert.That(results.Data[4]["UnitResult/Returns/YtD"], Is.EqualTo(null));
            Assert.That(results.Data[5]["UnitResult/Returns/YtD"], Is.EqualTo(null));
            Assert.That(results.Data[0]["UnitResult/UserDefinedKey"], Is.EqualTo("test1"));
            Assert.That(results.Data[1]["UnitResult/UserDefinedKey"], Is.EqualTo("test2"));
            Assert.That(results.Data[2]["UnitResult/UserDefinedKey"], Is.EqualTo("test_ccy1"));
            Assert.That(results.Data[3]["UnitResult/UserDefinedKey"], Is.EqualTo("test_ccy2"));
            Assert.That(results.Data[4]["UnitResult/UserDefinedKey"], Is.EqualTo(null));
            Assert.That(results.Data[5]["UnitResult/UserDefinedKey"], Is.EqualTo(null));
        }

        // Upserting a document on a portfolio level and checking that it is as one would expect it to be.
        [LusidFeature("F10-13")]
        [Test]
        public void TestFindOrCalculate_PortfolioResult()
        {
            // Setting up basic parameters
            string documentId = "document-1";
            string documentScope = "document-scope-" + Guid.NewGuid();
            string resultType = "UnitResult/Portfolio";
            DataMapKey dataMapKey = new DataMapKey("1.0.0", "test-code");
            DateTimeOffset effectiveAt = new DateTimeOffset(2022, 01, 19, 0, 0, 0, 0, TimeSpan.Zero);

            // Create and upsert a portfolio
            string scope = "scope-" + Guid.NewGuid();
            string portfolioCode = "pf";
            var portfolioRequest = new CreateTransactionPortfolioRequest(
                code: portfolioCode,
                displayName: $"Portfolio-{portfolioCode}",
                baseCurrency: "USD",
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);

            // Create and upsert two simple instruments
            string instrumentName1 = "AClientName1";
            string instrumentName2 = "AClientName2";
            string clientInstId1 = "clientInstId1";
            string clientInstId2 = "clientInstId2";
            var instruments = new Dictionary<string, InstrumentDefinition>
            {
                {
                    "an-inst1", new InstrumentDefinition(instrumentName1,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId1)
                        }}
                    )
                },
                {
                    "an-inst2", new InstrumentDefinition(instrumentName2,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId2)
                        }}
                    )
                },
            };
            var upsertResponse = _instrumentsApi.UpsertInstruments(instruments);

            // Obtain LusidInstrumentIds of the two intsruments above
            List<string> luids = upsertResponse.Values
                .Select(inst => inst.Value.LusidInstrumentId)
                .ToList();

            // Create and upsert transactions, one for each instrument defined above
            var transactionRequest1 = new List<TransactionRequest>
            {
                TestDataUtilities.BuildTransactionRequest(luids[0], 1000, 1m, "USD", effectiveAt, "Buy"),
            };
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest1);

            var transactionRequest2 = new List<TransactionRequest>
            {
                TestDataUtilities.BuildTransactionRequest(luids[1], 1000, 10m, "USD", effectiveAt, "Buy")
            };
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest2);

            // Create and upsert quotes, one for each instrument defined above
            var quoteRequest = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "UniqueKeyForDictionary1",
                luids[0],
                QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                123m,
                "USD",
                TestDataUtilities.EffectiveAt,
                QuoteSeriesId.QuoteTypeEnum.Price);

            TestDataUtilities.BuildQuoteRequest(
                quoteRequest,
                "UniqueKeyForDictionary2",
                luids[1],
                QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                123m,
                "USD",
                TestDataUtilities.EffectiveAt,
                QuoteSeriesId.QuoteTypeEnum.Price);
            _quotesApi.UpsertQuotes(scope, quoteRequest);

            // Create a data mapping for the upcoming document.
            // Composite leaf is used to link PV/Amount and PV/Ccy,
            // not the CompositeLeaf cannot have a name (must be null) and does not appear in the upserted document.
            DataMapping dataMapping = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("Instrument/default/LusidInstrumentId", "LusidInstrumentId", "string",
                    "PartOfUnique"),
                new DataDefinition("Holding/default/Currency", "holding-ccy", "string", "PartOfUnique"),
                new DataDefinition("Valuation/PV", null, "Result0D", "CompositeLeaf"),
                new DataDefinition("Valuation/PV/Amount", "pv", "decimal", "Leaf"),
                new DataDefinition("Valuation/PV/Ccy", "pv-ccy", "string", "Leaf"),
                new DataDefinition("UnitResult/UserDefinedData", "UserDefinedData", "string", "Leaf"),
            });
            var request = new CreateDataMapRequest(dataMapKey, dataMapping);
            _structuredResultDataApi.CreateDataMap(documentScope,
                new Dictionary<string, CreateDataMapRequest> {{"dataMapKey", request}});

            // Generate and upsert the document.
            // NB: the result does _not_ match the portfolio contents, this is to demonstrate/validate that entire calculation is elided
            string document = $"LusidInstrumentId,holding-ccy,pv,pv-ccy,UserDefinedData\n" +
                              $"LUID_TEST0000,USD,100.0,USD,\"exampleData1\"\n" +
                              $"LUID_TEST0001,USD,101.0,ZAR,\"exampleData2\"";
            StructuredResultData structuredResultData = new StructuredResultData("csv", "1.0.0", documentId, document, dataMapKey);
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", documentId, effectiveAt, resultType);
            var upsertDataRequest = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData);
            _structuredResultDataApi.UpsertStructuredResultData(documentScope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest}});

            // Create result data key rule specifying
            var resultDataKeyRule = new PortfolioResultDataKeyRule("Client", documentScope, documentId,  portfolioCode : portfolioCode, portfolioScope : scope, resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.PortfolioResultDataKeyRule);

            // Create and upsert a recipe with the result data key rule
            var pricingOptions = new PricingOptions {AllowAnyInstrumentsWithSecUidToPriceOffLookup = false, AllowPartiallySuccessfulEvaluation = true};
            var pricingContext = new PricingContext(null, null, pricingOptions, new List<ResultKeyRule>{resultDataKeyRule} );
            var configurationRecipe = new ConfigurationRecipe(documentScope, "recipe", new MarketContext(), pricingContext);
            var upsertRecipeRequest = new UpsertRecipeRequest(configurationRecipe, null);
            _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            // Create a valuation request, requesting LusidInstrument Id, Pv amount and UserDefinedData
            var valuationRequest = new ValuationRequest(
                recipeId: new ResourceId(documentScope, "recipe"),
                metrics: new List<AggregateSpec>
                {
                    new AggregateSpec(TestDataUtilities.Luid, AggregateSpec.OpEnum.Value),
                    new AggregateSpec("Valuation/PV", AggregateSpec.OpEnum.Value),
                    new AggregateSpec("UnitResult/UserDefinedData", AggregateSpec.OpEnum.Value)
                },
                portfolioEntityIds: new List<PortfolioEntityId>
                {
                    new PortfolioEntityId(scope, portfolioCode),
                },
                valuationSchedule: new ValuationSchedule(effectiveAt: effectiveAt));

            // Perform valuation and obtain results
            var results = _apiFactory.Api<IAggregationApi>().GetValuation(valuationRequest);

            // | LusidInstrumentId | PV Amount | UserDefinedData |
            // | ----------------- | --------- | --------------- |
            // | LUD_TEST0000      | 100       | exampleData1    |
            // | LUID_TEST0001     | 101       | exampleData2    |
            Assert.That(results.Data[0]["Instrument/default/LusidInstrumentId"], Is.EqualTo("LUID_TEST0000"));
            Assert.That(results.Data[0]["Valuation/PV"], Is.EqualTo(100m));
            Assert.That(results.Data[0]["UnitResult/UserDefinedData"], Is.EqualTo("exampleData1"));
            Assert.That(results.Data[1]["Instrument/default/LusidInstrumentId"], Is.EqualTo("LUID_TEST0001"));
            Assert.That(results.Data[1]["Valuation/PV"], Is.EqualTo(101m));
            Assert.That(results.Data[1]["UnitResult/UserDefinedData"], Is.EqualTo("exampleData2"));
        }

        [LusidFeature("F10-14")]
        [Test, Explicit]
        public void VirtualDocument_Compose_OverSeveralUpserts()
        {
            // Setting up basic parameters
            string scope = "scope-" + Guid.NewGuid();;
            string resultType = "UnitResult/Custom";
            string documentId = "document-1";
            DataMapKey dataMapKey1 = new DataMapKey("1.0.0", "datamap-1");
            DataMapKey dataMapKey2 = new DataMapKey("1.0.0", "datamap-2");
            DateTimeOffset effectiveAt = new DateTimeOffset(2022, 01, 19, 0, 0, 0, 0, TimeSpan.Zero);

            // Creating two data mappings, both containing the same identifiers but different data.
            DataMapping dataMapping1 = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("UnitResult/Id1", "id1", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/Id2", "id2", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/UserData1", "Data1", "string", "Leaf"),
            });
            var request1 = new CreateDataMapRequest(dataMapKey1, dataMapping1);
            _structuredResultDataApi.CreateDataMap(scope, new Dictionary<string, CreateDataMapRequest> {{"dataMapKey1", request1}});

            DataMapping dataMapping2 = new DataMapping(new List<DataDefinition>
            {
                new DataDefinition("UnitResult/Id1", "id1", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/Id2", "id2", "string", "PartOfUnique"),
                new DataDefinition("UnitResult/UserData1", "Data2", "string", "Leaf"),
            });
            var request2 = new CreateDataMapRequest(dataMapKey2, dataMapping2);
            _structuredResultDataApi.CreateDataMap(scope, new Dictionary<string, CreateDataMapRequest> {{"dataMapKey2", request2}});


            // Upserting multiple documents with the same document key:
            // Document 1:
            // | Id1 | Id2 | Data1   |
            // | --- | --- | ------- |
            // | a   | b   | Data_ab |
            // | a   | c   | Data_ac |
            // Document 2:
            // | Id1 | Id2 | Data1   |
            // | --- | --- | ------- |
            // | a   | d   | Data_ad |
            // Document 3:
            // | Id1 | Id2 | Data2    |
            // | --- | --- | -------- |
            // | a   | d   | Data2_ad |
            // The purpose of this is to observe how the composition works
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", documentId, effectiveAt, resultType);

            string document1 = "id1,id2,Data1\na,b,Data_ab\na,c,Data_ac";
            StructuredResultData structuredResultData1 = new StructuredResultData("csv", "1.0.0", documentId, document1, dataMapKey1);
            var upsertDataRequest1 = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData1);
            _structuredResultDataApi.UpsertStructuredResultData(scope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest1}});

            string document2 = "id1,id2,Data1\na,d,Data_ad";
            StructuredResultData structuredResultData2 = new StructuredResultData("csv", "1.0.0", documentId, document2, dataMapKey1);
            var upsertDataRequest2 = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData2);
            _structuredResultDataApi.UpsertStructuredResultData(scope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest2}});

            string document3 = "id1,id2,Data2\na,d,Data2_ad";
            StructuredResultData structuredResultData3 = new StructuredResultData("csv", "1.0.0", documentId, document3, dataMapKey2);
            var upsertDataRequest3 = new UpsertStructuredResultDataRequest(structResultDataId, structuredResultData3);
            _structuredResultDataApi.UpsertStructuredResultData(scope, new Dictionary<string, UpsertStructuredResultDataRequest>{{documentId, upsertDataRequest3}});

            // We expect the following document
            // | Id1 | Id2 | Data1   | Data2    |
            // | --- | --- | ------- | -------- |
            // | a   | b   | Data_ab |          |
            // | a   | c   | Data_ac |          |
            // | a   | d   | Data_ad | Data2_ad |
            var doc = _structuredResultDataApi.GetStructuredResultData(scope,
                new Dictionary<string, StructuredResultDataId>() {{"test", structResultDataId}});
            Thread.Sleep(5000);
            var result = _structuredResultDataApi.GetVirtualDocument(scope, new Dictionary<string, StructuredResultDataId>{{"Client", structResultDataId}});
        }

        // Demonstrating querying using structured result store with overriden cashflows.
        [LusidFeature("F10-15")]
        [Test]
        public void TestPortfolioUpsertableQueryWithOverridenCashFlows()
        {
            // Setting up basic parameters
            var effectiveAt = new DateTimeOffset(2019, 06, 28, 00, 00, 00, TimeSpan.Zero);
            string resultType = "UnitResult/Analytic";
            string recipeScope = "recipe-scope-" + Guid.NewGuid();
            string scope = "scope-" + Guid.NewGuid();
            string portfolioCode = "pf";

            //  Choosing wide date window to pick up 4 out of 5 cashflows on the instrument. The said cashflows and instrument are defined below.
            var effectiveFrom = new DateTimeOffset(2016, 1, 1, 0, 0, 0, 0, TimeSpan.Zero);
            var effectiveTo = new DateTimeOffset(2025, 1, 1, 0, 0, 0, 0, TimeSpan.Zero);

            // Create and upsert a portfolio
            var portfolioRequest = new CreateTransactionPortfolioRequest(
                code: portfolioCode,
                displayName: $"Portfolio-{portfolioCode}",
                baseCurrency: "USD",
                created: effectiveAt
            );
            _transactionPortfoliosApi.CreatePortfolio(scope, portfolioRequest);

            // Create and upsert a simple instrument
            string instrumentName = "AClientName1";
            string clientInstId = "clientInstId1";
            var instruments = new Dictionary<string, InstrumentDefinition>
            {
                {
                    "an-inst1", new InstrumentDefinition(instrumentName,
                        new Dictionary<string, InstrumentIdValue>
                        {{
                            "ClientInternal", new InstrumentIdValue(clientInstId)
                        }}
                    )
                }
            };
            var upsertResponse = _instrumentsApi.UpsertInstruments(instruments);

            // Obtain LusidInstrumentId for the instrument upserted above
            string luid = upsertResponse.Values.First().Value.LusidInstrumentId;

            // Create cashflow value set, 4 of which will be within the requested date range while one will not be.
            var cashFlowValueSet = new CashFlowValueSet(new List<CashFlowValue>()
                {
                    new CashFlowValue(
                        paymentAmount: 100,
                        paymentDate: effectiveAt,
                        paymentCcy: "USD",
                        cashFlowLineage: new CashFlowLineage() {CashFlowType = "Coupon"},
                        resultValueType: ResultValue.ResultValueTypeEnum.CashFlowValue),
                    new CashFlowValue(
                        paymentAmount: 101,
                        paymentDate: effectiveAt.AddDays(7),
                        paymentCcy: "USD",
                        cashFlowLineage: new CashFlowLineage() {CashFlowType = "Coupon"},
                        resultValueType: ResultValue.ResultValueTypeEnum.CashFlowValue),
                    new CashFlowValue(
                        paymentAmount: 102,
                        paymentDate: effectiveAt.AddDays(14),
                        paymentCcy: "USD",
                        cashFlowLineage: new CashFlowLineage() {CashFlowType = "Coupon"},
                        resultValueType: ResultValue.ResultValueTypeEnum.CashFlowValue),
                    new CashFlowValue(
                        paymentAmount: 103,
                        paymentDate: effectiveAt.AddDays(21),
                        paymentCcy: "USD",
                        cashFlowLineage: new CashFlowLineage() {CashFlowType = "Coupon"},
                        resultValueType: ResultValue.ResultValueTypeEnum.CashFlowValue),
                    new CashFlowValue(
                        paymentAmount: 205,
                        paymentDate: effectiveAt.AddYears(20),
                        paymentCcy: "USD",
                        cashFlowLineage: new CashFlowLineage() {CashFlowType = "Coupon"},
                        resultValueType: ResultValue.ResultValueTypeEnum.CashFlowValue),
                },
                ResultValue.ResultValueTypeEnum.CashFlowValueSet);

            // Upsert the cashflow value set
            StructuredResultDataId structResultDataId = new StructuredResultDataId("Client", portfolioCode, effectiveFrom, resultType);
            UpsertResultValuesDataRequest upsertResultValuesDataRequest = new UpsertResultValuesDataRequest(
                structResultDataId,
                new Dictionary<string, string>
                {
                    {"UnitResult/LusidInstrumentId", luid}
                },
                "UnitResult/Valuation/Cashflows",
                cashFlowValueSet);
            _structuredResultDataApi.UpsertResultValue(
                scope,
                new Dictionary<string, UpsertResultValuesDataRequest>{{"dataCashflows", upsertResultValuesDataRequest}}
            );

            // Create and upsert a transaction
            var transactionRequest = new List<TransactionRequest>
            {
                TestDataUtilities.BuildTransactionRequest(luid, 1000, 1m, "USD", effectiveAt, "Buy"),
            };
            _transactionPortfoliosApi.UpsertTransactions(scope, portfolioCode, transactionRequest);

            // Create and upsert a quote for the instrument defined above
            var quoteRequest1 = new Dictionary<string, UpsertQuoteRequest>();
            TestDataUtilities.BuildQuoteRequest(
                quoteRequest1,
                "UniqueKeyForDictionary",
                luid,
                QuoteSeriesId.InstrumentIdTypeEnum.LusidInstrumentId,
                123m,
                "USD",
                effectiveAt,
                QuoteSeriesId.QuoteTypeEnum.Price);
            _quotesApi.UpsertQuotes(scope, quoteRequest1);

            // Create result data key rule specifying, the quote interval is 10Y.
            string resourceKey = "UnitResult/*";
            var resultDataKeyRule = new ResultDataKeyRule(
                "Client",
                scope,
                portfolioCode,
                resourceKey: resourceKey,
                documentResultType: resultType,
                resultKeyRuleType: ResultKeyRule.ResultKeyRuleTypeEnum.ResultDataKeyRule,
                quoteInterval: "10Y"
                );

            // Create and upsert a recipe with the result data key rule
            var pricingOptions = new PricingOptions();
            var pricingContext = new PricingContext(
                null,
                new Dictionary<string, ModelSelection>
                {
                    {
                        "SimpleInstrument",
                        new ModelSelection(ModelSelection.LibraryEnum.Lusid, ModelSelection.ModelEnum.SimpleStatic)
                    }
                },
                pricingOptions,
                new List<ResultKeyRule>{resultDataKeyRule} );
            // Note it is necessary to specify default scope one is working in, the quotes must also be present.
            var configurationRecipe = new ConfigurationRecipe(
                recipeScope,
                "recipe-code",
                new MarketContext(options: new MarketOptions(defaultScope: scope)),
                pricingContext);
            var upsertRecipeRequest = new UpsertRecipeRequest(configurationRecipe);
            _recipeApi.UpsertConfigurationRecipe(upsertRecipeRequest);

            // Make a request for portfolio cashflows
            var response = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope,
                portfolioCode,
                effectiveAt,
                effectiveFrom,
                effectiveTo,
                null,
                string.Empty,
                recipeScope,
                "recipe-code"
                );

           // Expecing a response.Values to contain a list of InstrumentCashflows, of which we expect the following property values.
           // | Amount | Currency | PaymentDate          |
           // | ------ | -------- | -------------------- |
           // | 100000 | USD      | 28/6/2019 1:00:00 AM |
           // | 101000 | USD      | 05/7/2019 1:00:00 AM |
           // | 102000 | USD      | 12/7/2019 1:00:00 AM |
           // | 103000 | USD      | 19/7/2019 1:00:00 AM |
           var responseAmounts = response.Values.Select(x => x.Amount).ToList();
           var responseCurrencies = response.Values.Select(x => x.Currency).ToList();
           Assert.That(responseAmounts, Is.EquivalentTo(new List<decimal> {100000, 101000, 102000, 103000}));
           Assert.That(responseCurrencies, Is.EquivalentTo(new List<string> {"USD", "USD", "USD", "USD",}));
        }
    }
}
