using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.Fondy.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        /// <summary>
        /// The Fondy Merchan ID
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.MerchantId")]
        public string MerchantId { get; set; }
        public bool MerchantIdOverrideForStore { get; set; }
        /// <summary>
        /// Secret key
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.SecretKey")]
        public string SecretKey { get; set; }
        public bool SecretKeyOverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.TestingMode")]
        public bool TestingMode { get; set; }
        public bool TestingModeOverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.DescriptionTamplate")]
        public string DescriptionTemplate { get; set; }
        public bool DescriptionTemplateOverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentageOverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Fondy.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFeeOverrideForStore { get; set; }
    }
}