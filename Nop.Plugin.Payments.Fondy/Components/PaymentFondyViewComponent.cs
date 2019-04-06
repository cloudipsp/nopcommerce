using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Fondy.Components
{
    [ViewComponent(Name = "PaymentFondy")]
    public class PaymentFondyViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.Fondy/Views/PaymentInfo.cshtml");
        }
    }
}
