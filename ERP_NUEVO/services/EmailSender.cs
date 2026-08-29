// Services/EmailSender.cs (o donde prefieras ubicarlo — ya no va en Controllers)
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace BOS_ERP.Services
{
    public class EmailSender
    {
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;

        public EmailSender(
            IRazorViewEngine viewEngine,
            ITempDataProvider tempDataProvider,
            IServiceProvider serviceProvider)
        {
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _serviceProvider = serviceProvider;
        }

        public async Task<string> RenderViewToStringAsync(string viewPath, object model)
        {
            if (string.IsNullOrEmpty(viewPath))
                throw new ArgumentNullException(nameof(viewPath));

            var httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: false);

            if (viewResult.View == null)
                throw new FileNotFoundException($"No se encontró la vista '{viewPath}'.");

            using var sw = new StringWriter();

            var viewDataDictionary = new ViewDataDictionary(
                metadataProvider: new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                modelState: new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
            {
                Model = model
            };

            var tempData = new TempDataDictionary(httpContext, _tempDataProvider);

            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewDataDictionary,
                tempData,
                sw,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);

            return sw.ToString();
        }
    }
}