using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Lusid.Sdk.Model;

namespace Lusid.Sdk.Tests.Utilities
{
    public static class InstrumentExamples
    {

        public enum InterestRateSwapType
        {
            /// <summary>
            /// Vanilla Swap
            /// </summary>
            [EnumMember]
            Vanilla,

            [EnumMember]
            SOFR,

            [EnumMember]
            ESTR,

            [EnumMember]
            SONIA,

            [EnumMember]
            CDOR,

            [EnumMember]
            TONA,

            [EnumMember]
            SARON,

            [EnumMember]
            CrossCurrency,

            [EnumMember]
            Basis,

            [EnumMember]
            Amortising
        }


        public static LusidInstrument GetExampleInstrument(string instrumentName)
        {
            return instrumentName switch
            {
                nameof(Bond) => CreateExampleBond(),
                nameof(FxForward) => CreateExampleFxForward(),
                nameof(FxOption) => CreateExampleFxOption(),
                nameof(InterestRateSwap) => CreateExampleInterestRateSwap(InterestRateSwapType.Vanilla),
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
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddMonths(9),
                fixingDate: TestDataUtilities.StartDate.AddMonths(9).AddDays(-1),
                isNdf: isNdf,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FxForward
            );

        internal static LusidInstrument CreateExampleForwardRateAgreement()
        {
            return new ForwardRateAgreement(
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(1),
                fixingDate: TestDataUtilities.StartDate.AddMonths(11),
                fraRate: 0.05m,
                notional: 1_000_000m,
                domCcy: "GBP",
                instrumentType: LusidInstrument.InstrumentTypeEnum.ForwardRateAgreement);
        }

        internal static LusidInstrument CreateExampleFxOption(bool isDeliveryNotCash = true)
            => new FxOption(
                strike: 130,
                domCcy: "USD",
                fgnCcy: "JPY",
                startDate: TestDataUtilities.StartDate,
                optionMaturityDate: TestDataUtilities.StartDate.AddYears(1),
                optionSettlementDate: TestDataUtilities.StartDate.AddYears(1).AddDays(2),
                isCallNotPut: true,
                isDeliveryNotCash: isDeliveryNotCash,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FxOption
            );

        internal static LusidInstrument CreateExampleEquityOption(bool isCashSettled = false)
            => new EquityOption(
                startDate: TestDataUtilities.StartDate,
                optionMaturityDate: TestDataUtilities.StartDate.AddYears(1),
                optionSettlementDate: TestDataUtilities.StartDate.AddYears(1).AddDays(2),
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

        private static FlowConventions CreateExampleFlowConventions(string currency, string paymentFrequency, string rollConvention, string dayCount, int settleDays, int resetDays)
            => new FlowConventions(
                currency: currency,
                paymentFrequency: paymentFrequency,
                rollConvention: rollConvention,
                dayCountConvention: dayCount,
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                settleDays: settleDays,
                resetDays: resetDays);

        internal static LusidInstrument CreateExampleBond()
            => new Bond(
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(6),
                domCcy: "USD",
                principal: 100m,
                couponRate: 0.05m,
                flowConventions: CreateExampleFlowConventions("USD", "6M", "MF", "Act365", 2, 2),
                identifiers: new Dictionary<string, string>(),
                instrumentType: LusidInstrument.InstrumentTypeEnum.Bond
            );

        internal static LusidInstrument CreateExampleCfd()
            => new ContractForDifference(
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(6),
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
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(6),
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


        private static IndexConvention CreateExampleIndexConventions(string currency, string indexName, string tenor, string dayCount, string fixingRef)
        {
            string code = currency + indexName + tenor; // This is not needed if running an inline valuation - can be set to null
            return new IndexConvention(
                code: code,
                publicationDayLag: 0,
                currency: currency,
                paymentTenor: tenor,
                dayCountConvention: dayCount,
                fixingReference: fixingRef,
                indexName: indexName
            );
        }

        /// <summary>
        /// One can define an IRS (interest rate swap) by explicitly defining all parameters and in particular the flow and index convention variables.
        /// LUSID allows us to book flow IRS without the need to explicitly write out the flow and index convention by providing a semantic name for commonly understood ones.
        /// For example, new FlowConventionName(currency: "GBP", tenor: "3M") and new FlowConventionName(currency: "GBP", tenor: "3M", indexName:"LIBOR")
        /// </summary>
        internal static InterestRateSwap CreateSwapByNamedConventions(DateTimeOffset? startDateCustom = null)
        {
            DateTimeOffset startDate =  startDateCustom ?? TestDataUtilities.StartDate;
            var maturityDate = startDate.AddYears(2);
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

        internal static InterestRateSwap CreateExampleInterestRateSwap(InterestRateSwapType type)
        {
            InterestRateSwap swap = null;
            switch (type)
            {
                case InterestRateSwapType.Vanilla:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "USD",
                        indexName: "LIBOR",
                        dayCount: "Act365",
                        fixingRef: TestDataUtilities.VanillaSwapFixingReference,
                        rollConvention: "MF",
                        settleDays: 2,
                        resetDays: 2,
                        paymentFreq: "6M"
                    );
                    break;
                case InterestRateSwapType.SOFR:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "USD",
                        indexName: "SOFR",
                        dayCount: "Act360",
                        fixingRef: TestDataUtilities.RFRFixingReference,
                        rollConvention: "MF",
                        settleDays: 2,
                        resetDays: 0,
                        paymentFreq: "1Y",
                        indexTenor: "1D",
                        rate: 0.02m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InArrears",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "SpreadExclusive",
                        calcShift: "Lookback",
                        shift: 5
                    );
                    break;
                case InterestRateSwapType.ESTR:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "EUR",
                        indexName: "ESTR",
                        dayCount: "Act360",
                        fixingRef: TestDataUtilities.RFRFixingReference,
                        rollConvention: "MF",
                        settleDays: 1,
                        resetDays: 0,
                        paymentFreq: "1Y",
                        indexTenor: "1D",
                        rate: 0.01m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InArrears",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "Straight",
                        calcShift: "Lockout",
                        shift: 1
                    );
                    break;
                case InterestRateSwapType.SONIA:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "GBP",
                        indexName: "SONIA",
                        dayCount: "Act365",
                        fixingRef: TestDataUtilities.RFRFixingReference,
                        rollConvention: "MF",
                        settleDays: 0,
                        resetDays: 0,
                        paymentFreq: "1Y",
                        indexTenor: "1D",
                        rate: 0.01m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InArrears",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "Flat",
                        calcShift: "NoShift",
                        shift: 0
                    );
                    break;
                case InterestRateSwapType.TONA:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "JPY",
                        indexName: "TONA",
                        dayCount: "Act365",
                        fixingRef: TestDataUtilities.RFRFixingReference,
                        rollConvention: "MF",
                        settleDays: 2,
                        resetDays: 0,
                        paymentFreq: "1Y",
                        indexTenor: "1D",
                        rate: 0.01m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InArrears",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "Flat",
                        calcShift: "NoShift",
                        shift: 0
                    );
                    break;
                case InterestRateSwapType.SARON:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "CHF",
                        indexName: "SARON",
                        dayCount: "Act360",
                        fixingRef: TestDataUtilities.RFRFixingReference,
                        rollConvention: "MF",
                        settleDays: 2,
                        resetDays: 0,
                        paymentFreq: "1Y",
                        indexTenor: "1D",
                        rate: 0.01m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InArrears",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "None",
                        calcShift: "NoShift",
                        shift: 0
                    );
                    break;

                case InterestRateSwapType.CDOR:
                    swap = CreateInterestRateSwapForIndex(
                        currency: "CAD",
                        indexName: "CDOR",
                        dayCount: "Act365F",
                        fixingRef: TestDataUtilities.CDORFixingReference,
                        rollConvention: "MF",
                        settleDays: 0,
                        resetDays: 0,
                        paymentFreq: "6M",
                        indexTenor: "3M",
                        rate: 0.01m,
                        notional: 100000000m,
                        spread: 0.002m,
                        resetConvention: "InAdvance",
                        compoundingMethod: "Compounded",
                        spreadCompounding: "None",
                        calcShift: "NoShift",
                        shift: 0,
                        resetFrequency: "3M"
                    );
                    break;

                case InterestRateSwapType.CrossCurrency:
                    swap = CreateExampleCrossCurrencyBasisSwap();
                    break;

                case InterestRateSwapType.Basis:
                    swap = CreateExampleBasisSwap();
                    break;

                case InterestRateSwapType.Amortising:
                    swap = CreateExampleAmortisingSwap();
                    break;

                default:
                    throw new InvalidOperationException("No example IRS defined for type {type}");
            }

            return swap;
        }

        private static InterestRateSwap CreateInterestRateSwapForIndex(
            string currency,
            string indexName,
            string dayCount,
            string fixingRef,
            string rollConvention,
            int settleDays,
            int resetDays,
            string paymentFreq,
            string indexTenor = "6M",
            decimal rate = 0.05m,
            decimal notional = 100m,
            decimal spread = 0.002m,
            string resetConvention = "InAdvance",
            string compoundingMethod = "",
            string spreadCompounding = "",
            string calcShift = "",
            int shift = 0,
            string resetFrequency = "1D"
            )
        {
            var startDate = TestDataUtilities.StartDate;
            var maturityDate = TestDataUtilities.StartDate.AddYears(6);

            Compounding compounding = null;
            if (compoundingMethod != "")
            {
                 compounding = new Compounding(
                    calculationShiftMethod: calcShift,
                    compoundingMethod: compoundingMethod,
                    resetFrequency: resetFrequency,
                    spreadCompoundingMethod: spreadCompounding,
                    shift: shift);
            }

            // CREATE the fixed and floating leg
            var idxConvention = CreateExampleIndexConventions(currency, indexName, indexTenor, dayCount, fixingRef);
            var flowConventions = CreateExampleFlowConventions(currency, paymentFreq, rollConvention, dayCount, settleDays, resetDays);

            var fixedLeg = CreateExampleFixedLeg(startDate, maturityDate, flowConventions, rate, "Both", "Pay", notional);
            var floatLeg = CreateExampleFloatLeg(startDate, maturityDate, flowConventions, idxConvention, notional, "Both", "Receive", spread, resetConvention, compounding);

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

        internal static InterestRateSwaption CreateExampleInterestRateSwaption(
            string deliveryMethod="Cash",
            bool payFixed=true,
            string currency="USD"
        )
        {
            // CREATE an Interest Rate Swap (IRS)
            var startDate = TestDataUtilities.StartDate.AddYears(1);
            var maturityDate = startDate.AddYears(2);

            // CREATE the flow conventions, index convention for swap
            var idxConvention = CreateExampleIndexConventions("USD", "LIBOR", "6M", "Act365", TestDataUtilities.VanillaSwapFixingReference);
            var flowConventions = CreateExampleFlowConventions("USD", "6M", "MF", "Act365", 2, 2);

            // CREATE the fixed and floating leg
            var fixedLeg = CreateExampleFixedLeg(startDate, maturityDate, flowConventions, 0.05m, "Front", "Pay", 100m);
            var floatLeg = CreateExampleFloatLeg(startDate, maturityDate, flowConventions, idxConvention, 100m, "Front", "Receive", 0.002m, "InAdvance",null);

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
                startDate: TestDataUtilities.StartDate,
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
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
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
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(1),
                contractSize: 1_000_000m,
                flowConvention: CreateExampleFlowConventions("USD", "6M", "MF", "Act365", 2, 2),
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
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(6),
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
            var underlyingSwap = CreateSwapByNamedConventions(TestDataUtilities.StartDate.AddYears(1));
            var swaption = new InterestRateSwaption(
                startDate: TestDataUtilities.StartDate,
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
            IndexConvention indexConvention,
            decimal notional,
            string stubType,
            string payReceive,
            decimal spread,
            string resetConvention,
            Compounding compounding)
        {
            var floatLegDef = new LegDefinition(
                rateOrSpread: spread, // float leg spread over curve rate, often zero
                stubType: stubType,
                payReceive: payReceive,
                notionalExchangeType: "None",
                conventions: flowConventions,
                indexConvention: indexConvention,
                compounding: compounding,
                resetConvention: resetConvention);

            return new FloatingLeg(
                startDate: startDate,
                maturityDate: maturityDate,
                notional: notional,
                legDefinition: floatLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FloatingLeg
            );
        }

        private static FixedLeg CreateExampleFixedLeg(
            DateTimeOffset startDate,
            DateTimeOffset maturityDate,
            FlowConventions flowConventions,
            decimal rateOrSpread,
            string stubType,
            string payReceive,
            decimal notional)
        {
            var fixedLegDef = new LegDefinition(
                rateOrSpread: rateOrSpread, // fixed leg rate (swap rate)
                stubType: stubType,
                payReceive: payReceive,
                notionalExchangeType: "None",
                conventions: flowConventions
            );

            return new FixedLeg(
                startDate: startDate,
                maturityDate: maturityDate,
                notional: notional,
                legDefinition: fixedLegDef,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FixedLeg
            );
        }

        internal static EquitySwap CreateExampleEquitySwap(bool multiCoupon = false)
        {
            // CREATE an EquitySwap (that can then be upserted into LUSID)
            var startDate = TestDataUtilities.StartDate;
            var maturity =  multiCoupon ? startDate.AddYears(5) : startDate.AddMonths(6); // coupons every 6M
            var flowConventions = CreateExampleFlowConventions(
                currency: "USD",
                paymentFrequency: "6M",
                rollConvention: "MF",
                dayCount: "Act365",
                settleDays: 2,
                resetDays: 2);
            return new EquitySwap(
                startDate: startDate,
                maturityDate: maturity,
                code: "codeOfUnderlying",
                equityFlowConventions: flowConventions,
                fundingLeg: CreateExampleFloatLeg(
                    startDate,
                    maturity,
                    flowConventions,
                    CreateExampleIndexConventions(
                        currency: "USD",
                        indexName:"LIBOR",
                        tenor:"6M",
                        dayCount: "Act365",
                        fixingRef: TestDataUtilities.EquitySwapFixingRef),
                    notional: 100m,
                    stubType: "Front",
                    payReceive: "Receive",
                    spread: 0.002m,
                    resetConvention: "InAdvance",
                    compounding: null),
                initialPrice: 100m,
                includeDividends: false,
                notionalReset: false,
                quantity: 10m,
                underlyingIdentifier: "Figi",
                instrumentType: LusidInstrument.InstrumentTypeEnum.EquitySwap
            );
        }

        /// <summary>
        /// Single currency basis (floating-floating) swap
        /// </summary>
        private static InterestRateSwap CreateExampleBasisSwap()
        {
            // construct a 3M LIBOR leg (with a spread)
            var flow3M = CreateExampleFlowConventions(currency: "USD", paymentFrequency: "3M", rollConvention: "MF", dayCount: "Act360", settleDays: 2, resetDays: 2);
            var index3M = CreateExampleIndexConventions(currency: "USD", indexName: "LIBOR", tenor: "3M", dayCount: "Act365", fixingRef: TestDataUtilities.VanillaSwapFixingReference);
            var leg3M = CreateExampleFloatLeg(startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                flowConventions: flow3M,
                indexConvention: index3M,
                notional: 100m,
                stubType: "Both",
                payReceive: "Pay",
                spread: 0.002m,
                resetConvention: "InAdvance",
                compounding: null);

            // construct a 6M LIBOR leg
            var flow6M = CreateExampleFlowConventions(currency: "USD", paymentFrequency: "6M", rollConvention: "MF", dayCount: "Act360", settleDays: 2, resetDays: 2);
            var index6M = CreateExampleIndexConventions(currency: "USD", indexName: "LIBOR", tenor: "6M", dayCount: "Act365", fixingRef: TestDataUtilities.AlternateSwapFixingReference);
            var leg6M = CreateExampleFloatLeg(startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                flowConventions: flow6M,
                indexConvention: index6M,
                notional: 100m,
                stubType: "Both",
                payReceive: "Pay",
                spread: 0m,
                resetConvention: "InAdvance",
                compounding: null);

            return new InterestRateSwap(
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                legs: new List<InstrumentLeg>
                {
                    leg3M,
                    leg6M
                },
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwap
            );
        }

        /// <summary>
        /// An example cross-currency basis (floating-floating) swap.
        /// Note that it is common for such swaps to have resetting notionals to emphasise basis risk over fx risk, see https://www.clarusft.com/mechanics-of-cross-currency-swaps/
        /// However, we do not currently support this.
        /// </summary>
        private static InterestRateSwap CreateExampleCrossCurrencyBasisSwap()
        {
            // construct a leg that pays USD 3M LIBOR interest -- thus in this swap we are borrowing USD
            var flowUSD = CreateExampleFlowConventions(currency: "USD", paymentFrequency: "3M", rollConvention: "MF", dayCount: "Act360", settleDays: 2, resetDays: 2);
            var indexUSD = CreateExampleIndexConventions(currency: "USD", indexName: "LIBOR", tenor: "3M", dayCount: "Act360", fixingRef: TestDataUtilities.VanillaSwapFixingReference);
            var legDefnUSD = new LegDefinition(conventions: flowUSD, indexConvention: indexUSD, notionalExchangeType: "Both", payReceive: "Pay", stubType: "Both");

            var legUSD = new FloatingLeg(startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                legDefinition: legDefnUSD,
                notional: 130m,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FloatingLeg);

            // construct a leg that pays GBP 3M LIBOR interest -- thus in this swap we are lending GBP
            // this leg pays a spread known as the cross currency basis (typically negative due to higher demand for the dollar)
            var flowGBP = CreateExampleFlowConventions(currency: "GBP", paymentFrequency: "3M", rollConvention: "MF", dayCount: "Act365", settleDays: 2, resetDays: 2);
            var indexGBP = CreateExampleIndexConventions(currency: "GBP", indexName: "LIBOR", tenor: "3M", dayCount: "Act365", fixingRef: TestDataUtilities.AlternateSwapFixingReference);
            var legDefnGBP = new LegDefinition(conventions: flowGBP, indexConvention: indexGBP, notionalExchangeType: "Both", payReceive: "Receive", rateOrSpread: -0.001m, stubType: "Both");

            var legGBP = new FloatingLeg(startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                legDefinition: legDefnGBP,
                notional: 100m,
                instrumentType: LusidInstrument.InstrumentTypeEnum.FloatingLeg);

            return new InterestRateSwap(
                startDate: TestDataUtilities.StartDate,
                maturityDate: TestDataUtilities.StartDate.AddYears(5),
                legs: new List<InstrumentLeg>
                {
                    legUSD,
                    legGBP
                },
                instrumentType: LusidInstrument.InstrumentTypeEnum.InterestRateSwap
            );
        }

        /// <summary>
        /// An amortising swap i.e. a swap whose notional changes over time.
        /// </summary>
        private static InterestRateSwap CreateExampleAmortisingSwap()
        {
            // define an amortisation schedule; the notional starts at 100, then steps down to 50 and 30 over time.
            var steps = new List<LevelStep>
            {
                new LevelStep(TestDataUtilities.StartDate, 100m),
                new LevelStep(TestDataUtilities.StartDate.AddYears(2), 50m),
                new LevelStep(TestDataUtilities.StartDate.AddYears(4), 30m),
            };
            var stepSchedule = new StepSchedule(stepScheduleType: "Notional", levelType: "Absolute", steps: steps, scheduleType: "Step");

            // create a vanilla swap
            var swap = CreateExampleInterestRateSwap(InterestRateSwapType.Vanilla);
            var fixedLeg = swap.Legs.OfType<FixedLeg>().First();
            var floatLeg = swap.Legs.OfType<FloatingLeg>().First();

            // populate each leg with the amortisation schedule
            fixedLeg.LegDefinition.Amortisation = stepSchedule;
            floatLeg.LegDefinition.Amortisation = stepSchedule;

            return swap;
        }
    }
}
