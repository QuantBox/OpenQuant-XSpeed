﻿using System;
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
        private fnOnConnect _fnOnConnect_Holder;
        private fnOnDisconnect _fnOnDisconnect_Holder;
        private fnOnRspError _fnOnRspError_Holder;
        private fnOnMarketData _fnOnMarketData_Holder;
        private fnOnRspArbitrageInstrument _fnOnRspArbitrageInstrument_Holder;
        private fnOnRspInsertCancelOrder _fnOnRspCancelOrder_Holder;
        private fnOnRspInsertCancelOrder _fnOnRspInsertOrder_Holder;
        private fnOnRspQryExchangeInstrument _fnOnRspQryExchangeInstrument_Holder;
        //private fnOnRspQryDepthMarketData _fnOnRspQryDepthMarketData_Holder;
        //private fnOnRspQryInstrument _fnOnRspQryInstrument_Holder;
        //private fnOnRspQryInstrumentCommissionRate _fnOnRspQryInstrumentCommissionRate_Holder;
        //private fnOnRspQryInstrumentMarginRate _fnOnRspQryInstrumentMarginRate_Holder;
        //private fnOnRspQryInvestorPosition _fnOnRspQryInvestorPosition_Holder;
        //private fnOnRspQryTradingAccount _fnOnRspQryTradingAccount_Holder;

        private fnOnRtnCancelOrder _fnOnRtnCancelOrder_Holder;
        private fnOnRtnInstrumentStatus _fnOnRtnInstrumentStatus_Holder;
        private fnOnRtnMatchedInfo _fnOnRtnMatchedInfo_Holder;
        private fnOnRtnOrder _fnOnRtnOrder_Holder;
        //private fnOnRtnTrade _fnOnRtnTrade_Holder;

        private fnOnRspQuoteSubscribe _fnOnRspQuoteSubscribe_Holder;
        private fnOnRtnQuoteSubscribe _fnOnRtnQuoteSubscribe_Holder;



        #region 回调
        private void InitCallbacks()
        {
            //由于回调函数可能被GC回收，所以用成员变量将回调函数保存下来
            _fnOnConnect_Holder = OnConnect;
            _fnOnDisconnect_Holder = OnDisconnect;
            _fnOnMarketData_Holder = OnMarketData;
            //_fnOnErrRtnOrderAction_Holder = OnErrRtnOrderAction;
            //_fnOnErrRtnOrderInsert_Holder = OnErrRtnOrderInsert;
            _fnOnRspArbitrageInstrument_Holder = OnRspArbitrageInstrument;
            _fnOnRspError_Holder = OnRspError;
            _fnOnRspCancelOrder_Holder = OnRspCancelOrder;
            _fnOnRspInsertOrder_Holder = OnRspInsertOrder;
            _fnOnRspQryExchangeInstrument_Holder = OnRspQryExchangeInstrument;
            //_fnOnRspQryDepthMarketData_Holder = OnRspQryDepthMarketData;
            //_fnOnRspQryInstrument_Holder = OnRspQryInstrument;
            //_fnOnRspQryInstrumentCommissionRate_Holder = OnRspQryInstrumentCommissionRate;
            //_fnOnRspQryInstrumentMarginRate_Holder = OnRspQryInstrumentMarginRate;
            //_fnOnRspQryInvestorPosition_Holder = OnRspQryInvestorPosition;
            //_fnOnRspQryTradingAccount_Holder = OnRspQryTradingAccount;
            _fnOnRtnCancelOrder_Holder = OnRtnCancelOrder;
            _fnOnRtnInstrumentStatus_Holder = OnRtnInstrumentStatus;
            //_fnOnRtnDepthMarketData_Holder = OnRtnDepthMarketData;
            _fnOnRtnMatchedInfo_Holder = OnRtnMatchedInfo;
            _fnOnRtnOrder_Holder = OnRtnOrder;
            //_fnOnRtnTrade_Holder = OnRtnTrade;
            _fnOnRspQuoteSubscribe_Holder = OnRspQuoteSubscribe;
            _fnOnRtnQuoteSubscribe_Holder = OnRtnQuoteSubscribe;
        }
        #endregion

        private IntPtr m_pMsgQueue = IntPtr.Zero;   //消息队列指针
        private IntPtr m_pMdApi = IntPtr.Zero;      //行情对象指针
        private IntPtr m_pTdApi = IntPtr.Zero;      //交易对象指针

        //行情有效状态，约定连接上并通过认证为有效
        private volatile bool _bMdConnected;
        //交易有效状态，约定连接上，通过认证并进行结算单确认为有效
        private volatile bool _bTdConnected;

        //表示用户操作，也许有需求是用户有多个行情，只连接第一个等
        private bool _bWantMdConnect;
        private bool _bWantTdConnect;

        private readonly object _lockMd = new object();
        private readonly object _lockTd = new object();
        private readonly object _lockMsgQueue = new object();

        //记录交易登录成功后的SessionID、FrontID等信息
        private DFITCUserLoginInfoRtnField _RspUserLogin;

        //记录界面生成的报单，用于定位收到回报消息时所确定的报单,可以多个Ref对应一个Order
        private readonly Dictionary<string, SingleOrder> _OrderRef2Order = new Dictionary<string, SingleOrder>();
        //一个Order可能分拆成多个报单，如可能由平今与平昨，或开新单组合而成
        private readonly Dictionary<SingleOrder, DFITCOrderRspDataRtnField> _Orders4Cancel = new Dictionary<SingleOrder, DFITCOrderRspDataRtnField>();
        //交易所信息映射到本地信息
        private readonly Dictionary<string, string> _OrderSysID2OrderRef = new Dictionary<string, string>();

        ////记录账号的实际持仓，保证以最低成本选择开平
        //private readonly DbInMemInvestorPosition _dbInMemInvestorPosition = new DbInMemInvestorPosition();
        //记录合约实际行情，用于向界面通知行情用，这里应当记录AltSymbol
        private readonly Dictionary<string, DFITCDepthMarketDataField> _dictDepthMarketData = new Dictionary<string, DFITCDepthMarketDataField>();
        //记录合约列表,从实盘合约名到对象的映射
        private readonly Dictionary<string, DFITCExchangeInstrumentRtnField> _dictInstruments = new Dictionary<string, DFITCExchangeInstrumentRtnField>();
        private readonly Dictionary<string, DFITCAbiInstrumentRtnField> _dictAbiInstruments = new Dictionary<string, DFITCAbiInstrumentRtnField>();
        ////记录手续费率,从实盘合约名到对象的映射
        //private readonly Dictionary<string, CThostFtdcInstrumentCommissionRateField> _dictCommissionRate = new Dictionary<string, CThostFtdcInstrumentCommissionRateField>();
        ////记录保证金率,从实盘合约名到对象的映射
        //private readonly Dictionary<string, CThostFtdcInstrumentMarginRateField> _dictMarginRate = new Dictionary<string, CThostFtdcInstrumentMarginRateField>();
        //记录
        private readonly Dictionary<string, DataRecord> _dictAltSymbol2Instrument = new Dictionary<string, DataRecord>();

        //用于行情的时间，只在登录时改动，所以要求开盘时能得到更新
        private int _yyyy;
        private int _MM;
        private int _dd;

        private ServerItem server;
        private AccountItem account;

        #region 合约列表
        private void OnRspQryExchangeInstrument(IntPtr pTraderApi, ref DFITCExchangeInstrumentRtnField pInstrumentData, ref DFITCErrorRtnField pErrorInfo, bool bIsLast)
        {
            if (0 == pErrorInfo.nErrorID)
            {
                if (pInstrumentData.InstrumentID.Length > 0)
                {
                    _dictInstruments[pInstrumentData.InstrumentID] = pInstrumentData;
                    if (bIsLast)
                    {
                        tdlog.Info("{0} {1}已经接收完成，当前总计{2}", pInstrumentData.ExchangeID, pInstrumentData.instrumentType, _dictInstruments.Count);
                    }
                }
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryExchangeInstrument:{2}", pErrorInfo.requestID, pErrorInfo.nErrorID, pErrorInfo.errorMsg);
                EmitError(pErrorInfo.requestID, pErrorInfo.nErrorID, "OnRspQryExchangeInstrument:" + pErrorInfo.errorMsg);
            }
        }

        private void OnRspArbitrageInstrument(IntPtr pTraderApi, ref DFITCAbiInstrumentRtnField pAbiInstrumentData, ref DFITCErrorRtnField pErrorInfo, bool bIsLast)
        {
            if (0 == pErrorInfo.nErrorID)
            {
                if (pAbiInstrumentData.InstrumentID.Length > 0)
                {
                    _dictAbiInstruments[pAbiInstrumentData.InstrumentID] = pAbiInstrumentData;
                    if (bIsLast)
                    {
                        tdlog.Info("{0} 组合已经接收完成，当前总计{1}", pAbiInstrumentData.ExchangeID, _dictAbiInstruments.Count);
                    }
                }
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspArbitrageInstrument:{2}", pErrorInfo.requestID, pErrorInfo.nErrorID, pErrorInfo.errorMsg);
                EmitError(pErrorInfo.requestID, pErrorInfo.nErrorID, "OnRspArbitrageInstrument:" + pErrorInfo.errorMsg);
            }
        }
        #endregion

        //#region 手续费列表
        //private void OnRspQryInstrumentCommissionRate(IntPtr pTraderApi, ref CThostFtdcInstrumentCommissionRateField pInstrumentCommissionRate, ref CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        _dictCommissionRate[pInstrumentCommissionRate.InstrumentID] = pInstrumentCommissionRate;
        //        tdlog.Info("已经接收手续费率 {0}", pInstrumentCommissionRate.InstrumentID);

        //        //通知单例
        //        CTPAPI.GetInstance().FireOnRspQryInstrumentCommissionRate(pInstrumentCommissionRate);
        //    }
        //    else
        //    {
        //        tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInstrumentCommissionRate:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentCommissionRate:" + pRspInfo.ErrorMsg);
        //    }
        //}
        //#endregion

        //#region 保证金率列表
        //private void OnRspQryInstrumentMarginRate(IntPtr pTraderApi, ref CThostFtdcInstrumentMarginRateField pInstrumentMarginRate, ref CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        _dictMarginRate[pInstrumentMarginRate.InstrumentID] = pInstrumentMarginRate;
        //        tdlog.Info("已经接收保证金率 {0}", pInstrumentMarginRate.InstrumentID);

        //        //通知单例
        //        CTPAPI.GetInstance().FireOnRspQryInstrumentMarginRate(pInstrumentMarginRate);
        //    }
        //    else
        //    {
        //        tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInstrumentMarginRate:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentMarginRate:" + pRspInfo.ErrorMsg);
        //    }
        //}
        //#endregion

        //#region 持仓回报
        //private void OnRspQryInvestorPosition(IntPtr pTraderApi, ref CThostFtdcInvestorPositionField pInvestorPosition, ref CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        _dbInMemInvestorPosition.InsertOrReplace(
        //            pInvestorPosition.InstrumentID,
        //            pInvestorPosition.PosiDirection,
        //            pInvestorPosition.HedgeFlag,
        //            pInvestorPosition.PositionDate,
        //            pInvestorPosition.Position);
        //        timerPonstion.Enabled = false;
        //        timerPonstion.Enabled = true;
        //    }
        //    else
        //    {
        //        tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInvestorPosition:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInvestorPosition:" + pRspInfo.ErrorMsg);
        //    }
        //}
        //#endregion

        //#region 资金回报
        //CThostFtdcTradingAccountField m_TradingAccount;
        //private void OnRspQryTradingAccount(IntPtr pTraderApi, ref CThostFtdcTradingAccountField pTradingAccount, ref CThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLastt)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        m_TradingAccount = pTradingAccount;
        //        //有资金信息过来了，重新计时
        //        timerAccount.Enabled = false;
        //        timerAccount.Enabled = true;
        //    }
        //    else
        //    {
        //        tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryTradingAccount:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryTradingAccount:" + pRspInfo.ErrorMsg);
        //    }
        //}
        //#endregion

        #region 交易所状态
        private void OnRtnInstrumentStatus(IntPtr pTraderApi, ref DFITCInstrumentStatusField pInstrumentStatus)
        {
            tdlog.Info("{0},{1},{2},{3}",
                pInstrumentStatus.ExchangeID, pInstrumentStatus.InstrumentID,
                pInstrumentStatus.InstrumentStatus, pInstrumentStatus.EnterReason);

            //通知单例
            //CTPAPI.GetInstance().FireOnRtnInstrumentStatus(pInstrumentStatus);
        }
        #endregion

        #region 做市商
        private void OnRspQuoteSubscribe(IntPtr pTraderApi, ref DFITCQuoteSubscribeRspField pRspQuoteSubscribeData)
        {
            tdlog.Info("报价通知订阅响应：{0}",
                pRspQuoteSubscribeData.subscribeFlag);
        }

        private void OnRtnQuoteSubscribe(IntPtr pTraderApi, ref DFITCQuoteSubscribeRtnField pRtnQuoteSubscribeData)
        {
            tdlog.Info("报价通知订阅回报：{0},{1},{2},{3},{4},{5}",
                pRtnQuoteSubscribeData.quoteID,
                pRtnQuoteSubscribeData.ExchangeID,
                pRtnQuoteSubscribeData.InstrumentID,
                pRtnQuoteSubscribeData.instrumentType,
                pRtnQuoteSubscribeData.buySellType,
                pRtnQuoteSubscribeData.source);

            XSpeedAPI.GetInstance().FireOnRtnQuoteSubscribe(pRtnQuoteSubscribeData);
        }
        #endregion
    }
}
