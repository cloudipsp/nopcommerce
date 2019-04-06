using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Fondy
{
    public class FondyPaymentSettings : ISettings
    {
        public string MerchantId { get; set; }
        public string SecretKey { get; set; }
        public bool TestingMode { get; set; }
        public string DescriptionTemplate { get; set; }
        public bool AdditionalFeePercentage { get; set; }
        public decimal AdditionalFee { get; set; }
    }
}
