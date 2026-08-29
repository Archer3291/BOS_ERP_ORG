using Microsoft.AspNetCore.Mvc.Filters;

namespace BOS_ERP.Filters
{
    public class ApiKeyAuthorizeAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<ApiKeyAuthorizationFilter>();
        }
    }
}