using Lusid.Sdk.Api;
using Lusid.Sdk.Utilities;

namespace Lusid.Sdk.Examples.Utilities
{
    public class TutorialBase
    {
        internal readonly ILusidApiFactory ApiFactory;
        internal readonly ITransactionPortfoliosApi TransactionPortfoliosApi;
        internal readonly IInstrumentsApi InstrumentsApi;
        internal readonly IQuotesApi QuotesApi;
        internal readonly IConfigurationRecipeApi RecipeApi;
        internal readonly IPortfoliosApi PortfoliosApi;
        internal readonly ICutLabelDefinitionsApi CutLabelDefinitionsApi;
        internal readonly IOrdersApi OrdersApi;
        internal readonly ICorporateActionSourcesApi CorporateActionSourcesApi;
        internal readonly IStructuredResultDataApi StructuredResultDataApi;
        internal readonly IPropertyDefinitionsApi PropertyDefinitionsApi;

        protected TutorialBase()
        {
            // Initialize all the API end points
            ApiFactory = TestLusidApiFactoryBuilder.CreateApiFactory("secrets.json");
            PortfoliosApi = ApiFactory.Api<IPortfoliosApi>();
            TransactionPortfoliosApi = ApiFactory.Api<ITransactionPortfoliosApi>();
            InstrumentsApi = ApiFactory.Api<IInstrumentsApi>();
            QuotesApi = ApiFactory.Api<IQuotesApi>();
            ApiFactory.Api<IComplexMarketDataApi>();
            RecipeApi = ApiFactory.Api<IConfigurationRecipeApi>();
            ApiFactory.Api<IAggregationApi>();
            CutLabelDefinitionsApi = ApiFactory.Api<CutLabelDefinitionsApi>();
            OrdersApi = ApiFactory.Api<IOrdersApi>();
            CorporateActionSourcesApi = ApiFactory.Api<ICorporateActionSourcesApi>();
            ApiFactory.Api<IConventionsApi>();
            StructuredResultDataApi = ApiFactory.Api<IStructuredResultDataApi>();
            PropertyDefinitionsApi = ApiFactory.Api<IPropertyDefinitionsApi>();
        }
    }
}
