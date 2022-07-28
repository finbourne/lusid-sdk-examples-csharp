using System;
using System.Collections.Generic;
using System.Linq;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using LusidFeatures;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    [TestFixture]
    public class BondExamples: DemoInstrumentBase
    {
        private static bool IsZeroCouponBond(Bond bond) => bond.FlowConventions.PaymentFrequency == "0Invalid";

        /// <inheritdoc />
        protected override void CreateAndUpsertInstrumentResetsToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument instrument)
        {
            // nothing required.
        }

        /// <inheritdoc />
        protected override void CreateAndUpsertMarketDataToLusid(string scope, ModelSelection.ModelEnum model, LusidInstrument bond)
        {
            if (model != ModelSelection.ModelEnum.ConstantTimeValueOfMoney)
            {
                var upsertComplexMarketDataRequest = new Dictionary<string, UpsertComplexMarketDataRequest>
                {
                    {"discountCurve", TestDataUtilities.BuildRateCurveRequest(TestDataUtilities.EffectiveAt, "USD", "OIS", TestDataUtilities.ExampleDiscountFactors1)}
                };
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
            var bond = (Bond) instrument;
            var cashflows = _transactionPortfoliosApi.GetPortfolioCashFlows(
                scope: scope,
                code: portfolioCode,
                effectiveAt: TestDataUtilities.EffectiveAt,
                windowStart: bond.StartDate.AddDays(-3),
                windowEnd: bond.MaturityDate.AddDays(3),
                asAt:null,
                filter:null,
                recipeIdScope: scope,
                recipeIdCode: recipeCode).Values;

            // In case of a zero coupon bond, we expect only a single payment at the bond maturity.
            // Otherwise, we expect regular payments (=cashflows) depending on the bond face value and
            // coupon rate.
            Assert.That(cashflows.Count, Is.EqualTo(IsZeroCouponBond(bond) ? 1 : 13));
            // We perform here a very simple check that a bond cashflow must be positive.
            var allCashFlowsPositive = cashflows.All(cf => cf.Amount > 0);
            Assert.That(allCashFlowsPositive, Is.True);
        }

        [LusidFeature("F5-18")]
        [Test]
        public void BondCreationAndUpsertionExample()
        {
            // CREATE a Bond (that can then be upserted into LUSID)
            var bond = (Bond) InstrumentExamples.CreateExampleBond();
            
            // ASSERT that it was created
            Assert.That(bond, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            string uniqueId = bond.InstrumentType + Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(bond, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);
            
            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, asAt: upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.Bond);
            var roundTripBond = retrieved as Bond;
            Assert.That(roundTripBond, Is.Not.Null);
            Assert.That(roundTripBond.Principal, Is.EqualTo(bond.Principal));
            Assert.That(roundTripBond.CouponRate, Is.EqualTo(bond.CouponRate));
            Assert.That(roundTripBond.DomCcy, Is.EqualTo(bond.DomCcy));
            Assert.That(roundTripBond.MaturityDate, Is.EqualTo(bond.MaturityDate));
            Assert.That(roundTripBond.StartDate, Is.EqualTo(bond.StartDate));
            Assert.That(roundTripBond.FlowConventions.Currency, Is.EqualTo(bond.FlowConventions.Currency));
            Assert.That(roundTripBond.FlowConventions.PaymentFrequency, Is.EqualTo(bond.FlowConventions.PaymentFrequency));
            Assert.That(roundTripBond.FlowConventions.ResetDays, Is.EqualTo(bond.FlowConventions.ResetDays));
            Assert.That(roundTripBond.FlowConventions.SettleDays, Is.EqualTo(bond.FlowConventions.SettleDays));
            Assert.That(roundTripBond.FlowConventions.PaymentCalendars.Count, Is.EqualTo(bond.FlowConventions.PaymentCalendars.Count));
            Assert.That(roundTripBond.FlowConventions.PaymentCalendars, Is.EquivalentTo(bond.FlowConventions.PaymentCalendars));
            
            // DELETE Instrument 
            _instrumentsApi.DeleteInstrument("ClientInternal", uniqueId); 
        }
        
        [LusidFeature("F5-7")]
        [Test]
        public void ZeroCouponBondCreationAndUpsertionExample()
        {
            // CREATE the flow conventions for bond
            // To be recognised as a zero coupon bond, the paymentFrequency must be "0Invalid"
            // and the coupon rate must be 0.
            var flowConventions = new FlowConventions(
                currency: "GBP",
                paymentFrequency: "0Invalid",
                rollConvention: "None",
                dayCountConvention: "Invalid",
                paymentCalendars:new List<string>(),
                resetCalendars:new List<string>(),
                settleDays: 2,
                resetDays: 2);

            // CREATE a Bond (that can then be upserted into LUSID)
            var bond = new Bond(
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 9, 18, 0, 0, 0, TimeSpan.Zero),
                domCcy: "GBP",
                principal: 100m,
                couponRate: 0m,
                flowConventions: flowConventions,
                identifiers: new Dictionary<string, string>(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Bond);
            
            // ASSERT that it was created
            Assert.That(bond, Is.Not.Null);

            // CAN NOW UPSERT TO LUSID
            var uniqueId = bond.InstrumentType + Guid.NewGuid().ToString(); 
            var instrumentsIds = new List<(LusidInstrument, string)>{(bond, uniqueId)};
            var definitions = TestDataUtilities.BuildInstrumentUpsertRequest(instrumentsIds);
            
            var upsertResponse = _instrumentsApi.UpsertInstruments(definitions);
            ValidateUpsertInstrumentResponse(upsertResponse);

            // CAN NOW QUERY FROM LUSID
            var getResponse = _instrumentsApi.GetInstruments("ClientInternal", new List<string> { uniqueId }, upsertResponse.Values.First().Value.Version.AsAtDate);
            ValidateInstrumentResponse(getResponse, uniqueId);
            
            var retrieved = getResponse.Values.First().Value.InstrumentDefinition;
            Assert.That(retrieved.InstrumentType == LusidInstrument.InstrumentTypeEnum.Bond);
            var roundTripBond = retrieved as Bond;
            Assert.That(roundTripBond, Is.Not.Null);
            Assert.That(roundTripBond.Principal, Is.EqualTo(bond.Principal));
            Assert.That(roundTripBond.CouponRate, Is.EqualTo(bond.CouponRate));
            Assert.That(roundTripBond.DomCcy, Is.EqualTo(bond.DomCcy));
            Assert.That(roundTripBond.MaturityDate, Is.EqualTo(bond.MaturityDate));
            Assert.That(roundTripBond.StartDate, Is.EqualTo(bond.StartDate));
            Assert.That(roundTripBond.FlowConventions.Currency, Is.EqualTo(bond.FlowConventions.Currency));
            Assert.That(roundTripBond.FlowConventions.PaymentFrequency, Is.EqualTo(bond.FlowConventions.PaymentFrequency));
            Assert.That(roundTripBond.FlowConventions.ResetDays, Is.EqualTo(bond.FlowConventions.ResetDays));
            Assert.That(roundTripBond.FlowConventions.SettleDays, Is.EqualTo(bond.FlowConventions.SettleDays));
            Assert.That(roundTripBond.FlowConventions.PaymentCalendars.Count, Is.EqualTo(bond.FlowConventions.PaymentCalendars.Count));
            Assert.That(roundTripBond.FlowConventions.PaymentCalendars, Is.EquivalentTo(bond.FlowConventions.PaymentCalendars));
        }
        
        [LusidFeature("F22-1")]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic, false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, false)]
        [TestCase(ModelSelection.ModelEnum.Discounting, false)]
        [TestCase(ModelSelection.ModelEnum.SimpleStatic, true)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney, true)]
        [TestCase(ModelSelection.ModelEnum.Discounting, true)]
        public void BondGetValuationExample(ModelSelection.ModelEnum modelName, bool isZeroCouponBond)
        {
            // CREATE a Bond to be priced by LUSID
            var bond = isZeroCouponBond ? InstrumentExamples.CreateExampleZeroCouponBond() : InstrumentExamples.CreateExampleBond();
            CallLusidGetValuationEndpoint(bond, modelName);
        }
        
        [LusidFeature("F22-2")]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney,false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney,true)]
        [TestCase(ModelSelection.ModelEnum.Discounting,false)]
        [TestCase(ModelSelection.ModelEnum.Discounting,true)]
        public void BondGetInlineValuationExample(ModelSelection.ModelEnum modelName,bool isZeroCouponBond)
        {
            // CREATE a Bond to be priced by LUSID
            var bond = isZeroCouponBond ? InstrumentExamples.CreateExampleZeroCouponBond() : InstrumentExamples.CreateExampleBond();
            CallLusidInlineValuationEndpoint(bond, modelName);
        }

        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney,false)]
        [TestCase(ModelSelection.ModelEnum.ConstantTimeValueOfMoney,true)]
        [TestCase(ModelSelection.ModelEnum.Discounting,false)]
        [TestCase(ModelSelection.ModelEnum.Discounting,true)]
        public void BondPortfolioCashFlowsExample(ModelSelection.ModelEnum modelName,bool isZeroCouponBond)
        {
            var bond = isZeroCouponBond ? InstrumentExamples.CreateExampleZeroCouponBond() : InstrumentExamples.CreateExampleBond();
            CallLusidGetPortfolioCashFlowsEndpoint(bond, modelName);
        }
    }
}
