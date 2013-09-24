using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Reflection;
using QuantBox.CSharp2XSpeed;

namespace QuantBox.Helper.XSpeed
{
    public class DataConvert
    {
        static FieldInfo tradeField;
        static FieldInfo quoteField;

        public static bool TryConvert(OpenQuant.API.Trade trade, ref DFITCDepthMarketDataField DepthMarketData)
        {
            if (tradeField == null)
            {
                tradeField = typeof(OpenQuant.API.Trade).GetField("trade", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            XSpeedTrade t = tradeField.GetValue(trade) as XSpeedTrade;
            if (null != t)
            {
                DepthMarketData = t.DepthMarketData;
                return true;
            }
            return false;
        }

        public static bool TryConvert(OpenQuant.API.Quote quote, ref DFITCDepthMarketDataField DepthMarketData)
        {
            if (quoteField == null)
            {
                quoteField = typeof(OpenQuant.API.Quote).GetField("quote", BindingFlags.NonPublic | BindingFlags.Instance);
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
