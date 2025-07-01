namespace LiveFinPrices
{
    public enum Instrument
    {
        BtcUsdt = 0,
        EthUsdt = 1,
    }

    public static class Settings
    {
        public const int UpdatesBufferSize=1024*4;
        public static readonly Dictionary<string, Instrument> InstrumentByName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["btcusdt"] = Instrument.BtcUsdt,
            //["ethusdt"] = Instrument.EthUsdt
        };
    }
}
