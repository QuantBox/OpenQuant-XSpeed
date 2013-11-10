using QuantBox.CSharp2XSpeed;
using SmartQuant.FIX;
using SmartQuant.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuantBox.OQ.XSpeed
{
    public partial class XSpeedProvider : IInstrumentProvider
    {
        public event SecurityDefinitionEventHandler SecurityDefinition;

        public void SendSecurityDefinitionRequest(FIXSecurityDefinitionRequest request)
        {
            lock (this)
            {
                if (!_bTdConnected)
                {
                    EmitError(-1, -1, "交易没有连接，无法获取合约列表");
                    tdlog.Error("交易没有连接，无法获取合约列表");
                    return;
                }

                string symbol = request.ContainsField(EFIXField.Symbol) ? request.Symbol : null;
                string securityType = request.ContainsField(EFIXField.SecurityType) ? request.SecurityType : null;
                string securityExchange = request.ContainsField(EFIXField.SecurityExchange) ? request.SecurityExchange : null;

                #region 过滤
                List<DFITCExchangeInstrumentRtnField> list = new List<DFITCExchangeInstrumentRtnField>();
                foreach (DFITCExchangeInstrumentRtnField inst in _dictInstruments.Values)
                {
                    int flag = 0;
                    if (null == symbol)
                    {
                        ++flag;
                    }
                    else if (inst.InstrumentID.ToUpper().StartsWith(symbol.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityExchange)
                    {
                        ++flag;
                    }
                    else if (inst.ExchangeID.ToUpper().StartsWith(securityExchange.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityType)
                    {
                        ++flag;
                    }
                    else
                    {
                        if (FIXSecurityType.Future == securityType)
                        {
                            if (DFITCInstrumentTypeType.COMM_TYPE == inst.instrumentType)
                            {
                                ++flag;
                            }
                        }
                        //else if (FIXSecurityType.MultiLegInstrument == securityType)//理解上是否有问题
                        //{
                        //    if (TThostFtdcProductClassType.Combination == inst.ProductClass)
                        //    {
                        //        ++flag;
                        //    }
                        //}
                        else if (FIXSecurityType.FutureOption == securityType)
                        {
                            if (DFITCInstrumentTypeType.OPT_TYPE == inst.instrumentType)
                            {
                                ++flag;
                            }
                        }
                    }

                    if (3 == flag)
                    {
                        list.Add(inst);
                    }
                }
                #endregion

                #region 过滤1
                List<DFITCAbiInstrumentRtnField> list1 = new List<DFITCAbiInstrumentRtnField>();
                foreach (DFITCAbiInstrumentRtnField inst in _dictAbiInstruments.Values)
                {
                    int flag = 0;
                    if (null == symbol)
                    {
                        ++flag;
                    }
                    else if (inst.InstrumentID.ToUpper().StartsWith(symbol.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityExchange)
                    {
                        ++flag;
                    }
                    else if (inst.ExchangeID.ToUpper().StartsWith(securityExchange.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityType)
                    {
                        ++flag;
                    }
                    else
                    {
                        if (FIXSecurityType.MultiLegInstrument == securityType)
                        {
                            ++flag;
                        }
                    }

                    if (3 == flag)
                    {
                        list1.Add(inst);
                    }
                }
                #endregion

                list.Sort(SortDFITCExchangeInstrumentRtnField);
                list1.Sort(SortDFITCAbiInstrumentRtnField);

                //如果查出的数据为0，应当想法立即返回
                if (0 == list.Count&&0==list1.Count)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = 1//有个除0错误的问题
                    };
                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }

                #region 期货 期权
                foreach (DFITCExchangeInstrumentRtnField inst in list)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        //SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = list.Count + list1.Count,
                    };

                    {
                        string securityType2;
                        switch (inst.instrumentType)
                        {
                            case DFITCInstrumentTypeType.COMM_TYPE:
                                securityType2 = FIXSecurityType.Future;
                                break;
                            case DFITCInstrumentTypeType.OPT_TYPE:
                                securityType2 = FIXSecurityType.FutureOption;
                                var match = Regex.Match(inst.InstrumentID, @"(\d+)(-?)([CP])(-?)(\d+)");
                                if (match.Success)
                                {
                                    definition.AddField(EFIXField.PutOrCall, match.Groups[3].Value == "C" ? FIXPutOrCall.Call : FIXPutOrCall.Put);
                                    definition.AddField(EFIXField.StrikePrice, double.Parse(match.Groups[5].Value));
                                }                                
                                break;
                            default:
                                securityType2 = FIXSecurityType.NoSecurityType;
                                break;
                        }
                        definition.AddField(EFIXField.SecurityType, securityType2);
                    }
                    {
                        double x = inst.minPriceFluctuation;
                        int i = 0;
                        for (; x - (int)x != 0; ++i)
                        {
                            x = x * 10;
                        }
                        definition.AddField(EFIXField.PriceDisplay, string.Format("F{0}", i));
                    }

                    
                    definition.AddField(EFIXField.Symbol, inst.InstrumentID);
                    definition.AddField(EFIXField.SecurityExchange, inst.ExchangeID);
                    definition.AddField(EFIXField.Currency, "CNY");//Currency.CNY
                    definition.AddField(EFIXField.TickSize, inst.minPriceFluctuation);
                    definition.AddField(EFIXField.SecurityDesc, inst.VarietyName);
                    definition.AddField(EFIXField.Factor, inst.contractMultiplier);

                    try
                    {
                        definition.AddField(EFIXField.MaturityDate, DateTime.ParseExact(inst.instrumentMaturity, "yyyy.MM.dd", CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex)
                    {
                        tdlog.Warn("合约:{0},字段内容:{1},{2}", inst.InstrumentID, inst.instrumentMaturity, ex.Message);
                    }

                    //还得补全内容

                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }
                #endregion

                #region 组合
                foreach (DFITCAbiInstrumentRtnField inst in list1)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        //SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = list.Count + list1.Count,
                    };
                    string securityType2 = FIXSecurityType.MultiLegInstrument;
                    definition.AddField(EFIXField.SecurityType, securityType2);

                    definition.AddField(EFIXField.Symbol, inst.InstrumentID);//
                    definition.AddField(EFIXField.SecurityExchange, inst.ExchangeID);
                    definition.AddField(EFIXField.Currency, "CNY");//Currency.CNY
                    definition.AddField(EFIXField.SecurityDesc, inst.instrumentName);                    

                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }
                #endregion
            }
        }

        private static int SortDFITCExchangeInstrumentRtnField(DFITCExchangeInstrumentRtnField a1, DFITCExchangeInstrumentRtnField a2)
        {
            return a1.InstrumentID.CompareTo(a2.InstrumentID);
        }

        private static int SortDFITCAbiInstrumentRtnField(DFITCAbiInstrumentRtnField a1, DFITCAbiInstrumentRtnField a2)
        {
            return a1.InstrumentID.CompareTo(a2.InstrumentID);
        }
    }
}
