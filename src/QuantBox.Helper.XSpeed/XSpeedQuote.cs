using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SmartQuant.Data;
using QuantBox.CSharp2XSpeed;

namespace QuantBox.Helper.XSpeed
{
    public class XSpeedQuote : Quote
    {
        public XSpeedQuote()
            : base()
        {
        }

        public XSpeedQuote(Quote quote)
            : base(quote)
        {
        }

        public XSpeedQuote(DateTime datetime, double bid, int bidSize, double ask, int askSize)
            : base(datetime, bid, bidSize, ask, askSize)
        {
        }

        public DFITCDepthMarketDataField DepthMarketData;
    }
}
