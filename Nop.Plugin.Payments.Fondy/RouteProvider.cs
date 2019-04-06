using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Fondy
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            //confirm pay
            routeBuilder.MapRoute("Plugin.Payments.Fondy.ConfirmPay",
                 "Plugins/Fondy/ConfirmPay",
                 new { controller = "PaymentFondy", action = "ConfirmPay" });
            //success
            routeBuilder.MapRoute("Plugin.Payments.Fondy.Success",
                 "Plugins/Fondy/Success",
                 new { controller = "PaymentFondy", action = "Success" });
        }

        public int Priority
        {
            get
            {
                return 0;
            }
        }
    }
}
