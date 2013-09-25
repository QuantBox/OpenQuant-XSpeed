using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Reflection;
using QuantBox.CSharp2XSpeed;

#if OQ
using OpenQuant.API;
#elif QD
using SmartQuant.Data;
#endif

namespace QuantBox.Helper.XSpeed
{
    public class DataConvert
    {
        static FieldInfo tradeField;
        static FieldInfo quoteField;

        public static bool TryConvert(Trade trade, ref DFITCDepthMarketDataField DepthMarketData)
        {
            if (tradeField == null)
            {
                tradeField = typeof(Trade).GetField("trade", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            XSpeedTrade t = tradeField.GetValue(trade) as XSpeedTrade;
            if (null != t)
            {
                DepthMarketData = t.DepthMarketData;
                return true;
            }
            return false;
        }

        public static bool TryConvert(Quote quote, ref DFITCDepthMarketDataField DepthMarketData)
        {
            if (quoteField == null)
            {
                quoteField = typeof(Quote).GetField("quote", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            XSpeedQuote q = quoteField.GetValue(quote) as XSpeedQuote;
            if (null != q)
            {
                DepthMarketData = q.DepthMarketData;
                return true;
            }
            return false;
        }
    }
}
