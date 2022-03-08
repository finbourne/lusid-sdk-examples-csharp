using System;
using System.Collections.Generic;
using Lusid.Sdk.Model;

namespace Lusid.Sdk.Examples.Utilities
{
    public static class InstrumentExamples
    {
        private static readonly DateTimeOffset TestEffectiveAt = new DateTimeOffset(2020, 2, 23, 0, 0, 0, TimeSpan.Zero);

        public static LusidInstrument GetExampleInstrument(string instrumentName)
        {
            return instrumentName switch
            {
                nameof(Bond) => CreateExampleBond(),
                nameof(FxForward) => CreateExampleFxForward(),
                nameof(FxOption) => CreateExampleFxOption(),
                nameof(InterestRateSwap) => CreateExampleInterestRateSwap(),
                nameof(CreditDefaultSwap) => CreateExampleCreditDefaultSwap(),
                nameof(ContractForDifference) => CreateExampleCfd(),
                _ => throw new ArgumentOutOfRangeException($"Please implement case for instrument {instrumentName}")
            };
        }

        internal static LusidInstrument CreateExampleFxForward(bool isNdf = true)
            => new FxForward(
                domAmount: 1m,
                fgnAmount: -123m,
                domCcy: "USD",
                fgnCcy: "JPY",
                refSpotRate: 100m,
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 9, 18, 0, 0, 0, TimeSpan.Zero),
                fixingDate: new DateTimeOffset(2020, 8, 18, 0, 0, 0, TimeSpan.Zero),
                isNdf: isNdf,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FxForward
            );

        internal static LusidInstrument CreateExampleFxOption(bool isDeliveryNotCash = true)
            => new FxOption(
                strike: 130,
                domCcy: "USD",
                fgnCcy: "JPY",
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                optionMaturityDate: new DateTimeOffset(2020, 12, 18, 0, 0, 0, TimeSpan.Zero),
                optionSettlementDate: new DateTimeOffset(2020, 12, 21, 0, 0, 0, TimeSpan.Zero),
                isCallNotPut: true,
                isDeliveryNotCash: isDeliveryNotCash,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FxOption
            );

        internal static LusidInstrument CreateExampleEquityOption(bool isCashSettled = false)
            => new EquityOption(
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                optionMaturityDate: new DateTimeOffset(2020, 12, 19, 0, 0, 0, TimeSpan.Zero),
                optionSettlementDate: new DateTimeOffset(2020, 12, 21, 0, 0, 0, TimeSpan.Zero),
                deliveryType: isCashSettled
                    ? "Cash"
                    : "Physical",
                optionType: "Call",
                strike: 130,
                domCcy: "USD",
                underlyingIdentifier: "RIC",
                code: "ACME",
                instrumentType: LusidInstrument.InstrumentTypeEnum.EquityOption
            );

        internal static LusidInstrument CreateExampleSimpleInstrument()
            => new SimpleInstrument(
                instrumentType: LusidInstrument.InstrumentTypeEnum.SimpleInstrument, 
                domCcy: "USD", 
                assetClass: SimpleInstrument.AssetClassEnum.Equities, 
                simpleInstrumentType: "Equity"
            );

        internal static LusidInstrument CreateExampleEquity()
            => new Equity(
                instrumentType: LusidInstrument.InstrumentTypeEnum.Equity, 
                domCcy: "USD", 
                identifiers: new EquityAllOfIdentifiers(isin: "US-000402625-0") 
            );

        private static FlowConventions CreateExampleFlowConventions()
            => new FlowConventions(
                currency: "USD",
                paymentFrequency: "6M",
                rollConvention: "MF",
                dayCountConvention: "Act365",
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                settleDays: 2,
                resetDays: 2);

        internal static LusidInstrument CreateExampleBond()
            => new Bond(
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 9, 18, 0, 0, 0, TimeSpan.Zero),
                domCcy: "USD",
                principal: 100m,
                couponRate: 0.05m,
                flowConventions: CreateExampleFlowConventions(),
                identifiers: new Dictionary<string, string>(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Bond
            );

        internal static LusidInstrument CreateExampleCfd()
            => new ContractForDifference(
                startDate: new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 2, 2, 0, 0, 0, TimeSpan.Zero),
                code: "some-id",
                contractSize: 10m,
                payCcy: "USD",
                referenceRate: 0,
                type: "Futures",
                underlyingCcy: "USD",
                underlyingIdentifier: "RIC",
                instrumentType: LusidInstrument.InstrumentTypeEnum.ContractForDifference
            );

        internal static LusidInstrument CreateExampleZeroCouponBond()
            => new Bond(
                startDate: new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 9, 18, 0, 0, 0, TimeSpan.Zero),
                domCcy: "USD",
                principal: 100m,
                couponRate: 0m,
                flowConventions: 
                    new FlowConventions(
                        currency: "USD",
                        paymentFrequency: "0Invalid",
                        rollConvention: "MF",
                        dayCountConvention: "Act365",
                        paymentCalendars: new List<string>(),
                        resetCalendars: new List<string>(),
                        settleDays: 2
                    ),
                identifiers: new Dictionary<string, string>(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Bond
            );

        internal static LusidInstrument CreateExampleInterestRateSwap()
        {
            // CREATE an Interest Rate Swap (IRS) (that can then be upserted into LUSID)
            var startDate = TestEffectiveAt;
            var maturityDate = startDate.AddYears(3);

            // CREATE the fixed and floating leg
            var idxConvention = CreateExampleIndexConventions("GbpLibor6m");
            var flowConventions = CreateExampleFlowConventions();
            var fixedLeg = CreateExampleFixedLeg(startDate, maturityDate, flowConventions);
            var floatLeg = CreateExampleFloatLeg(startDate, maturityDate, flowConventions, idxConvention);

            return new InterestRateSwap(
                startDate: startDate,
                maturityDate: maturityDate,
                legs: new List<InstrumentLeg>
                {
                    floatLeg,
                    fixedLeg
                },
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwap
            );
        }

        private static IndexConvention CreateExampleIndexConventions(string code)
        {
            return new IndexConvention(
                code: code,
                publicationDayLag: 0,
                currency: "USD",
                paymentTenor: "6M",
                dayCountConvention: "Act365",
                fixingReference: "BP00",
                indexName: "LIBOR"
            );
        }

        /// <summary>
        /// One can define an IRS (interest rate swap) by explicitly defining all parameters and in particular the flow and index convention variables.
        /// LUSID allows us to book flow IRS without the need to explicitly write out the flow and index convention by providing a semantic name for commonly understood ones.
        /// For example, new FlowConventionName(currency: "GBP", tenor: "3M") and new FlowConventionName(currency: "GBP", tenor: "3M", indexName:"LIBOR")
        /// </summary>
        internal static InterestRateSwap CreateSwapByNamedConventions()
        {
            var startDate = new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero);
            var maturityDate = new DateTimeOffset(2030, 2, 7, 0, 0, 0, TimeSpan.Zero);
            decimal fixedRate = 0.02m;
            string fixedLegDirection = "Pay";
            decimal notional = 100m;

            // CREATE the flow conventions, index convention
            FlowConventionName flowConventionName = new FlowConventionName(currency: "USD", tenor: "6M");
            FlowConventionName indexConventionName =
                new FlowConventionName(currency: "USD", tenor: "6M", indexName: "LIBOR");

            var floatingLegDirection = fixedLegDirection == "Pay" ? "Receive" : "Pay";

            // CREATE the leg definitions
            var fixedLegDef = new LegDefinition(
                rateOrSpread: fixedRate, // fixed leg rate (swap rate)
                stubType: "Front",
                payReceive: fixedLegDirection,
                notionalExchangeType: "None",
                conventionName: flowConventionName
            );

            var floatLegDef = new LegDefinition(
                rateOrSpread: 0,
                stubType: "Front",
                payReceive: floatingLegDirection,
                notionalExchangeType: "None",
                conventionName: flowConventionName,
                indexConventionName: indexConventionName
            );

            // CREATE the fixed leg
            var fixedLeg = new FixedLeg(
                notional: notional,
                startDate: startDate,
                maturityDate: maturityDate,
                legDefinition: fixedLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FixedLeg
                );

            // CREATE the floating leg
            var floatLeg = new FloatingLeg(
                notional: notional,
                startDate: startDate,
                maturityDate: maturityDate,
                legDefinition: floatLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FloatingLeg
            );

            var irs = new InterestRateSwap(
                startDate: startDate,
                maturityDate: maturityDate,
                legs: new List<InstrumentLeg>
                {
                    floatLeg,
                    fixedLeg
                },
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwap
            );
            return irs;
        }

        internal static InterestRateSwaption CreateExampleInterestRateSwaption(
            string deliveryMethod="Cash",
            bool payFixed=true,
            string currency="USD"
        )
        {
            // CREATE an Interest Rate Swap (IRS)
            var startDate = new DateTimeOffset(2020, 2, 7, 0, 0, 0, TimeSpan.Zero);
            var maturityDate = new DateTimeOffset(2030, 2, 7, 0, 0, 0, TimeSpan.Zero);

            // CREATE the flow conventions, index convention for swap
            var idxConvention = CreateExampleIndexConventions("UsdLibor6m");
            var flowConventions = CreateExampleFlowConventions();

            // CREATE the fixed and floating leg
            var fixedLeg = CreateExampleFixedLeg(startDate, maturityDate, flowConventions);
            var floatLeg = CreateExampleFloatLeg(startDate, maturityDate, flowConventions, idxConvention);

            var swap = new InterestRateSwap(
                startDate: startDate,
                maturityDate: maturityDate,
                legs: new List<InstrumentLeg>
                {
                    floatLeg,
                    fixedLeg
                },
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwap
            );

            // CREATE swaption to upsert to LUSID
            var swaption = new InterestRateSwaption(
                startDate: new DateTimeOffset(2020, 1, 15, 0, 0, 0, TimeSpan.Zero),
                payOrReceiveFixed: payFixed ? "Pay" : "Receive",
                deliveryMethod: deliveryMethod,
                swap: swap,
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwaption);

            return swaption;
        }

        private static CdsFlowConventions CreateExampleCdsFlowConventions()
            => new CdsFlowConventions(
                currency: "USD",
                paymentFrequency: "3M",
                rollConvention: "F",
                dayCountConvention: "Act365",
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                rollFrequency: "6M",
                settleDays: 0,
                resetDays: 0);

        internal static LusidInstrument CreateExampleCreditDefaultSwap()
            => new CreditDefaultSwap(
                ticker: "XYZCorp",
                startDate: new DateTimeOffset(2020, 6, 20, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2025, 6, 20, 0, 0, 0, TimeSpan.Zero),
                flowConventions: CreateExampleCdsFlowConventions(),
                couponRate: 0.05m,
                null,
                protectionDetailSpecification: new CdsProtectionDetailSpecification(
                    "SNR",
                    "MM",
                    true,
                    true
                ),
                instrumentType: LusidInstrument.InstrumentTypeEnum.CreditDefaultSwap
            );

        internal static LusidInstrument CreateExampleTermDeposit(DateTimeOffset startDate)
            => new TermDeposit(
                startDate: startDate,
                maturityDate: startDate.AddYears(1),
                contractSize: 1_000_000m,
                flowConvention: CreateExampleFlowConventions(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.TermDeposit
            );

        internal static ExoticInstrument CreateExampleExotic()
            => new ExoticInstrument(
                instrumentFormat: new InstrumentDefinitionFormat("source", "someVendor", "1.1"),
                content: "{\"data\":\"exoticInstrument\"}",
                instrumentType: LusidInstrument.InstrumentTypeEnum.ExoticInstrument);

        internal static Future CreateExampleFuture()
        {
            // CREATE an future (that can then be upserted into LUSID)
            var contractDetails = new FuturesContractDetails(
                domCcy: "USD",
                contractCode: "CL",
                contractMonth: "F",
                contractSize: 42000,
                convention: "Actual365",
                country: "US",
                description: "Crude Oil Nymex future Jan21",
                exchangeCode: "NYM",
                exchangeName: "NYM",
                tickerStep: 0.01m,
                unitValue: 4.2m
            );

            var futureDefinition = new Future(
                startDate: new DateTimeOffset(2020, 09, 11, 0, 0, 0, TimeSpan.Zero),
                maturityDate: new DateTimeOffset(2020, 12, 31, 0, 0, 0, TimeSpan.Zero),
                identifiers: new Dictionary<string, string>(),
                contractDetails: contractDetails,
                contracts: 1,
                refSpotPrice: 100,
                underlying: new ExoticInstrument(
                    new InstrumentDefinitionFormat("custom", "custom", "0.0.0"),
                    content: "{}",
                    LusidInstrument.InstrumentTypeEnum.ExoticInstrument),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Future
            );

            return futureDefinition;
        }

        internal static InterestRateSwaption CreateExampleInterestRateSwaptionWithNamedConventions()
        {
            var underlyingSwap = CreateSwapByNamedConventions();
            var swaption = new InterestRateSwaption(
                startDate: new DateTimeOffset(2020, 1, 15, 0, 0, 0, TimeSpan.Zero),
                payOrReceiveFixed: "Pay",
                deliveryMethod: "Cash",
                swap: underlyingSwap,
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwaption);

            return swaption;
        }

        private static FloatingLeg CreateExampleFloatLeg(
            DateTimeOffset startDate,
            DateTimeOffset maturityDate,
            FlowConventions flowConventions,
            IndexConvention indexConvention)
        {
            var floatLegDef = new LegDefinition(
                rateOrSpread: 0.002m, // float leg spread over curve rate, often zero
                stubType: "Front",
                payReceive: "Receive",
                notionalExchangeType: "None",
                conventions: flowConventions,
                indexConvention: indexConvention);

            return new FloatingLeg(
                startDate: startDate,
                maturityDate: maturityDate,
                notional: 100m,
                legDefinition: floatLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FloatingLeg
            );
        }

        private static FixedLeg CreateExampleFixedLeg(
            DateTimeOffset startDate,
            DateTimeOffset maturityDate,
            FlowConventions flowConventions)
        {
            var fixedLegDef = new LegDefinition(
                rateOrSpread: 0.05m, // fixed leg rate (swap rate)
                stubType: "Front",
                payReceive: "Pay",
                notionalExchangeType: "None",
                conventions: flowConventions
            );

            return new FixedLeg(
                startDate: startDate,
                maturityDate: maturityDate,
                notional: 100m,
                legDefinition: fixedLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FixedLeg
            );
        }

        internal static EquitySwap CreateExampleEquitySwap(bool multiCoupon = false)
        {
            // CREATE an EquitySwap (that can then be upserted into LUSID)
            var startDate = TestEffectiveAt;
            var maturity =  multiCoupon ? startDate.AddYears(5) : startDate.AddMonths(6); // coupons every 6M
            var flowConventions = CreateExampleFlowConventions();
            return new EquitySwap(
                startDate: startDate,
                maturityDate: maturity,
                code: "codeOfUnderlying",
                equityFlowConventions: flowConventions,
                fundingLeg: CreateExampleFloatLeg(startDate, maturity, flowConventions, CreateExampleIndexConventions("GbpLibor6m")),
                initialPrice: 100m,
                includeDividends: false,
                notionalReset: false,
                quantity: 10m,
                underlyingIdentifier: "Figi",
                instrumentType: LusidInstrument.InstrumentTypeEnum.EquitySwap
            );
        }
    }
}
