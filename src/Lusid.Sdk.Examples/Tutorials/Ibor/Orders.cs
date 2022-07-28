using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lusid.Sdk.Api;
using Lusid.Sdk.Client;
using Lusid.Sdk.Model;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Ibor
{
    /// <summary>
    /// Orders represent an instruction from an investor to buy or sell a quantity of a specific
    /// security.
    /// </summary>
    [TestFixture]
    public class Orders: TutorialBase
    {
        private InstrumentLoader _instrumentLoader;
        private IList<string> _instrumentIds;

        private readonly IDictionary<string, string> _tutorialScopes = new Dictionary<string, string> { };

        private readonly IList<string> _tutorialPropertyCodes = new List<string> {
            "TIF",
            "OrderBook",
            "PortfolioManager",
            "Account",
            "Strategy",
            "Scope",
            "Code"
        };

        [OneTimeSetUp]
        public void SetUp()
        {
            _instrumentLoader = new InstrumentLoader(_apiFactory);
            _instrumentIds = _instrumentLoader.LoadInstruments();

            var guid = Guid.NewGuid().ToString();

            _tutorialScopes["simple-upsert"] = $"Orders-SimpleUpsert-{guid}";
            _tutorialScopes["unknown-instrument"] = $"Orders-UnknownInstrument-{guid}";
            _tutorialScopes["filtering"] = $"Orders-Filter-{guid}";
            
            LoadProperties();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            var propertyDefinitionApi = _apiFactory.Api<IPropertyDefinitionsApi>();

            try
            {
                foreach (var scope in _tutorialScopes.Values)
                {
                    foreach (var code in _tutorialPropertyCodes)
                    {
                        propertyDefinitionApi.DeletePropertyDefinition(
                            domain: CreatePropertyDefinitionRequest.DomainEnum.Order.ToString(),
                            scope: scope,
                            code: code);
                    }
                }
            }
            catch (ApiException)
            {
                //  dont fail if delete fails
            }
        }

        private void LoadProperties()
        {
            foreach (var scope in _tutorialScopes.Values)
            {
                foreach (var code in _tutorialPropertyCodes)
                {
                    //    Create the property definitions
                    try
                    {
                        _apiFactory
                            .Api<IPropertyDefinitionsApi>()
                            .CreatePropertyDefinition(
                                createPropertyDefinitionRequest: MakePropertyDefinition(propertyScope: scope,
                                    propertyCode: code));
                    }
                    catch (ApiException apiException) when (apiException.Message.Contains("PropertyAlreadyExists")) 
                    {
                        // Ignore if it already exists
                    }

                }   
            }
            
            CreatePropertyDefinitionRequest MakePropertyDefinition(string propertyScope, string propertyCode)
                // Details of the property to be created
                => new CreatePropertyDefinitionRequest(
                    domain: CreatePropertyDefinitionRequest.DomainEnum.Order,
                    scope: propertyScope,
                    lifeTime: CreatePropertyDefinitionRequest.LifeTimeEnum.Perpetual,
                    code: propertyCode,
                    valueRequired: false,
                    displayName: $"Order {propertyCode}",
                    dataTypeId: new ResourceId("system", "string")
                );
            
        }
        
        [LusidFeature("F4")]
        [Test]
        public void Upsert_Simple_Order()
        {
            var testScope = _tutorialScopes["simple-upsert"];
            var order = $"Order-{Guid.NewGuid().ToString()}";
            var orderId = new ResourceId(testScope, order);

            // We want to make a request for a single order. The internal security id will be mapped on upsert
            // from the instrument identifiers passed.
            var request = new OrderSetRequest(
                orderRequests: new List<OrderRequest>
                {
                    new OrderRequest(
                        id: orderId,
                        quantity: 100,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio.
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("J Wilson")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("RiskArb")) },
                        },
                        
                        side: "Buy", 
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    )
                });

            // We can ask the Orders API to upsert this order for us
            var upsertResult = _ordersApi.UpsertOrders(request);

            // The return gives us a list of orders upserted, and LusidInstrument for each has been mapped to a LUID
            // using the instrument identifiers passed
            Assert.That(upsertResult.Values.Count == 1);
            Assert.That(upsertResult.Values.All(rl => rl.Id.Code.Equals(order)));
            Assert.That(upsertResult.Values.All(rl => rl.LusidInstrumentId.Equals(_instrumentIds.First())));
        }
        
        [LusidFeature("F5")]
        [Test]
        public void Upsert_Simple_Order_With_Unknown_Instrument()
        {
            var testScope = _tutorialScopes["unknown-instrument"];
            var order = $"Order-{Guid.NewGuid().ToString()}";
            var orderId = new ResourceId(testScope, order);

            // We want to make a request for a single order. We'll map the internal security id to an Unknown placeholder
            // if we can't translate it.
            var initialRequest = new OrderSetRequest(
                orderRequests: new List<OrderRequest>
                {
                    new OrderRequest(
                        id: orderId,
                        quantity: 100,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = "LUID_SomeNonexistentInstrument"
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("J Wilson")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("RiskArb")) },
                        },
                        side: "Buy", 
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    )
                });

            // We can ask the Orders API to upsert this order for us
            var upsertResult = _ordersApi.UpsertOrders(initialRequest);

            // The return gives us a list of orders upserted, and LusidInstrument for each has been mapped to a LUID
            // using the instrument identifiers passed
            Assert.That(upsertResult.Values.Count == 1);
            Assert.That(upsertResult.Values.All(rl => rl.Id.Code.Equals(order)));
            Assert.That(upsertResult.Values.All(rl => rl.LusidInstrumentId.Equals("LUID_ZZZZZZZZ")));
        }
        
        [LusidFeature("F6")]
        [Test]
        public void Update_Simple_Order()
        {
            var testScope = _tutorialScopes["simple-upsert"];
            var order = $"Order-{Guid.NewGuid().ToString()}";
            var orderId = new ResourceId(testScope, order);

            // We want to make a request for a single order. The internal security id will be mapped on upsert
            // from the instrument identifiers passed. Properties
            var request = new OrderSetRequest(
                orderRequests: new List<OrderRequest>
                {
                    new OrderRequest(
                        id: orderId,
                        quantity: 100,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>(),
                        side: "Buy",
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    )
                });

            var upsertResult = _ordersApi.UpsertOrders(request);

            // The return gives us a list of orders upserted, and LusidInstrument for each has been mapped to a LUID
            // using the instrument identifiers passed
            Assert.That(upsertResult.Values.Count == 1);
            Assert.That(upsertResult.Values.All(rl => rl.Id.Code.Equals(order)));
            Assert.That(upsertResult.Values.All(rl => rl.LusidInstrumentId.Equals(_instrumentIds.First())));
            Assert.That(upsertResult.Values.All(rl => rl.Quantity == 100));
            Assert.That(upsertResult.Values.All(rl => !rl.Properties.Any()));
            
            // We can update that Order with a new Quantity, and some extra parameters
            var updateRequest = new OrderSetRequest(
                orderRequests: new List<OrderRequest>
                {
                    new OrderRequest(
                        id: orderId,
                        quantity: 500,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("J Wilson")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("RiskArb")) },
                        },
                        side: "Buy",
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                        )
                });
            
            upsertResult = _ordersApi.UpsertOrders(updateRequest);
            
            // The return gives us a list of orders upserted, and LusidInstrument for each has been mapped to a LUID
            // using the instrument identifiers passed. We can see that the quantity has been udpated, and properties added
            Assert.That(upsertResult.Values.Count == 1);
            Assert.That(upsertResult.Values.All(rl => rl.Id.Code.Equals(order)));
            Assert.That(upsertResult.Values.All(rl => rl.LusidInstrumentId.Equals(_instrumentIds.First())));
            Assert.That(upsertResult.Values.All(rl => rl.Quantity == 500));
            Assert.That(upsertResult.Values.All(rl => rl.Properties.Count() == 5));
        }
        
        [LusidFeature("F7")]
        [Test]
        public void Upsert_And_Retrieve_Simple_Orders()
        {
            var testScope = _tutorialScopes["filtering"];
            var order1 = $"Order-{Guid.NewGuid().ToString()}";
            var order2 = $"Order-{Guid.NewGuid().ToString()}";
            var order3 = $"Order-{Guid.NewGuid().ToString()}";
            var orderId1 = new ResourceId(testScope, order1);
            var orderId2 = new ResourceId(testScope, order2);
            var orderId3 = new ResourceId(testScope, order3);

            // We want to make a request to upsert several orders. The internal security id will be mapped on upsert
            // from the instrument identifiers passed. We can filter on a number of parameters on query.
            var request = new OrderSetRequest(
                orderRequests: new List<OrderRequest>
                {
                    new OrderRequest(
                        id: orderId1,
                        quantity: 99,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("ZB123")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("RiskArb")) },
                            { $"Order/{testScope}/Scope", new PerpetualProperty($"Order/{testScope}/Scope", new PropertyValue(orderId1.Scope)) },
                            { $"Order/{testScope}/Code", new PerpetualProperty($"Order/{testScope}/Code", new PropertyValue(orderId1.Code)) },
                        },
                        side: "Buy",
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    ),
                    new OrderRequest(
                        id: orderId2,
                        quantity: 200,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.First()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("J Wilson")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("UK Growth")) },
                            { $"Order/{testScope}/Scope", new PerpetualProperty($"Order/{testScope}/Scope", new PropertyValue(orderId2.Scope)) },
                            { $"Order/{testScope}/Code", new PerpetualProperty($"Order/{testScope}/Code", new PropertyValue(orderId2.Code)) },
                        },
                        side: "Sell",
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    ),
                    new OrderRequest(
                        id: orderId3,
                        quantity: 300,
                        // These instrument identifiers should all map to the same instrument. If the instance of
                        // LUSID has the specified instruments registered these identifiers will get resolved to
                        // an actual internal instrument on upsert; otherwise, they'll be resolved to instrument or
                        // currency unknown.
                        instrumentIdentifiers: new Dictionary<string, string>()
                        {
                            ["Instrument/default/LusidInstrumentId"] = _instrumentIds.Skip(1).Take(1).Single()
                        },
                        // [Experimental] Currently this portfolio doesn't need to exist. As the domain model evolves
                        // this reference might disappear, or might become a strict reference to an existing portfolio
                        portfolioId: new ResourceId(testScope, "OrdersTestPortfolio"),
                        properties: new Dictionary<string, PerpetualProperty>
                        {
                            { $"Order/{testScope}/TIF", new PerpetualProperty($"Order/{testScope}/TIF", new PropertyValue("GTC")) },
                            { $"Order/{testScope}/OrderBook", new PerpetualProperty($"Order/{testScope}/OrderBook", new PropertyValue("UK Test Orders 2")) },
                            { $"Order/{testScope}/PortfolioManager", new PerpetualProperty($"Order/{testScope}/PortfolioManager", new PropertyValue("F Bar")) },
                            { $"Order/{testScope}/Account", new PerpetualProperty($"Order/{testScope}/Account", new PropertyValue("J Wilson")) },
                            { $"Order/{testScope}/Strategy", new PerpetualProperty($"Order/{testScope}/Strategy", new PropertyValue("RiskArb")) },
                            { $"Order/{testScope}/Scope", new PerpetualProperty($"Order/{testScope}/Scope", new PropertyValue(orderId3.Scope)) },
                            { $"Order/{testScope}/Code", new PerpetualProperty($"Order/{testScope}/Code", new PropertyValue(orderId3.Code)) },
                        },
                        side: "Buy",
                        state: "New", 
                        type: "Limit", 
                        date: DateTimeOffset.Parse("2022-07-02")
                    )
                });

            // We can ask the Orders API to upsert these orders for us
            var upsertResult = _ordersApi.UpsertOrders(request);

            // The return gives us a list of orders upserted, and LusidInstrument for each has been mapped to a LUID
            // using the instrument identifiers passed
            Assert.That(upsertResult.Values.Count, Is.EqualTo(3));
            Assert.That(upsertResult.Values.Single(rl => rl.Id.Code.Equals(order1)).LusidInstrumentId, Is.EqualTo(_instrumentIds.First()));

            var t = upsertResult.Values.First().Version.AsAtDate;

            var order1Filter = $"{order1}";
            var order2Filter = $"{order2}";
            var order3Filter = $"{order3}";
            
            // In order to enable efficient filtering, LUSID indexes upserted data under the hood. Here,
            // we wait for a few seconds whilst this happens.
            Thread.Sleep(5000);

            var quantityFilter = _ordersApi.ListOrders(
                asAt: t,
                filter:
                      $"Quantity gt 100 "
                    + $"and properties.Order/{testScope}/Scope eq '{testScope}' "
                    + $"and properties.Order/{testScope}/Code in ('{order1}', '{order2}', '{order3}')"
                );

            Assert.That(quantityFilter.Values.Count, Is.EqualTo(2));
            Assert.That(quantityFilter.Values.All(rl => rl.Quantity > 100));
            
            /*
             * Other filters are also possible:
             *
            
            var propertyFilter = _ordersApi.ListOrders(asAt: t, filter: $"Properties[Order/{testScope}/OrderBook] eq 'UK Test Orders 2'");

            Assert.That(propertyFilter.Values.Count, Is.EqualTo(1));
            Assert.That(propertyFilter.Values.Single(rl => rl.Id.Code.Equals(order3)).Properties[$"Order/{testScope}/OrderBook"].Value.LabelValue, Is.EqualTo("UK Test Orders 2"));

            var instrumentFilter = _ordersApi.ListOrders(asAt: t, filter: $"LusidInstrumentId eq '{_instrumentIds.First()}' and Id.Scope eq '{testScope}'");

            Assert.That(instrumentFilter.Values.Count, Is.EqualTo(2));
            Assert.That(instrumentFilter.Values.All(rl => rl.LusidInstrumentId.Equals(_instrumentIds[0])));

            var sideFilter = _ordersApi.ListOrders(asAt: t, filter: $"Side eq 'Sell' and Id.Scope eq '{testScope}'");

            Assert.That(sideFilter.Values.Count, Is.EqualTo(1));
            Assert.That(sideFilter.Values.All(rl => rl.Side.Equals("Sell")));
            */

        } 
    } 
}