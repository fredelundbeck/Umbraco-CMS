using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Web.Common.Controllers;
using Umbraco.Web.Common.Extensions;
using Umbraco.Web.Common.Routing;
using Umbraco.Web.Routing;

namespace Umbraco.Web.Common.Filters
{
    /// <summary>
    /// Used to set the <see cref="UmbracoRouteValues"/> request feature based on the <see cref="CustomRouteContentFinderDelegate"/> specified (if any)
    /// for the custom route.
    /// </summary>
    public class UmbracoVirtualPageFilterAttribute : Attribute, IAsyncActionFilter
    {
        /// <inheritdoc/>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            Endpoint endpoint = context.HttpContext.GetEndpoint();

            // Check if there is any delegate in the metadata of the route, this
            // will occur when using the ForUmbraco method during routing.
            CustomRouteContentFinderDelegate contentFinder = endpoint.Metadata.OfType<CustomRouteContentFinderDelegate>().FirstOrDefault();

            if (contentFinder != null)
            {
                await SetUmbracoRouteValues(context, contentFinder.FindContent(context));
            }
            else
            {
                // Check if the controller is IVirtualPageController and then use that to FindContent
                if (context.Controller is IVirtualPageController ctrl)
                {
                    await SetUmbracoRouteValues(context, ctrl.FindContent(context));
                }
            }

            // if we've assigned not found, just exit
            if (!(context.Result is NotFoundResult))
            {
                await next();
            }
        }

        private async Task SetUmbracoRouteValues(ActionExecutingContext context, IPublishedContent content)
        {
            if (content != null)
            {
                IUmbracoContextAccessor umbracoContext = context.HttpContext.RequestServices.GetRequiredService<IUmbracoContextAccessor>();
                IPublishedRouter router = context.HttpContext.RequestServices.GetRequiredService<IPublishedRouter>();
                IPublishedRequestBuilder requestBuilder = await router.CreateRequestAsync(umbracoContext.UmbracoContext.CleanedUmbracoUrl);
                requestBuilder.SetPublishedContent(content);
                IPublishedRequest publishedRequest = requestBuilder.Build();

                var routeValues = new UmbracoRouteValues(
                    publishedRequest,
                    (ControllerActionDescriptor)context.ActionDescriptor);

                context.HttpContext.Features.Set(routeValues);
            }
            else
            {
                // if there is no content then it should be a not found
                context.Result = new NotFoundResult();
            }
        }
    }
}