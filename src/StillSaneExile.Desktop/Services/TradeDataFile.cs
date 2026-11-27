using System.IO;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Services;

/// <summary>Reads <c>data/trade/trade-data.json</c>, which ships with the app, once per run. Nothing is
/// downloaded: SSE never calls the trade site. A missing or broken file gives empty data, so links still
/// open with the item's base and rarity and every ticked stat is listed to set by hand.</summary>
public static class TradeDataFile
{
    private static readonly Lock Gate = new();
    private static (string Path, TradeData Data)? _loaded;

    public static TradeData Load(AppPaths paths)
    {
        lock (Gate)
        {
            if (_loaded is { } l && l.Path == paths.TradeData)
            {
                return l.Data;
            }

            TradeData data;
            try
            {
                data = TradeData.Parse(File.ReadAllText(paths.TradeData));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                data = TradeData.Empty;
            }

            _loaded = (paths.TradeData, data);
            return data;
        }
    }
}
