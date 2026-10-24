namespace RevoHelper.Core.Hardware;

public static class MachineSupportClassifier
{
    public static MachineSupportAssessment Classify(MachineIdentity identity)
    {
        var manufacturer = identity.Manufacturer.Trim();
        var product = identity.ProductName.Trim();
        var deviceKey = $"{manufacturer} {product}".Trim();

        if (!manufacturer.Equals("MECHREVO", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                MachineSupportStatus.Unsupported,
                deviceKey,
                "RevoHelper only supports explicitly identified Mechrevo systems.");
        }

        if (product.Equals("YAOSHI Series", StringComparison.OrdinalIgnoreCase) &&
            identity.BaseboardProduct.Equals("YAOSHI Series-X6AR55xY", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                MachineSupportStatus.Supported,
                deviceKey,
                "This exact Yaoshi baseboard is explicitly recognized.");
        }

        return new(
            MachineSupportStatus.Unverified,
            deviceKey,
            "This Mechrevo model is not yet verified for hardware writes.");
    }
}
