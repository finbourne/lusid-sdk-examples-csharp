using System.Collections.Generic;
using Lusid.Sdk.Model;
using Lusid.Sdk.Tests.Utilities;
using Lusid.Sdk.Utilities;
using NUnit.Framework;

namespace Lusid.Sdk.Tests.Tutorials.Instruments
{
    public class InstrumentData: TutorialBase
    {
        [Test]
        public void UpsertConventions()
        {
            string scope = "TestConventionsScope";
            string flowConventionsCode = "GBP-6M";
            string indexConventionCode = "GBP-6M-Libor";

            // CREATE the flow conventions, index convention for swap
            var flowConventions = new FlowConventions(
                scope: scope,
                code: flowConventionsCode,
                currency: "GBP",
                paymentFrequency: "6M",
                rollConvention: "ModifiedFollowing",
                dayCountConvention: "Actual365",
                paymentCalendars: new List<string>(),
                resetCalendars: new List<string>(),
                settleDays: 2,
                resetDays: 2
            );

            var indexConvention = new IndexConvention(
                scope: scope,
                code: indexConventionCode,
                publicationDayLag: 0,
                currency: "GBP",
                paymentTenor: "6M",
                dayCountConvention: "Actual365",
                fixingReference: TestDataUtilities.VanillaSwapFixingReference,
                indexName: "LIBOR"
            );

            var flowConventionsResponse =  _conventionsApi.UpsertFlowConventions(new UpsertFlowConventionsRequest(flowConventions));
            Assert.That(flowConventionsResponse, Is.Not.Null);
            Assert.That(flowConventionsResponse.Value, Is.Not.Null);

            var indexConventionsResponse = _conventionsApi.UpsertIndexConvention(new UpsertIndexConventionRequest(indexConvention));
            Assert.That(indexConventionsResponse, Is.Not.Null);
            Assert.That(indexConventionsResponse.Value, Is.Not.Null);

            var retrievedFlowConventions = _conventionsApi.GetFlowConventions(scope, flowConventionsCode);
            Assert.That(retrievedFlowConventions, Is.Not.Null);
            Assert.That(retrievedFlowConventions.Value.Scope, Is.EqualTo(flowConventions.Scope));
            Assert.That(retrievedFlowConventions.Value.Code, Is.EqualTo(flowConventions.Code));

            var retrievedIndexConvention = _conventionsApi.GetIndexConvention(scope, indexConventionCode);
            Assert.That(retrievedIndexConvention, Is.Not.Null);
            Assert.That(retrievedIndexConvention.Value.Scope, Is.EqualTo(indexConvention.Scope));
            Assert.That(retrievedIndexConvention.Value.Code, Is.EqualTo(indexConvention.Code));
        }
    }
}
