using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using QuantBox.CSharp2XSpeed;
using QuantBox.Helper.XSpeed;
using SmartQuant;
using SmartQuant.Data;
using SmartQuant.Execution;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using SmartQuant.Providers;


namespace QuantBox.OQ.XSpeed
{
    partial class XSpeedProvider
    {
        #region 深度行情回调
        private DateTime _dateTime = DateTime.Now;
        private void OnMarketData(IntPtr pMdUserApi, ref DFITCDepthMarketDataField pMarketDataField)
        {
            try
            {
                DataRecord record;
                if (!_dictAltSymbol2Instrument.TryGetValue(pMarketDataField.InstrumentID, out record))
                {
                    mdlog.Warn("合约{0}不在订阅列表中却收到了数据", pMarketDataField.InstrumentID);
                    return;
                }

                Instrument instrument = record.Instrument;

                DFITCDepthMarketDataField DepthMarket;
                _dictDepthMarketData.TryGetValue(pMarketDataField.InstrumentID, out DepthMarket);

                //将更新字典的功能提前，因为如果一开始就OnTrade中下单，涨跌停没有更新
                _dictDepthMarketData[pMarketDataField.InstrumentID] = pMarketDataField;

                if (TimeMode.LocalTime == _TimeMode)
                {
                    //为了生成正确的Bar,使用本地时间
                    _dateTime = Clock.Now;
                }
                else
                {
                    ////直接按HH:mm:ss来解析，测试过这种方法目前是效率比较高的方法
                    //try
                    //{
                    //    // 只有使用交易所行情时才需要处理跨天的问题
                    //    ChangeTradingDay(pDepthMarketData.TradingDay);

                    //    int HH = int.Parse(pDepthMarketData.UpdateTime.Substring(0, 2));
                    //    int mm = int.Parse(pDepthMarketData.UpdateTime.Substring(3, 2));
                    //    int ss = int.Parse(pDepthMarketData.UpdateTime.Substring(6, 2));

                    //    _dateTime = new DateTime(_yyyy, _MM, _dd, HH, mm, ss, pDepthMarketData.UpdateMillisec);
                    //}
                    //catch (Exception ex)
                    //{
                    //    _dateTime = Clock.Now;
                    //}
                }

                if (record.TradeRequested)
                {
                    //通过测试，发现IB的Trade与Quote在行情过来时数量是不同的，在这也做到不同
                    if (DepthMarket.LastPrice == pMarketDataField.LastPrice
                        && DepthMarket.Volume == pMarketDataField.Volume)
                    { }
                    else
                    {
                        //行情过来时是今天累计成交量，得转换成每个tick中成交量之差
                        int volume = pMarketDataField.Volume - DepthMarket.Volume;
                        if (0 == DepthMarket.Volume)
                        {
                            //没有接收到最开始的一条，所以这计算每个Bar的数据时肯定超大，强行设置为0
                            volume = 0;
                        }
                        else if (volume < 0)
                        {
                            //如果隔夜运行，会出现今早成交量0-昨收盘成交量，出现负数，所以当发现为负时要修改
                            volume = pMarketDataField.Volume;
                        }

                        XSpeedTrade trade = new XSpeedTrade(_dateTime,
                            pMarketDataField.LastPrice == double.MaxValue ? 0 : pMarketDataField.LastPrice,
                            volume);

                        trade.DepthMarketData = pMarketDataField;

                        EmitNewTradeEvent(instrument, trade);
                    }
                }

                if (record.QuoteRequested)
                {
                    //if (
                    //DepthMarket.BidVolume1 == pDepthMarketData.BidVolume1
                    //&& DepthMarket.AskVolume1 == pDepthMarketData.AskVolume1
                    //&& DepthMarket.BidPrice1 == pDepthMarketData.BidPrice1
                    //&& DepthMarket.AskPrice1 == pDepthMarketData.AskPrice1
                    //)
                    //{ }
                    //else
                    {
                        XSpeedQuote quote = new XSpeedQuote(_dateTime,
                            pMarketDataField.BidPrice1 == -1 ? 0 : pMarketDataField.BidPrice1,
                            pMarketDataField.BidVolume1,
                            pMarketDataField.AskPrice1 == -1 ? 0 : pMarketDataField.AskPrice1,
                            pMarketDataField.AskVolume1
                        );

                        quote.DepthMarketData = pMarketDataField;

                        EmitNewQuoteEvent(instrument, quote);
                    }
                }

                if (record.MarketDepthRequested)
                {
                    EmitNewMarketDepth(instrument, _dateTime, 0, MDSide.Ask, pMarketDataField.AskPrice1, pMarketDataField.AskVolume1);
                    EmitNewMarketDepth(instrument, _dateTime, 0, MDSide.Bid, pMarketDataField.BidPrice1, pMarketDataField.BidVolume1);
                }
            }
            catch (Exception ex)
            {
                tdlog.Error(ex);
            }
            

            //// 直接回报CTP的行情信息
            //if (EmitOnRtnDepthMarketData)
            //{
            //    CTPAPI.GetInstance().FireOnRtnDepthMarketData(pDepthMarketData);
            //}
        }

        private void EmitNewMarketDepth(Instrument instrument, DateTime datatime, int position, MDSide ask, double price, int size)
        {
            MDOperation insert = MDOperation.Update;
            if (MDSide.Ask == ask)
            {
                if (position >= instrument.OrderBook.Ask.Count)
                {
                    insert = MDOperation.Insert;
                }
            }
            else
            {
                if (position >= instrument.OrderBook.Bid.Count)
                {
                    insert = MDOperation.Insert;
                }
            }

            if (price != -1 && size != -1)
            {
                EmitNewMarketDepth(instrument, new MarketDepth(datatime, "", position, insert, ask, price, size));
            }
        }
        #endregion
    }
}
