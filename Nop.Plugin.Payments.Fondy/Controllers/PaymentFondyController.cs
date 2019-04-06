using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Fondy.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Fondy.Controllers
{
    public class PaymentFondyController : BasePaymentController
    {
        private const string ORDER_DESCRIPTION = "Pay for order #$orderId";

        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPaymentService _paymentService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;

        public PaymentFondyController(ILocalizationService localizationService,
            ILogger logger,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IPaymentService paymentService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper)
        {
            this._storeContext = storeContext;
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._logger = logger;
            this._localizationService = localizationService;
            this._webHelper = webHelper;
            this._permissionService = permissionService;
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var fondyPaymentSettings = _settingService.LoadSetting<FondyPaymentSettings>(storeScope);

            if (!fondyPaymentSettings.DescriptionTemplate.Any())
                fondyPaymentSettings.DescriptionTemplate = ORDER_DESCRIPTION;

            var model = new ConfigurationModel
            {
                MerchantId = fondyPaymentSettings.MerchantId,
                SecretKey = fondyPaymentSettings.SecretKey,
                TestingMode = fondyPaymentSettings.TestingMode,
                DescriptionTemplate = fondyPaymentSettings.DescriptionTemplate,
                AdditionalFee = fondyPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = fondyPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.MerchantIdOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.MerchantId, storeScope);
                model.SecretKeyOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.SecretKey, storeScope);
                model.TestingModeOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.TestingMode, storeScope);
                model.DescriptionTemplateOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.DescriptionTemplate, storeScope);
                model.AdditionalFeeOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentageOverrideForStore = _settingService.SettingExists(fondyPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.Fondy/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var fondyPaymentSettings = _settingService.LoadSetting<FondyPaymentSettings>(storeScope);

            //save settings
            fondyPaymentSettings.MerchantId = model.MerchantId;
            fondyPaymentSettings.SecretKey = model.SecretKey;
            fondyPaymentSettings.TestingMode = model.TestingMode;
            fondyPaymentSettings.DescriptionTemplate = model.DescriptionTemplate;
            fondyPaymentSettings.AdditionalFee = model.AdditionalFee;
            fondyPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;


            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.MerchantId, model.MerchantIdOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.SecretKey, model.SecretKeyOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.TestingMode, model.TestingModeOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.DescriptionTemplate, model.DescriptionTemplateOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.AdditionalFee, model.AdditionalFeeOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(fondyPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentageOverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        
        private ContentResult GetResponse(string textToResponse, FondyPaymentProcessor processor, bool success = false)
        {
            var status = success ? "ok" : "error";
            if (!success)
                _logger.Error($"Fondy. {textToResponse}");

            return Content(textToResponse, "text/html", Encoding.UTF8);
        }

        private string GetValue(string key, IFormCollection form)
        {
            return (form.Keys.Contains(key) ? form[key].ToString() : _webHelper.QueryString<string>(key)) ?? string.Empty;
        }

        private void UpdateOrderStatus(Order order, string status)
        {
            status = status.ToLower();
           
            switch (status)
            {
                case "declined":
                case "expired":
                    {
                        //mark order as canceled
                        if ((order.PaymentStatus == PaymentStatus.Paid || order.PaymentStatus == PaymentStatus.Authorized) &&
                            _orderProcessingService.CanCancelOrder(order))
                            _orderProcessingService.CancelOrder(order, true);
                    }
                    break;
                case "approved":
                    {
                        //mark order as paid
                        if (_orderProcessingService.CanMarkOrderAsPaid(order) && status.ToUpper() == "PAID")
                            _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    break;
            }
        }

        public ActionResult ConfirmPay(IpnModel model)
        {
            var form = model.Form;
            var processor = GetPaymentProcessor();

            const string orderIdKey = "order_id";
            const string signatureKey = "signature";
            const string orderStatus = "order_status";

            var orderId = GetValue(orderIdKey, form);
            var signature = GetValue(signatureKey, form);
            var result = GetValue(orderStatus, form);

            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
            {
                order = _orderService.GetOrderByGuid(orderGuid);
            }

            if (order == null)
                return GetResponse("Order cannot be loaded", processor);

            var sb = new StringBuilder();
            sb.AppendLine("Fondy:");
            foreach (var key in form.Keys)
            {
                sb.AppendLine(key + ": " + form[key]);
            }
           
            //order note
            order.OrderNotes.Add(new OrderNote
            {
                Note = sb.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
            _orderService.UpdateOrder(order);
           
            var postData = new NameValueCollection();
            foreach (var keyValuePair in form.Where(pair => !pair.Key.Equals(signatureKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (keyValuePair.Value == "") {
                    continue;
                }
                postData.Add(keyValuePair.Key, keyValuePair.Value);
            }
            var checkDataString = processor.GetSignature(postData);

            if (checkDataString != signature)
                return GetResponse("Invalid order data", processor);

            if (result == "declined" || result == "expired")
                return GetResponse("The payment has been canceled", processor, true);

            //mark order as paid
            if (_orderProcessingService.CanMarkOrderAsPaid(order) && result == "approved")
            {
                _orderProcessingService.MarkOrderAsPaid(order);
            }

            return GetResponse("The order has been paid", processor, true);
        }

        private FondyPaymentProcessor GetPaymentProcessor()
        {
            var processor =
                _paymentService.LoadPaymentMethodBySystemName("Payments.Fondy") as FondyPaymentProcessor;
            if (processor == null ||
                !_paymentService.IsPaymentMethodActive(processor) || !processor.PluginDescriptor.Installed)
                throw new NopException("Fondy module cannot be loaded");
            return processor;
        }

        public ActionResult Success(IpnModel model)
        {
            var form = model.Form;
            const string orderIdKey = "order_id";
            var orderId = GetValue(orderIdKey, form);
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = string.Empty });

            //update payment status if need
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                var status = GetPaymentProcessor().GetPaymentStatus(orderId);
                if (status[0].ToLower() == "approved")
                    UpdateOrderStatus(order, status[1]);
            }

            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }

        //ToDo method
        public ActionResult CancelOrder(IpnModel model)
        {
            var form = model.Form;
            const string orderIdKey = "order_id";
            var orderId = GetValue(orderIdKey, form);
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = string.Empty });

            //update payment status if need
            if (order.PaymentStatus != PaymentStatus.Voided)
            {
                var status = GetPaymentProcessor().GetPaymentStatus(orderId);
                if (status[0].ToLower() == "ok")
                    UpdateOrderStatus(order, status[1]);
            }

            return RedirectToRoute("OrderDetails", new { orderId = order.Id });
        }
    }
}