using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Nop.Plugin.Payments.Fondy
{
    /// <summary>
    /// Fondy payment method
    /// </summary>
    public class FondyPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly ICurrencyService _currencyService;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly CurrencySettings _currencySettings;
        private readonly FondyPaymentSettings _fondyPaymentSettings;

        private const string FONDY_URL = "https://api.fondy.eu/api/checkout/redirect/";
        private const string FONDY_RESULTS_URL = "https://api.fondy.eu/api/status/order_id";

        #endregion

        #region Ctor

        public FondyPaymentProcessor(ICurrencyService currencyService,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            IWebHelper webHelper,
            CurrencySettings currencySettings,
            FondyPaymentSettings fondyPaymentSettings)
        {
            this._currencyService = currencyService;
            this._localizationService = localizationService;
            this._paymentService = paymentService;
            this._settingService = settingService;
            this._webHelper = webHelper;
            this._currencySettings = currencySettings;
            this._fondyPaymentSettings = fondyPaymentSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var orderGuid = postProcessPaymentRequest.Order.OrderGuid;
            var orderTotal = postProcessPaymentRequest.Order.OrderTotal;
            var amount = Math.Round(orderTotal * 100).ToString();
            var orderId = orderGuid.ToString();
            var mid = _fondyPaymentSettings.MerchantId;
            var secret = _fondyPaymentSettings.SecretKey;
            if (_fondyPaymentSettings.TestingMode == true)
            {
                mid = "1396424";
            }
            //create and send post data
            var post = new RemotePost
            {
                FormName = "FondyPay",
                Url = FONDY_URL
            };
            post.Add("merchant_id", mid);
            post.Add("order_id", orderId);
            post.Add("currency", _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode);
            post.Add("amount", amount);
            post.Add("order_desc", _fondyPaymentSettings.DescriptionTemplate.Replace("$orderId", postProcessPaymentRequest.Order.Id.ToString()));
            var siteUrl = _webHelper.GetStoreLocation();
            var response_url = $"{siteUrl}Plugins/Fondy/Success";
            var server_callback_url = $"{siteUrl}Plugins/Fondy/ConfirmPay";

            post.Add("response_url", response_url);
            post.Add("server_callback_url", server_callback_url);
            
            //code to identify the sender and check integrity of files
            post.Add("signature", GetSignature(post.Params));

            post.Post();
        }

        /// <summary>
        /// Get the status of the order in the system Fondy 
        /// </summary>
        /// <param name="orderId">Order ID</param>
        /// <returns>An array of three elements.
        /// </returns>
        public string[] GetPaymentStatus(string orderId)
        {
            //create and send post data
            var postData = new NameValueCollection
            {
                { "merchant_id", _fondyPaymentSettings.MerchantId },
                { "order_id", orderId },
            };

            postData.Add("signature", GetSignature(postData));

            byte[] data;
            using (var client = new WebClient())
            {
                data = client.UploadValues(FONDY_RESULTS_URL, postData);
            }

            using (var ms = new MemoryStream(data))
            {
                using (var sr = new StreamReader(ms))
                {
                    var rez = sr.ReadToEnd();           
                    try
                    {
                        var doc = QueryHelpers.ParseQuery(rez);

                        var status = doc["order_status"].ToString();
                        var amount = doc["amount"].ToString();

                        return new[] { status, amount };
                    }
                    catch (NullReferenceException)
                    {
                        return new string[3];
                    }
                }
            }
        }

        /// <summary>
        /// Get the signarure
        /// </summary>
        public string GetSignature(NameValueCollection postData)
        {
            if (ContainsKey(postData, "response_signature_string"))
            {
                postData.Remove("response_signature_string");
            }
            if (ContainsKey(postData, "signature"))
            {
                postData.Remove("signature");
            }
           
            string signature = string.Join("|", postData.AllKeys.OrderBy(s => s).Select(key => postData[key]));
            string secret = _fondyPaymentSettings.SecretKey;
            if (_fondyPaymentSettings.TestingMode == true)
            {
                secret = "test";
            }          
            signature = secret + "|" + signature;

            return GetSha1(signature).ToLower();
        }

        private static bool ContainsKey(NameValueCollection collection, string key)
        {
            if (collection.Get(key) == null)
            {
                return collection.AllKeys.Contains(key);
            }

            return true;
        }

        public string GetScriptName(string scriptUrl)
        {
            return scriptUrl.Split('/').Last().Split('?').First();
        }

        private static string GetSha1(string value)
        {
            var data = Encoding.ASCII.GetBytes(value);
            var hashData = new SHA1Managed().ComputeHash(data);
            var hash = string.Empty;
            foreach (var b in hashData)
            {
                hash += b.ToString("X2");
            }
            return hash.ToLower();
        }

        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            return false;
        }

        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            var result = _paymentService.CalculateAdditionalFee(cart,
                _fondyPaymentSettings.AdditionalFee, _fondyPaymentSettings.AdditionalFeePercentage);
            return result;
        }

        public bool CanRePostProcessPayment(Order order)
        {
            return !((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5);
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentFondy/Configure";
        }

        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        public string GetPublicViewComponentName()
        {
            return "PaymentFondy";
        }

        /// <summary>
        /// Install plugin method
        /// </summary>
        public override void Install()
        {
            //settings
            var settings = new FondyPaymentSettings();
            _settingService.SaveSetting(settings);

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.MerchantId", "Merchant ID");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.MerchantId.Hint", "Specify the Fondy Merchan ID of your store on the website Fondy.ru.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.SecretKey", "Payment key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.SecretKey.Hint", "Set the payment.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.TestingMode", "Test mode");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.TestingMode.Hint", "Check to enable test mode. Will be used test merchant");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.DescriptionTamplate", "Order description template");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.DescriptionTamplate.Hint", "Template text transmitted in the description on the website. There should not be empty. $orderId - Order number.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.RedirectionTip", "For payment you will be redirected to the FONDY checkout page.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Fondy.Fields.PaymentMethodDescription", "For payment you will be redirected to the FONDY checkout page.");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin method
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<FondyPaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.MerchantId");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.MerchantId.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.SecretKey");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.SecretKey.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.TestingMode");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.TestingMode.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.DescriptionTamplate");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.DescriptionTamplate.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Fondy.Fields.PaymentMethodDescription");

            base.Uninstall();
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            get { return _localizationService.GetResource("Plugins.Payments.Fondy.Fields.PaymentMethodDescription"); }
        }

        #endregion
    }
}