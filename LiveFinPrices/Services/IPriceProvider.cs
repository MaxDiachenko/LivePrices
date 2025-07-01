namespace LiveFinPrices.Services
{
    public interface IPriceProvider
    {
        event Action<Instrument, float> OnPriceUpdate;
        float GetCurrentPrice(string symbol);
        IEnumerable<string> GetAvailableInstruments();
        float GetCurrentPrice(Instrument instrument);
    }
}