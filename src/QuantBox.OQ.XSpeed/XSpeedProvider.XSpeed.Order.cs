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
        #region 撤单
        private void Cancel(SingleOrder order)
        {
            if (order == null)
                return;

            if (!_bTdConnected)
            {
                EmitError(-1, -1, "交易服务器没有连接，无法撤单");
                tdlog.Error("交易服务器没有连接，无法撤单");
                return;
            }

            DFITCOrderRspDataRtnField pOrderRtn;
            if (_Orders4Cancel.TryGetValue(order, out pOrderRtn))
            {
                Instrument inst = InstrumentManager.Instruments[order.Symbol];
                string altSymbol = inst.GetSymbol(Name);

                int localOrderID = pOrderRtn.localOrderID;
                int spdOrderID = pOrderRtn.spdOrderID;
                if (spdOrderID >= 0)
                {
                    localOrderID = -1;
                }
                else
                {
                    spdOrderID = -1;
                }

                TraderApi.TD_CancelOrder(m_pTdApi, altSymbol, localOrderID, spdOrderID);
            }
        }
        #endregion

        #region 下单与订单分割
        private void Send(NewOrderSingle order)
        {
            if (order == null)
                return;

            if (!_bTdConnected)
            {
                EmitError(-1, -1, "交易服务器没有连接，无法报单");
                tdlog.Error("交易服务器没有连接，无法报单");
                return;
            }

            Instrument inst = InstrumentManager.Instruments[order.Symbol];
            string altSymbol = inst.GetSymbol(Name);
            string altExchange = inst.GetSecurityExchange(Name);
            double tickSize = inst.TickSize;
            string type = inst.SecurityType;

        //    CThostFtdcInstrumentField _Instrument;
        //    if (_dictInstruments.TryGetValue(altSymbol, out _Instrument))
        //    {
        //        //从合约列表中取交易所名与tickSize，不再依赖用户手工设置的参数了
        //        tickSize = _Instrument.PriceTick;
        //        altExchange = _Instrument.ExchangeID;
        //    }

            //最小变动价格修正
            double price = order.Price;

            //市价修正，如果不连接行情，此修正不执行，得策略层处理
            DFITCDepthMarketDataField DepthMarket;
            //如果取出来了，并且为有效的，涨跌停价将不为0
            _dictDepthMarketData.TryGetValue(altSymbol, out DepthMarket);

            // 市价单模拟
            if (OrdType.Market == order.OrdType)
            {
                //按买卖调整价格
                if (order.Side == Side.Buy)
                {
                    price = DepthMarket.LastPrice + LastPricePlusNTicks * tickSize;
                }
                else
                {
                    price = DepthMarket.LastPrice - LastPricePlusNTicks * tickSize;
                }
            }

            //没有设置就直接用
            if (tickSize > 0)
            {
                decimal remainder = ((decimal)price % (decimal)tickSize);
                if (remainder != 0)
                {
                    if (order.Side == Side.Buy)
                    {
                        price = Math.Ceiling(price / tickSize) * tickSize;
                    }
                    else
                    {
                        price = Math.Floor(price / tickSize) * tickSize;
                    }
                }
                else
                {
                    //正好能整除，不操作
                }
            }

            if (0 == DepthMarket.UpperLimitPrice
                && 0 == DepthMarket.LowerLimitPrice)
            {
                //涨跌停无效
            }
            else
            {
                //防止价格超过涨跌停
                if (price >= DepthMarket.UpperLimitPrice)
                    price = DepthMarket.UpperLimitPrice;
                else if (price <= DepthMarket.LowerLimitPrice)
                    price = DepthMarket.LowerLimitPrice;
            }


            DFITCOpenCloseTypeType szCombOffsetFlag;

            //根据 梦翔 与 马不停蹄 的提示，新加在Text域中指定开平标志的功能
            if (order.Text.StartsWith(OpenPrefix))
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.OPEN;
            }
            else if (order.Text.StartsWith(ClosePrefix))
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.CLOSE;
            }
            else if (order.Text.StartsWith(CloseTodayPrefix))
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.CLOSETODAY;
            }
            else if (order.Text.StartsWith(CloseYesterdayPrefix))
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.CLOSE;
            }
            else if (order.Text.StartsWith(ExecutePrefix))
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.EXECUTE;
            }
            else
            {
                szCombOffsetFlag = DFITCOpenCloseTypeType.OPEN;
            }

            DFITCInsertType insertType = DFITCInsertType.BASIC_ORDER;
            if(order.Text.Length>=3)
            {
                if(order.Text[2] == '*')
                {
                    insertType = DFITCInsertType.AUTO_ORDER;
                }
            }

            int leave = (int)order.OrderQty;

            DFITCSpeculatorType szCombHedgeFlag = SpeculatorType;

            bool bSupportMarketOrder = SupportMarketOrder.Contains(altExchange);

            tdlog.Info("Side:{0},Price:{1},LastPrice:{2},Qty:{3},Text:{4}",
                order.Side, order.Price, DepthMarket.LastPrice, order.OrderQty, order.Text);

            DFITCBuySellTypeType sBuySellType = order.Side == Side.Buy ? DFITCBuySellTypeType.BUY : DFITCBuySellTypeType.SELL;
            DFITCOrderTypeType orderType = DFITCOrderTypeType.LIMITORDER;
            DFITCOrderPropertyType orderProperty = DFITCOrderPropertyType.NON;
            DFITCInstrumentTypeType nInstrumentType = (FIXSecurityType.FutureOption == type) ? DFITCInstrumentTypeType.OPT_TYPE : DFITCInstrumentTypeType.COMM_TYPE;

            switch(order.TimeInForce)
            {
                case TimeInForce.IOC:
                    orderProperty = DFITCOrderPropertyType.FAK;
                    break;
                case TimeInForce.FOK:
                    orderProperty = DFITCOrderPropertyType.FOK;
                    break;
                default:
                    break;
            }

            int nRet = 0;

            switch (order.OrdType)
            {
                case OrdType.Limit:
                    break;
                case OrdType.Market:
                    if (SwitchMakertOrderToLimitOrder || !bSupportMarketOrder)
                    {
                    }
                    else
                    {
                        //price = 0;
                        orderType = DFITCOrderTypeType.MKORDER;
                    }
                    break;
                default:
                    tdlog.Warn("没有实现{0}", order.OrdType);
                    return;
            }

            nRet = TraderApi.TD_SendOrder(m_pTdApi,-1,
                        altSymbol,
                        sBuySellType,
                        szCombOffsetFlag,
                        szCombHedgeFlag,
                        leave,
                        price,
                        orderType,
                        orderProperty,
                        nInstrumentType,
                        insertType);

            if (nRet > 0)
            {
                _OrderRef2Order[string.Format("{0}:{1}",_RspUserLogin.sessionID,nRet)] = order as SingleOrder;
            }
        }
        #endregion

        #region 报单回报
        private void OnRtnOrder(IntPtr pTraderApi, ref DFITCOrderRtnField pRtnOrderData)
        {
            tdlog.Info("{0},{1},{2},开平{3},价{4},原量{5},撤量{6},状态{7},会话{8},本地{9},柜台{10},报单编号{11}",
                    pRtnOrderData.SuspendTime, pRtnOrderData.InstrumentID, pRtnOrderData.buySellType, pRtnOrderData.openCloseType, pRtnOrderData.insertPrice,
                    pRtnOrderData.orderAmount, pRtnOrderData.cancelAmount, pRtnOrderData.orderStatus,
                    pRtnOrderData.sessionID, pRtnOrderData.localOrderID, pRtnOrderData.spdOrderID, pRtnOrderData.OrderSysID);
        }

        private void OnRspInsertOrder(IntPtr pTraderApi, ref DFITCOrderRspDataRtnField pOrderRtn, ref DFITCErrorRtnField pErrorInfo)
        {
            SingleOrder order;
            if (pErrorInfo.nErrorID != 0)
            {
                tdlog.Error("OnRspInsertOrder:请求{0},会话{1},本地{2},柜台{3},错误{4},{5}",
                    pErrorInfo.requestID, pErrorInfo.sessionID, pErrorInfo.localOrderID, pErrorInfo.spdOrderID, pErrorInfo.nErrorID, pErrorInfo.errorMsg);
                string strKey = string.Format("{0}:{1}", pErrorInfo.sessionID, pErrorInfo.localOrderID);
                if (_OrderRef2Order.TryGetValue(strKey, out order))
                {
                    order.Text = string.Format("{0}|{1}#{2}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pErrorInfo.nErrorID, pErrorInfo.errorMsg);
                    EmitCancelled(order);
                }
            }
            else
            {
                tdlog.Info("OnRspInsertOrder:本地{0},柜台{1},状态{2}",
                    pOrderRtn.localOrderID, pOrderRtn.spdOrderID, pOrderRtn.orderStatus);
                string strKey = string.Format("{0}:{1}", _RspUserLogin.sessionID, pOrderRtn.localOrderID);
                if (_OrderRef2Order.TryGetValue(strKey, out order))
                {
                    _Orders4Cancel[order] = pOrderRtn;
                    EmitAccepted(order);
                }
                //else
                //{
                //    _OrderRef2Order[strKey] = order;
                //    _Orders4Cancel[order] = pOrderRtn;
                //    EmitAccepted(order);
                //}
            }
        }
        #endregion

        #region 成交回报
        private void OnRtnMatchedInfo(IntPtr pTraderApi, ref DFITCMatchRtnField pRtnMatchData)
        {
            tdlog.Info("时{0},合约{1},方向{2},开平{3},价{4},量{5},会话{6},本地{7},柜台{8},成交编号{9}",
                    pRtnMatchData.matchedTime, pRtnMatchData.InstrumentID, pRtnMatchData.buySellType, pRtnMatchData.openCloseType,
                    pRtnMatchData.matchedPrice, pRtnMatchData.matchedAmount, pRtnMatchData.sessionID,pRtnMatchData.localOrderID, pRtnMatchData.spdOrderID, pRtnMatchData.matchID);

            SingleOrder order;
            string strKey = string.Format("{0}:{1}", pRtnMatchData.sessionID, pRtnMatchData.localOrderID);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                EmitFilled(order, pRtnMatchData.matchedPrice, pRtnMatchData.matchedAmount);
            }
        }
        #endregion

        #region 撤单回报
        private void OnRspCancelOrder(IntPtr pTraderApi, ref DFITCOrderRspDataRtnField pOrderRtn, ref DFITCErrorRtnField pErrorInfo)
        {
            SingleOrder order;
            if (pErrorInfo.nErrorID != 0)
            {
                tdlog.Error("OnRspCancelOrder:请求{0},会话{1},本地{2},柜台{3},错误{4},{5}",
                    pErrorInfo.requestID, pErrorInfo.sessionID, pErrorInfo.localOrderID, pErrorInfo.spdOrderID, pErrorInfo.nErrorID, pErrorInfo.errorMsg);

                string strKey = string.Format("{0}:{1}", pErrorInfo.sessionID, pErrorInfo.localOrderID);
                if (_OrderRef2Order.TryGetValue(strKey, out order))
                {
                    EmitCancelReject(order, order.OrdStatus, order.Text);
                }
            }
            else
            {
                tdlog.Info("OnRspCancelOrder:本地{0},柜台{1},状态{2}",
                    pOrderRtn.localOrderID, pOrderRtn.spdOrderID, pOrderRtn.orderStatus);
                string strKey = string.Format("{0}:{1}", _RspUserLogin.sessionID, pOrderRtn.localOrderID);
                if (_OrderRef2Order.TryGetValue(strKey, out order))
                {
                    EmitCancelled(order);
                }
            }
        }

        public void OnRtnCancelOrder(IntPtr pTraderApi, ref DFITCOrderCanceledRtnField pCancelOrderData)
        {
            tdlog.Info("OnRtnCancelOrder:{0},{1},{2},{3},{4},撤量{5},会话{6},本地{7},柜台{8},报单编号{9}",
                pCancelOrderData.canceledTime,pCancelOrderData.InstrumentID,pCancelOrderData.insertPrice,pCancelOrderData.buySellType,
                pCancelOrderData.openCloseType,pCancelOrderData.cancelAmount,
                pCancelOrderData.sessionID,pCancelOrderData.localOrderID,pCancelOrderData.spdOrderID,pCancelOrderData.OrderSysID);

            SingleOrder order;
            //A发的单，B来撤，A收到为非负数，B收到为非正数
            string strKey = string.Format("{0}:{1}", pCancelOrderData.sessionID, pCancelOrderData.localOrderID);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                EmitCancelled(order);
            }
        }
        #endregion

        #region 错误回调
        private void OnRspError(IntPtr pApi, ref DFITCErrorRtnField pRspInfo)
        {
            tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspError:{2},本地{3},柜台{4}",
                pRspInfo.requestID, pRspInfo.nErrorID, pRspInfo.errorMsg,
                pRspInfo.localOrderID,pRspInfo.spdOrderID
                );

            // 这个地方有风险，也就是我没法区分下单被取消，还是撤单被取消
            SingleOrder order;
            string strKey = string.Format("{0}:{1}", _RspUserLogin.sessionID, pRspInfo.localOrderID);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                EmitCancelled(order);
            }
            //EmitError(nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        }
        #endregion
    }
}
