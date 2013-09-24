using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SmartQuant.Data;
using QuantBox.CSharp2XSpeed;

namespace QuantBox.Helper.XSpeed
{
    public class XSpeedTrade : Trade
    {
        public XSpeedTrade():base()
        {
        }

        public XSpeedTrade(Trade trade):base(trade)
        {
        }

        public XSpeedTrade(DateTime datetime, double price, int size)
            : base(datetime, price, size)
        {
        }

        public DFITCDepthMarketDataField DepthMarketData;
    }
}
