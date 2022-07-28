using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Api;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.MarketData
{
    [TestFixture]
    public class Quotes: TutorialBase
    {
        
        [LusidFeature("F28")]
        [Test]
        public void Add_Quote()
        {
            var request = new UpsertQuoteRequest(
                quoteId: new QuoteId(
                    new QuoteSeriesId(
                        provider: "DataScope",
                        priceSource: "BankA",
                        instrumentId: "BBG000B9XRY4",
                        instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.Figi,
                        quoteType: QuoteSeriesId.QuoteTypeEnum.Price,
                        field: "mid"),
                    new DateTimeOffset(2019, 4, 15, 0, 0, 0, TimeSpan.Zero).ToString("o")),
                metricValue: new MetricValue(
                    value: 199.23M,
                    unit: "USD"),
                lineage: "InternalSystem");

            _quotesApi.UpsertQuotes(TestDataUtilities.TutorialScope, new Dictionary<string, UpsertQuoteRequest> { { "correlationId", request} });
        }
        
        [LusidFeature("F29")]
        [Test]
        public void Get_Quote_For_Instrument_For_Single_Day()
        {
            var quoteSeriesId = new QuoteSeriesId(
                    provider: "DataScope",
                    priceSource: "BankA",
                    instrumentId: "BBG000B9XRY4",
                    instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.Figi,
                    quoteType: QuoteSeriesId.QuoteTypeEnum.Price,
                    field: "mid");
            var effectiveDate = new DateTimeOffset(2019, 4, 15, 0, 0, 0, TimeSpan.Zero);
            
            //  Get the close quote for AAPL on 15-Apr-19
            var quoteResponse = _quotesApi.GetQuotes(
                TestDataUtilities.TutorialScope,
                effectiveAt: effectiveDate.ToString("o"),
                requestBody: new Dictionary<string, QuoteSeriesId> {{"correlationId", quoteSeriesId}});
            
            Assert.That(quoteResponse.Values.Count, Is.EqualTo(1));

            Quote quote = quoteResponse.Values.First().Value;
            
            Assert.That(quote.MetricValue.Value, Is.EqualTo(199.23));
        }
        
        [LusidFeature("F30")]
        [Test]
        public void Get_Timeseries_Quotes()
        {
            var startDate = new DateTimeOffset(2019, 4, 15, 0, 0, 0, TimeSpan.Zero);
            var dateRange = Enumerable.Range(0, 30).Select(offset => startDate.AddDays(offset));
            
            var quoteSeriesId = new QuoteSeriesId(
                provider: "Client",
                instrumentId: "BBG000DMBXR2",
                instrumentIdType: QuoteSeriesId.InstrumentIdTypeEnum.Figi,
                quoteType: QuoteSeriesId.QuoteTypeEnum.Price,
                field: "mid");
            
            //    Get the quotes for each day in the date range
            var quoteResponses = dateRange
                .Select(d =>
                    _quotesApi.GetQuotes(
                        TestDataUtilities.MarketDataScope,
                        effectiveAt: d.ToString("o"),
                        requestBody:
                        new Dictionary<string, QuoteSeriesId> {{"correlationId", quoteSeriesId}}))
                .SelectMany(q => q.Values)
                .ToList();
            
            Assert.That(quoteResponses, Has.Count.EqualTo(30));
        }
    }
}