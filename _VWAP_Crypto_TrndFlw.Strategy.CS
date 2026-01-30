using ATCenterProxy.interop;
using PowerLanguage.Function;
using PowerLanguage.TradeManager;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;

namespace PowerLanguage.Strategy
{
    public class _VWAP_Crypto_TrndFlw : SignalObject
    {
        // ===== Inputs =====

        [Input] public int sessionStartTimeC { get; set; } = 100;
        [Input] public int sessionEndTimeC { get; set; } = 2300;

        [Input] public double InitialCapital { get; set; } = 100000;
        [Input] public double RiskPercent { get; set; } = 0.01;

        [Input] public int MyPtnLY { get; set; } = 144;
        [Input] public int MyPtnSY { get; set; } = 152;
        [Input] public int MyPtnLN { get; set; } = 153;
        [Input] public int MyPtnSN { get; set; } = 27;

        [Input] public int MyStartTime { get; set; } = 500;
        [Input] public int MyEndTime { get; set; } = 1900;
        [Input] public int MyStartPause { get; set; } = 1400;
        [Input] public int MyEndPause { get; set; } = 1500;

        [Input] public int ATRLen { get; set; } = 14;
        [Input] public double ATRMult { get; set; } = 4.9;
        [Input] public double RRR_tp { get; set; } = 5.8;
        [Input] public int ID { get; set; } = 0;

        [Input] public double NumDevsUp { get; set; } = 1.5;
        [Input] public double NumDevsDn { get; set; } = 1.1;

        [Input] public bool EnableDayPatterns { get; set; } = true;
        [Input] public bool DebugLogging { get; set; } = true;
        [Input] public bool DebugOnlyInWindow { get; set; } = false;
        [Input] public bool Parity_Test_Mode { get; set; } = false;

        [Input] public int SessionTZOffsetMinutes { get; set; } = 0;
        [Input] public int ResetAtHH { get; set; } = 0;
        [Input] public int ResetAtMM { get; set; } = 30;
        [Input] public bool ResetOnlyWeekdays { get; set; } = false;
        [Input] public double BPV_Override { get; set; } = 0.0;
        [Input] public int LogTimeOffsetMinutes { get; set; } = 0;
        [Input] public int RestartCondition { get; set; } = 1;

        [Input] public int MaxStoplossInSession { get; set; } = 1;
        [Input] public double MaxPercentLossInSession { get; set; } = 0.7;
        [Input] public double MaxDailyDrawdown { get; set; } = 1.3;

        [Input] public int TradeOnMonday { get; set; } = 2;
        [Input] public int TradeOnTuesday { get; set; } = 3;
        [Input] public int TradeOnWednesday { get; set; } = 4;
        [Input] public int TradeOnThursday { get; set; } = 5;
        [Input] public int TradeOnFriday { get; set; } = -1;
        [Input] public int TradeOnSaturday { get; set; } = -1;
        [Input] public int TradeOnSunday { get; set; } = -1;

        // ================= TRAILING STOP - NOVÉ INPUTY =================
        [Input] public bool UseTrailingStop { get; set; } = false;           // Zapni/vypni trailing stop
        [Input] public double TrailActivationATR { get; set; } = 1.3;    // 2.75  // 1.5 // Aktivuj trailing po X × ATR profite (z Adaptrade:  1.2223) // 1.75 2.00 2.5 1.25 asi nejaleise
        [Input] public double TrailPct { get; set; } = 83;        // 35        // Trail % z profitu (z Adaptrade: 83%)
        [Input] public bool UseATRFloor { get; set; } = false;               // Použiť ATR floor pre minimum stop distance
        [Input] public double ATRFloorMult { get; set; } = 1;     //1.25        // ATR floor multiplikátor //0.25 - 0.75

        // ================= BREAK-EVEN (existujúce) =================
        [Input] public bool UseBreakEven { get; set; } = false;
        [Input] public double BE_Trigger_R { get; set; } = 2.9;
        [Input] public int BE_OffsetTicks { get; set; } = 0;

        // ================= FILTERS =================
        [Input] public bool UseTrendFilter { get; set; } = false;
        [Input] public int TrendEmaLen { get; set; } = 75;
        [Input] public double TrendAtrBufferMult { get; set; } = 0.0;

        [Input] public bool UseVolRegimeFilter { get; set; } = true;
        [Input] public int AtrRegimeLen { get; set; } = 350;
        [Input] public double AtrRatioMin { get; set; } = 0.6;
        [Input] public double AtrRatioMax { get; set; } = 2.1;

        [Input] public bool UseBandWidthFilter { get; set; } = true;
        [Input] public double BandWidthMinAtr { get; set; } = 0.75;
        [Input] public double BandWidthMaxAtr { get; set; } = 7.5;

        [Input] public bool UseDistanceFromVWAPFilter { get; set; } = false;
        [Input] public double DistMaxAtr { get; set; } = 6.0;


        // === Long-only VWAP distance + VWAP trend filter ===
        [Input] public double MaxLongDistATR { get; set; } = 0;      // max vzdialenosť od VWAP v ATR (napr. 0.5 až 1.5)
        [Input] public int VwapTrendLookback { get; set; } = 8;       // koľko barov späť porovnávať
        [Input] public double VwapTrendMinATR { get; set; } = 0.02;    // min “pohyb” VWAP za lookback v ATR, aby to nebolo flat
        [Input] public bool RequireVwapRisingForLong { get; set; } = true; // long len keď VWAP rastie (nie len “nie flat”)



        public enum EResetMode { Daily = 1, Weekly = 2, Session = 3, Never = 4 }
        [Input] public EResetMode ResetMode { get; set; } = EResetMode.Session;

        // ===== Internals =====
        private double[] _ohlcVals;
        private int _stoplossThisSession = 0;

        // VWAP kumulatívy
        private double _PriceW, _ShareW, _P2Sum;
        private double _vwapNow = 0.0;

        // Pásma
        private double _upperPrevBar, _lowerPrevBar;
        private double _upperNow, _lowerNow;

        // ATR (SMA TR)
        private Queue<double> _trQ;
        private double _trSum, _atrCurr;

        private IOrderMarket _longNext, _shortNext;
        private IOrderMarket _longClose, _shortClose;
        private int _entryBar = -1;
        private double _actualTradeSL;
        private double _actualTradeTP;
        private int _actualTradeContracts;
        private double _actualTradeSLPrice = 0;
        private double _actualTradeEntryPrice = 0;
        private bool _inTrade = false;
        private bool _isLong = false;
        private bool _isShort = false;
        private bool _noMoreTradesInSession = false;
        private double _sessionStartEquity = 0.0;
        private double _sessionPercentLoss = 0.0;
        private int _prevMarketPosition = 0;
        private double _dayMaxDrawdown = 0.0;
        private bool _ddLimitHit;

        // BE (existujúce)
        private double _entryPrice;
        private double _origSlPts;
        private bool _beArmed;
        private bool _beActivated;

        // ================= TRAILING STOP - NOVÉ PREMENNÉ =================
        private double _trailStopPriceLong = 0;      // Aktuálna trailing stop cena pre LONG
        private double _trailStopPriceShort = 0;     // Aktuálna trailing stop cena pre SHORT
        private bool _trailActivatedLong = false;    // Či je trailing aktívny pre LONG
        private bool _trailActivatedShort = false;   // Či je trailing aktívny pre SHORT
        private double _initialStopLong = 0;         // Počiatočný protective stop pre LONG
        private double _initialStopShort = 0;        // Počiatočný protective stop pre SHORT
        private double _atrAtEntry = 0;              // ATR pri vstupe (pre floor calculation)

        // Filters
        private double _emaTrend = double.NaN;
        private readonly Queue<double> _atrRegQ = new Queue<double>();
        private double _atrRegSum = 0.0;

        private double _emaTrendPrev = double.NaN;   // na kontrolu rising EMA


        // --- VWAP history for trend check ---
        private readonly Queue<double> _vwapHist = new Queue<double>();
        private double _vwapPrev = double.NaN;



        // Session
        private bool _okayToCalc = false;
        private int _prevSessionId = -1;
        private int _weekAnchor = -1;

        private readonly double[,] _pastDays = new double[4, 20];
        private int _actDayIdx = 0;
        private bool _actInit = false;
        private double _actO, _actH, _actL, _actC;

        private static int ToHHmm(DateTime dt) => dt.Hour * 100 + dt.Minute;
        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        private const string TELEGRAM_BOT_TOKEN = "";
        private const string TELEGRAM_CHAT_ID = "5586622274";

        private bool _sessInit = false;
        private List<SessionWin> _sessions = new List<SessionWin>();

        public class CustomTrade
        {
            public double EntryPrice { get; set; }
            public double StopLossPrice { get; set; }
            public double TargetPrice { get; set; }
            public double OnlyStopLoss { get; set; }
            public double OnlyTargetPrice { get; set; }
            public double Risk { get; set; }
            public double Reward { get; set; }
            public double Loss { get; set; }
            public bool IsLong { get; set; }
        }

        private sealed class SessionWin
        {
            public int StartDow;
            public int StartTime;
            public int EndDow;
            public int EndTime;
        }

        public _VWAP_Crypto_TrndFlw(object ctx) : base(ctx) { }

        protected override void Create()
        {
            _ohlcVals = new double[24];
            _trQ = new Queue<double>(Math.Max(1, ATRLen));
            _trSum = 0.0; _atrCurr = 0.0;

            _longNext = OrderCreator.MarketNextBar(new SOrderParameters(Contracts.UserSpecified, "VWAP_Long", EOrderAction.Buy));
            _shortNext = OrderCreator.MarketNextBar(new SOrderParameters(Contracts.UserSpecified, "VWAP_Short", EOrderAction.SellShort));

            _longClose = OrderCreator.MarketThisBar(new SOrderParameters(Contracts.UserSpecified, "VWAP_Long_Close", EOrderAction.Sell));
            _shortClose = OrderCreator.MarketThisBar(new SOrderParameters(Contracts.UserSpecified, "VWAP_Short_Close", EOrderAction.BuyToCover));
        }

        protected override void StartCalc()
        {
            ResetVWAPAccums();
            _vwapHist.Clear();
            _vwapPrev = double.NaN;

            _trQ.Clear(); _trSum = 0.0; _atrCurr = 0.0;
            _entryBar = -1;

            _okayToCalc = false;
            _prevSessionId = -1;
            _weekAnchor = -1;
            _stoplossThisSession = 0;
            _sessionPercentLoss = 0;
            _ddLimitHit = false;

            _sessionStartEquity = InitialCapital;

            // Reset trailing stop premenných
            ResetTrailingStopState();
        }

        // ================= TRAILING STOP - NOVÁ METÓDA NA RESET =================
        private void ResetTrailingStopState()
        {
            _trailStopPriceLong = 0;
            _trailStopPriceShort = 0;
            _trailActivatedLong = false;
            _trailActivatedShort = false;
            _initialStopLong = 0;
            _initialStopShort = 0;
            _atrAtEntry = 0;
        }

        // ===== Helpers =====
        private DateTime EX(int ago = 0) => Bars.Time[ago].AddMinutes(SessionTZOffsetMinutes);
        private DateTime LT(int ago = 0) => Bars.Time[ago].AddMinutes(LogTimeOffsetMinutes);
        private static double Typical(double h, double l, double c) => (h + l + c) / 3.0;

        #region Session

        private void InitSessionsIfNeeded()
        {
            if (_sessInit) return;

            _sessions = new List<SessionWin>
            {
                new SessionWin { StartDow = 1, StartTime = 30, EndDow = 1, EndTime = 2359 },
                new SessionWin { StartDow = 2, StartTime = 30, EndDow = 2, EndTime = 2359 },
                new SessionWin { StartDow = 3, StartTime = 30, EndDow = 3, EndTime = 2359 },
                new SessionWin { StartDow = 4, StartTime = 30, EndDow = 4, EndTime = 2359 },
                new SessionWin { StartDow = 5, StartTime = 30, EndDow = 5, EndTime = 2359 },
                new SessionWin { StartDow = 6, StartTime = 30, EndDow = 6, EndTime = 2359 },
                new SessionWin { StartDow = 7, StartTime = 30, EndDow = 7, EndTime = 2359 },
            };

            _sessInit = true;
        }

        private int MinutesIntoWeek(DateTime dt)
        {
            int dow = (int)dt.DayOfWeek;
            if (dow == 0) dow = 7;

            int hh = dt.Hour;
            int mm = dt.Minute;
            int minutesToday = hh * 60 + mm;

            int minutesBefore = (dow - 1) * 24 * 60;
            return minutesBefore + minutesToday;
        }

        private int CurrentSession(DateTime dt)
        {
            InitSessionsIfNeeded();

            int nowMin = MinutesIntoWeek(dt);
            int barType = 0;
            int current = -1;

            for (int i = 0; i < _sessions.Count; i++)
            {
                var s = _sessions[i];

                int startMin = MinutesIntoWeekFor(s.StartDow, s.StartTime);
                int endMin = MinutesIntoWeekFor(s.EndDow, s.EndTime);

                bool inSession;

                if (startMin <= endMin)
                {
                    inSession = (nowMin > startMin || ((barType == 0 || barType > 4) && nowMin >= startMin)) && nowMin <= endMin;
                }
                else
                {
                    inSession = nowMin >= startMin || nowMin <= endMin;
                }

                if (inSession)
                {
                    current = i + 1;
                    break;
                }
            }

            return current;
        }

        private int MinutesIntoWeekFor(int dow, int hhmm)
        {
            int hh = hhmm / 100;
            int mm = hhmm % 100;
            return (dow - 1) * 24 * 60 + hh * 60 + mm;
        }

        #endregion

        private void SendTelegramMessage(string message)
        {
            // Telegram implementácia - ponechaná prázdna
        }

        private double GetPLTicks()
        {
            double t = 0.0;
            try { t = Bars.Ticks[0]; } catch { }
            if (t <= 0.0)
            {
                try { t = Bars.Volume[0]; } catch { }
            }
            if (t <= 0.0) t = 1.0;
            return t;
        }

        private bool SessionOHLC5(int startTime, int endTime, double[] ohlcOut)
        {
            if (startTime == 0 && endTime == 0)
            {
                startTime = 100;
                endTime = 2300;
            }

            DateTime now = EX(0);
            DateTime prev = (Bars.CurrentBar > 0) ? EX(1) : now.AddMinutes(-1);

            int t = ToHHmm(now);
            int t1 = ToHHmm(prev);

            bool oneDaySession = startTime < endTime;

            bool timeStarted = t > startTime;
            bool timeNotEnded = t <= endTime;
            bool prevLessStart = t1 <= startTime;

            bool inSession = oneDaySession
                ? (timeStarted && timeNotEnded)
                : (timeStarted || timeNotEnded);

            bool isStartOfSession = inSession && timeStarted && prevLessStart;

            if (!oneDaySession)
            {
                if (inSession && now.Date != prev.Date && prevLessStart)
                    isStartOfSession = true;
            }
            else
            {
                if (inSession && now.Date != prev.Date)
                    isStartOfSession = true;
            }

            if (!_actInit)
            {
                _actInit = true;
                _actO = Bars.Open[0];
                _actH = Bars.High[0];
                _actL = Bars.Low[0];
                _actC = Bars.Close[0];
            }

            _pastDays[0, _actDayIdx] = _actO;
            _pastDays[1, _actDayIdx] = _actH;
            _pastDays[2, _actDayIdx] = _actL;
            _pastDays[3, _actDayIdx] = _actC;

            if (inSession)
            {
                _actH = Math.Max(_actH, Bars.High[0]);
                _actL = Math.Min(_actL, Bars.Low[0]);
                _actC = Bars.Close[0];
            }

            if (isStartOfSession)
            {
                _actO = Bars.Open[0];
                _actH = Bars.High[0];
                _actL = Bars.Low[0];
                _actC = Bars.Close[0];

                _actDayIdx = (_actDayIdx + 1) % 20;
            }

            ohlcOut[0] = _actO;
            ohlcOut[1] = _actH;
            ohlcOut[2] = _actL;
            ohlcOut[3] = _actC;

            for (int dayRef = 1; dayRef <= 5; dayRef++)
            {
                int retIdx = (20 + _actDayIdx - dayRef) % 20;
                int baseIdx = dayRef * 4;
                ohlcOut[baseIdx + 0] = _pastDays[0, retIdx];
                ohlcOut[baseIdx + 1] = _pastDays[1, retIdx];
                ohlcOut[baseIdx + 2] = _pastDays[2, retIdx];
                ohlcOut[baseIdx + 3] = _pastDays[3, retIdx];
            }

            return isStartOfSession;
        }

        private bool DayPatterns(int code, double[] ohlc)
        {
            if (!EnableDayPatterns) return true;
            switch (code)
            {
                case 144:
                    return Bars.Close[0] > ohlc[0];

                case 152:
                    return true;

                case 153:
                    return false;

                case 27:
                    double opend5 = ohlc[20];
                    double closed1 = ohlc[7];
                    double body5d = Math.Abs(opend5 - closed1);

                    double highd1 = ohlc[5], highd2 = ohlc[9], highd3 = ohlc[13], highd4 = ohlc[17], highd5 = ohlc[21];
                    double lowd1 = ohlc[6], lowd2 = ohlc[10], lowd3 = ohlc[14], lowd4 = ohlc[18], lowd5 = ohlc[22];

                    double maxHigh = Math.Max(highd1, Math.Max(highd2, Math.Max(highd3, Math.Max(highd4, highd5))));
                    double minLow = Math.Min(lowd1, Math.Min(lowd2, Math.Min(lowd3, Math.Min(lowd4, lowd5))));
                    double range5d = Math.Max(0.0, maxHigh - minLow);

                    if (DebugLogging)
                    {
                        Output.WriteLine($"DayPatterns(27): body5d={body5d: F2}  range5d={range5d:F2}  -> {(body5d > 0.9 * range5d)}");
                    }

                    return body5d > 0.9 * range5d;

                default:
                    return true;
            }
        }

        private static bool OHLC5Complete(double[] o)
        {
            return o[20] != 0.0 || o[21] != 0.0 || o[22] != 0.0 || o[23] != 0.0;
        }

        private bool InTimeWindow(int nowHHmm, int startHHmm, int endHHmm)
        {
            if (startHHmm > endHHmm)
            {
                return nowHHmm >= startHHmm || nowHHmm < endHHmm;
            }
            else
            {
                return nowHHmm >= startHHmm && nowHHmm < endHHmm;
            }
        }

        private bool InEntryWindow(DateTime exNow)
        {
            if (exNow.DayOfWeek == DayOfWeek.Saturday || exNow.DayOfWeek == DayOfWeek.Sunday) return false;
            int hhmm = exNow.Hour * 100 + exNow.Minute;
            bool inWin = (MyStartTime <= MyEndTime) ? (hhmm >= MyStartTime && hhmm < MyEndTime)
                                                    : (hhmm >= MyStartTime || hhmm < MyEndTime);
            bool inPause = (hhmm >= MyStartPause && hhmm < MyEndPause);
            return inWin && !inPause;
        }

        private int BarsSinceMyEntry() => (_entryBar < 0) ? int.MaxValue : (Bars.CurrentBar - _entryBar);

        private void ResetVWAPAccums()
        {
            _PriceW = 0.0; _ShareW = 0.0; _P2Sum = 0.0;
            _upperPrevBar = _lowerPrevBar = 0.0;
            _upperNow = _lowerNow = 0.0;
        }

        private int ComputeDailySessionId(DateTime exNow)
        {
            var cut = new TimeSpan(ResetAtHH, ResetAtMM, 0);
            DateTime anchor = (exNow.TimeOfDay >= cut) ? exNow.Date : exNow.Date.AddDays(-1);

            if (ResetOnlyWeekdays)
            {
                while (anchor.DayOfWeek == DayOfWeek.Saturday || anchor.DayOfWeek == DayOfWeek.Sunday)
                    anchor = anchor.AddDays(-1);
            }
            return anchor.Year * 10000 + anchor.Month * 100 + anchor.Day;
        }

        private int ComputeWeeklySessionId(DateTime exNow)
        {
            var cut = new TimeSpan(ResetAtHH, ResetAtMM, 0);
            DateTime anchor = (exNow.TimeOfDay >= cut) ? exNow.Date : exNow.Date.AddDays(-1);

            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            var cal = ci.Calendar;
            var rule = System.Globalization.CalendarWeekRule.FirstFourDayWeek;
            var dow = DayOfWeek.Monday;
            int week = cal.GetWeekOfYear(anchor, rule, dow);

            int id = anchor.Year * 100 + week;
            if (_weekAnchor < 0) _weekAnchor = id;
            return id;
        }

        private int CurrentSessionId(DateTime dt)
        {
            switch (dt.DayOfWeek)
            {
                case DayOfWeek.Saturday: return TradeOnSaturday;
                case DayOfWeek.Sunday: return TradeOnSunday;
                case DayOfWeek.Monday: return TradeOnMonday;
                case DayOfWeek.Tuesday: return TradeOnTuesday;
                case DayOfWeek.Wednesday: return TradeOnWednesday;
                case DayOfWeek.Thursday: return TradeOnThursday;
                case DayOfWeek.Friday: return TradeOnFriday;
                default: return -1;
            }
        }

        private string BuildTradeAlertJson(
            string side,
            double entryPrice,
            double stopLossPrice,
            double takeProfitPrice,
            double contracts)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{" +
                "\"symbol\":\"{0}\"," +
                "\"action\":\"{1}\"," +
                "\"time\":\"{2:yyyy-MM-dd HH:mm}\"," +
                "\"entryPrice\":{3:F2}," +
                "\"stopLoss\":{4:F2}," +
                "\"takeProfit\":{5:F2}," +
                "\"contracts\":{6}" +
                "}}",
                Bars.Info.Name,
                side,
                LT(),
                entryPrice,
                stopLossPrice,
                takeProfitPrice,
                contracts
            );
        }

        #region Filters

        //private void UpdateTrendEma(double price)
        //{
        //    int len = Math.Max(1, TrendEmaLen);
        //    double alpha = 2.0 / (len + 1.0);

        //    if (double.IsNaN(_emaTrend) || _emaTrend == 0.0)
        //        _emaTrend = price;
        //    else
        //        _emaTrend = _emaTrend + alpha * (price - _emaTrend);
        //}

        private void UpdateTrendEma(double price)
        {
            int len = Math.Max(1, TrendEmaLen);
            double alpha = 2.0 / (len + 1.0);

            if (double.IsNaN(_emaTrend))
            {
                _emaTrend = price;
                _emaTrendPrev = price;
                return;
            }

            _emaTrendPrev = _emaTrend;
            _emaTrend = _emaTrend + alpha * (price - _emaTrend);
        }


        private double UpdateAtrRegimeAndGetRatio(double atrNow)
        {
            int len = Math.Max(1, AtrRegimeLen);

            _atrRegQ.Enqueue(atrNow);
            _atrRegSum += atrNow;

            if (_atrRegQ.Count > len)
                _atrRegSum -= _atrRegQ.Dequeue();

            double atrBase = (_atrRegQ.Count > 0) ? (_atrRegSum / _atrRegQ.Count) : atrNow;
            if (atrBase <= 0.0) return 1.0;

            return atrNow / atrBase;
        }

        private bool IsTrendOkLong(double atrNow)
        {
            if (!UseTrendFilter) return true;

            double buffer = TrendAtrBufferMult * atrNow;
            return Bars.Close[0] > (_emaTrend + buffer);
        }

        private bool IsTrendOkShort(double atrNow)
        {
            if (!UseTrendFilter) return true;

            double buffer = TrendAtrBufferMult * atrNow;
            return Bars.Close[0] < (_emaTrend - buffer);
        }

        private bool IsVolRegimeOk(double atrRatio)
        {
            if (!UseVolRegimeFilter) return true;
            return atrRatio >= AtrRatioMin && atrRatio <= AtrRatioMax;
        }

        private bool IsBandWidthOk(double atrNow)
        {
            if (!UseBandWidthFilter) return true;
            if (atrNow <= 0.0) return true;

            double bw = (_upperNow - _lowerNow);
            double bwNorm = bw / atrNow;

            return bwNorm >= BandWidthMinAtr && bwNorm <= BandWidthMaxAtr;
        }

        private bool IsDistanceFromVwapOk(double atrNow, double vwapNow)
        {
            if (!UseDistanceFromVWAPFilter) return true;
            if (atrNow <= 0.0) return true;

            double distNorm = Math.Abs(Bars.Close[0] - vwapNow) / atrNow;
            return distNorm <= DistMaxAtr;
        }

        #endregion Filters

        private bool IsVwapTrending(bool forLong, double vwapNow, double vwapPast, double atrNow)
        {
            if (atrNow <= 0) return true; // keď ATR nemáš, neblokuj

            double delta = vwapNow - vwapPast;                 // VWAP zmena za lookback
            double absDeltaAtr = Math.Abs(delta) / atrNow;     // v ATR jednotkách

            // 1) nie flat
            bool notFlat = absDeltaAtr >= VwapTrendMinATR;
            if (!notFlat) return false;

            // 2) ak chceš aj smer
            if (RequireVwapRisingForLong && forLong)
                return delta > 0;

            return true;
        }

        private bool IsWithinMaxLongDistFromVwap(double price, double vwap, double atrNow)
        {
            if (MaxLongDistATR <= 0) return true; // vypnuté
            if (atrNow <= 0) return true;

            double distAtr = Math.Abs(price - vwap) / atrNow;
            return distAtr <= MaxLongDistATR;
        }

        private double GetVwapPast(int lookback)
        {
            if (lookback <= 0) return _vwapPrev;
            if (_vwapHist.Count <= lookback) return double.NaN;

            // Queue enumeruje od najstaršieho -> najnovší
            // chceme element "count-1-lookback"
            int idx = _vwapHist.Count - 1 - lookback;
            int i = 0;
            foreach (var v in _vwapHist)
            {
                if (i == idx) return v;
                i++;
            }
            return double.NaN;
        }


        private void UpdateVWAP(bool accumulateNow)
        {
            double price = (Bars.High[0] + Bars.Low[0] + Bars.Close[0]) / 3.0;
            double ticks = Bars.Volume[0];
            if (ticks <= 0) ticks = Math.Max(1, Bars.Ticks[0]);

            double vwapPre = (_ShareW > 0.0) ? (_PriceW / _ShareW) : price;
            double varPre = (_ShareW > 0.0) ? Math.Max(0.0, (_P2Sum / _ShareW) - vwapPre * vwapPre) : 0.0;
            double devPre = Math.Sqrt(varPre);
            double uPre = vwapPre + NumDevsUp * devPre;
            double lPre = vwapPre - NumDevsDn * devPre;

            if (DebugLogging)
                Output.WriteLine($"{Bars.Time[0]:yyyy-MM-dd HH:mm} | NET VWAP PRE:  Price={price:F6} Vol={ticks:F0} " +
                                 $"PriceW={_PriceW: F1} ShareW={_ShareW:F0} P2Sum={_P2Sum: F1} VWAP={vwapPre: F6} DEV={devPre:F6} U={uPre: F6} L={lPre:F6}");

            if (_okayToCalc)
            {
                _PriceW += price * ticks;
                _ShareW += ticks;
                _P2Sum += ticks * price * price;
            }

            double vwap = (_ShareW > 0.0) ? (_PriceW / _ShareW) : price;
            _vwapNow = vwap;
            double var = (_ShareW > 0.0) ? Math.Max(0.0, (_P2Sum / _ShareW) - vwap * vwap) : 0.0;
            double dev = Math.Sqrt(var);
            _upperNow = vwap + NumDevsUp * dev;
            _lowerNow = vwap - NumDevsDn * dev;

            if (DebugLogging)
                Output.WriteLine($"{Bars.Time[0]:yyyy-MM-dd HH:mm} | NET VWAP POST: PriceW={_PriceW: F1} ShareW={_ShareW: F0} P2Sum={_P2Sum:F1} " +
                                 $"VWAP={vwap:F6} DEV={dev:F6} U(0)={_upperNow:F6} L(0)={_lowerNow:F6}  U(1)={_upperPrevBar:F6} L(1)={_lowerPrevBar:F6}");
        }

        private bool IsReachedMaxDailyDrawdown()
        {
            double eqNow = (InitialCapital > 0 ? InitialCapital : 100000.0) + NetProfit;

            double ddPct = (_sessionStartEquity - eqNow) / _sessionStartEquity * 100.0;

            if (ddPct >= MaxDailyDrawdown)
            {
                _noMoreTradesInSession = true;
                _ddLimitHit = true;

                if (StrategyInfo.MarketPosition != 0)
                {
                    if (CurrentPosition.Side == EMarketPositionSide.Long)
                    {
                        _longClose.Send(_actualTradeContracts);
                    }
                    else
                    {
                        _shortClose.Send(_actualTradeContracts);
                    }
                }

                if (DebugLogging)
                    Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] DAILY DD hit EARLY:  {ddPct: F2}% >= {MaxDailyDrawdown}% → blocking new trades");

                return true;
            }
            else
            {
                if (StrategyInfo.MarketPosition != 0)
                {
                    var currentEquity = StrategyInfo.OpenEquity - StrategyInfo.ClosedEquity;
                    if (currentEquity < 0)
                    {
                        eqNow = eqNow + currentEquity;
                        ddPct = (_sessionStartEquity - eqNow) / _sessionStartEquity * 100.0;
                        if (ddPct >= MaxDailyDrawdown)
                        {
                            _noMoreTradesInSession = true;
                            _ddLimitHit = true;

                            if (CurrentPosition.Side == EMarketPositionSide.Long)
                            {
                                string json = BuildTradeAlertJson("CLOSE", 0, 0, 0, 0);
                                SendTelegramMessage(json);
                                _longClose.Send(_actualTradeContracts);
                            }
                            else
                            {
                                string json = BuildTradeAlertJson("CLOSE", 0, 0, 0, 0);
                                SendTelegramMessage(json);
                                _shortClose.Send(_actualTradeContracts);
                            }

                            if (DebugLogging)
                                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] DAILY DD hit EARLY: {ddPct:F2}% >= {MaxDailyDrawdown}% → blocking new trades");

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsReachedMaxLossDay(bool isLong)
        {
            double eqNow = (InitialCapital > 0 ? InitialCapital : 100000.0) + NetProfit;

            if (isLong)
            {
                if (_actualTradeEntryPrice > Bars.Close[0])
                {
                    eqNow = eqNow - ((_actualTradeEntryPrice - Bars.Close[0]) * _actualTradeContracts);
                }
            }
            else
            {
                if (_actualTradeEntryPrice < Bars.Close[0])
                {
                    eqNow = eqNow - ((Bars.Close[0] - _actualTradeEntryPrice) * _actualTradeContracts);
                }
            }

            double ddPct = (_sessionStartEquity - eqNow) / _sessionStartEquity * 100.0;

            if (ddPct >= MaxPercentLossInSession)
            {
                _noMoreTradesInSession = true;

                if (DebugLogging)
                    Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] DAILY DD hit EARLY:  {ddPct: F2}% >= {MaxPercentLossInSession}% → blocking new trades");

                return true;
            }

            return false;
        }

        // ================= TRAILING STOP - HLAVNÁ LOGIKA =================
        /// <summary>
        /// Vypočíta a aplikuje trailing stop pre otvorenú pozíciu. 
        /// Logika je založená na Adaptrade Builder výstupe: 
        /// 1. Počiatočný stop = protective stop (MMFr × price range)
        /// 2. Aktivácia trailing keď profit > TrailActivationATR × ATR
        /// 3. Trailing = Entry + TrailPct% × (CurrentPrice - Entry)
        /// 4. ATR Floor = stop nikdy nesmie byť bližšie ako ATRFloorMult × ATR od ceny
        /// </summary>
        //private void ManageTrailingStop()
        //{
        //    if (!UseTrailingStop) return;
        //    if (StrategyInfo.MarketPosition == 0) return;
        //    if (_actualTradeContracts <= 0) return;

        //    double bpv = (BPV_Override > 0.0) ? BPV_Override : Bars.Info.BigPointValue;
        //    double currentPrice = Bars.Close[0];

        //    // ===== LONG POSITION =====
        //    if (CurrentPosition.Side == EMarketPositionSide.Long)
        //    {
        //        // Inicializácia pri prvom bare po vstupe
        //        if (_initialStopLong == 0)
        //        {
        //            _initialStopLong = _actualTradeEntryPrice - _actualTradeSL;
        //            _trailStopPriceLong = _initialStopLong;
        //            _atrAtEntry = _atrCurr;

        //            if (DebugLogging)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL INIT LONG: Entry={_actualTradeEntryPrice:F2} InitStop={_initialStopLong:F2} ATR={_atrAtEntry:F4}");
        //        }

        //        // Profit v ATR jednotkách
        //        double profitPoints = currentPrice - _actualTradeEntryPrice;
        //        double profitInATR = (_atrAtEntry > 0) ? profitPoints / _atrAtEntry : 0;

        //        if (DebugLogging)
        //        {
        //            Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL DEBUG: " +
        //                $"Entry={_actualTradeEntryPrice:F2} " +
        //                $"Current={currentPrice:F2} " +
        //                $"ProfitPts={profitPoints:F2} " +
        //                $"ATRatEntry={_atrAtEntry:F4} " +
        //                $"ProfitInATR={profitInATR:F2} " +
        //                $"ActivationThreshold={TrailActivationATR:F2} " +
        //                $"TrailActivated={_trailActivatedLong || _trailActivatedShort}");
        //        }

        //        // Aktivácia trailing stopu
        //        if (!_trailActivatedLong && profitInATR >= TrailActivationATR)
        //        {
        //            _trailActivatedLong = true; 

        //            if (DebugLogging)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL ACTIVATED LONG: ProfitATR={profitInATR:F2} >= Trigger={TrailActivationATR: F2}");
        //        }

        //        // Výpočet trailing stop ceny
        //        if (_trailActivatedLong)
        //        {
        //            // Trail stop = Entry + TrailPct% z aktuálneho profitu
        //            double newTrailStop = _actualTradeEntryPrice + (TrailPct / 100.0) * profitPoints;

        //            // ATR Floor - stop nesmie byť bližšie ako ATRFloorMult × ATR
        //            if (UseATRFloor)
        //            {
        //                double atrFloor = currentPrice - (ATRFloorMult * _atrCurr);
        //                newTrailStop = Math.Min(newTrailStop, atrFloor);
        //            }

        //            // Stop sa môže len posúvať HORE (pre long)
        //            if (newTrailStop > _trailStopPriceLong)
        //            {
        //                _trailStopPriceLong = newTrailStop;

        //                if (DebugLogging)
        //                    Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL UPDATE LONG: NewStop={_trailStopPriceLong:F2} Price={currentPrice:F2} Profit={profitPoints:F2}");
        //            }
        //        }

        //        // Aplikuj stop - vždy použi lepší z (initial, trail)
        //        double activeStop = Math.Max(_initialStopLong, _trailStopPriceLong);
        //        double stopDistance = _actualTradeEntryPrice - activeStop;
        //        double stopLossDollars = stopDistance * bpv * _actualTradeContracts;

        //        if (stopLossDollars > 0)
        //        {
        //            GenerateStopLoss(stopLossDollars);

        //            if (DebugLogging && _trailActivatedLong)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL STOP LONG: StopPrice={activeStop:F2} Distance={stopDistance:F4} SL$={stopLossDollars:F2}");

        //            if (DebugLogging)
        //            {
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] STOP GENERATED: " +
        //                    $"ActiveStop={activeStop: F2} " +
        //                    $"InitialStop={_initialStopLong:F2} " +
        //                    $"TrailStop={_trailStopPriceLong:F2} " +
        //                    $"StopDistance={stopDistance:F4} " +
        //                    $"StopLoss$={stopLossDollars:F2} " +
        //                    $"CurrentPrice={currentPrice:F2}");
        //            }
        //        }


        //    }

        //    // ===== SHORT POSITION =====
        //    else if (CurrentPosition.Side == EMarketPositionSide.Short)
        //    {
        //        // Inicializácia pri prvom bare po vstupe
        //        if (_initialStopShort == 0)
        //        {
        //            _initialStopShort = _actualTradeEntryPrice + _actualTradeSL;
        //            _trailStopPriceShort = _initialStopShort;
        //            _atrAtEntry = _atrCurr;

        //            if (DebugLogging)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL INIT SHORT: Entry={_actualTradeEntryPrice:F2} InitStop={_initialStopShort: F2} ATR={_atrAtEntry:F4}");
        //        }

        //        // Profit v ATR jednotkách
        //        double profitPoints = _actualTradeEntryPrice - currentPrice;
        //        double profitInATR = (_atrAtEntry > 0) ? profitPoints / _atrAtEntry : 0;

        //        // Aktivácia trailing stopu
        //        if (!_trailActivatedShort && profitInATR >= TrailActivationATR)
        //        {
        //            _trailActivatedShort = true;

        //            if (DebugLogging)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL ACTIVATED SHORT: ProfitATR={profitInATR:F2} >= Trigger={TrailActivationATR:F2}");
        //        }

        //        // Výpočet trailing stop ceny
        //        if (_trailActivatedShort)
        //        {
        //            // Trail stop = Entry - TrailPct% z aktuálneho profitu
        //            double newTrailStop = _actualTradeEntryPrice - (TrailPct / 100.0) * profitPoints;

        //            // ATR Floor - stop nesmie byť bližšie ako ATRFloorMult × ATR
        //            if (UseATRFloor)
        //            {
        //                double atrFloor = currentPrice + (ATRFloorMult * _atrCurr);
        //                newTrailStop = Math.Max(newTrailStop, atrFloor);
        //            }

        //            // Stop sa môže len posúvať DOLE (pre short)
        //            if (newTrailStop < _trailStopPriceShort)
        //            {
        //                _trailStopPriceShort = newTrailStop;

        //                if (DebugLogging)
        //                    Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL UPDATE SHORT: NewStop={_trailStopPriceShort:F2} Price={currentPrice:F2} Profit={profitPoints:F2}");
        //            }
        //        }

        //        // Aplikuj stop - vždy použi lepší z (initial, trail)
        //        double activeStop = Math.Min(_initialStopShort, _trailStopPriceShort);
        //        double stopDistance = activeStop - _actualTradeEntryPrice;
        //        double stopLossDollars = stopDistance * bpv * _actualTradeContracts;

        //        if (stopLossDollars > 0)
        //        {
        //            GenerateStopLoss(stopLossDollars);

        //            if (DebugLogging && _trailActivatedShort)
        //                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL STOP SHORT: StopPrice={activeStop:F2} Distance={stopDistance:F4} SL$={stopLossDollars: F2}");
        //        }
        //    }
        //}

        // ================= TRAILING STOP - OPRAVENÁ VERZIA (ako OLDER) =================
        private void ManageTrailingStop()
        {
            if (!UseTrailingStop) return;
            if (StrategyInfo.MarketPosition == 0) return;
            if (_actualTradeContracts <= 0) return;

            double bpv = (BPV_Override > 0.0) ? BPV_Override : Bars.Info.BigPointValue;
            double currentPrice = Bars.Close[0];

            // ===== LONG POSITION =====
            if (CurrentPosition.Side == EMarketPositionSide.Long)
            {
                // Inicializácia pri prvom bare po vstupe
                if (_initialStopLong == 0)
                {
                    _initialStopLong = _actualTradeEntryPrice - _actualTradeSL;
                    _trailStopPriceLong = _initialStopLong;
                    _atrAtEntry = _atrCurr;

                    if (DebugLogging)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL INIT LONG: Entry={_actualTradeEntryPrice: F2} InitStop={_initialStopLong:F2} ATR={_atrAtEntry:F4}");
                }

                // Profit v ATR jednotkách
                double profitPoints = currentPrice - _actualTradeEntryPrice;
                double profitInATR = (_atrAtEntry > 0) ? profitPoints / _atrAtEntry : 0;

                // Aktivácia trailing stopu
                if (!_trailActivatedLong && profitInATR >= TrailActivationATR)
                {
                    _trailActivatedLong = true;

                    if (DebugLogging)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL ACTIVATED LONG: ProfitATR={profitInATR:F2} >= Trigger={TrailActivationATR:F2}");
                }

                // Výpočet trailing stop ceny
                if (_trailActivatedLong)
                {
                    // Trail stop = Entry + TrailPct% z aktuálneho profitu
                    double newTrailStop = _actualTradeEntryPrice + (TrailPct / 100.0) * profitPoints;

                    // ATR Floor - stop nesmie byť bližšie ako ATRFloorMult × ATR
                    if (UseATRFloor)
                    {
                        double atrFloor = currentPrice - (ATRFloorMult * _atrCurr);
                        newTrailStop = Math.Min(newTrailStop, atrFloor);
                    }

                    // Stop sa môže len posúvať HORE (pre long)
                    if (newTrailStop > _trailStopPriceLong)
                    {
                        _trailStopPriceLong = newTrailStop;

                        if (DebugLogging)
                            Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL UPDATE LONG: NewStop={_trailStopPriceLong:F2} Price={currentPrice:F2} Profit={profitPoints:F2}");
                    }
                }

                // Aplikuj stop - vždy použi lepší z (initial, trail)
                // DÔLEŽITÉ: Používame vzdialenosť od ENTRY PRICE, nie od current price! 
                double activeStop = Math.Max(_initialStopLong, _trailStopPriceLong);
                double stopDistance = _actualTradeEntryPrice - activeStop;
                double stopLossDollars = stopDistance * bpv * _actualTradeContracts;

                if (stopLossDollars > 0)
                {
                    GenerateStopLoss(stopLossDollars);

                    if (DebugLogging && _trailActivatedLong)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL STOP LONG:  StopPrice={activeStop:F2} Distance={stopDistance:F4} SL$={stopLossDollars:F2}");
                }
                else
                {
                    double minPerContract = 0.1; //  0.1 je ok ako "almost BE"
                    //if (amountPerContract <= 0) amountPerContract = minPerContract;

                    double amountTotal = minPerContract * _actualTradeContracts;
                    GenerateStopLoss(amountTotal);
                }
            }

            // ===== SHORT POSITION =====
            else if (CurrentPosition.Side == EMarketPositionSide.Short)
            {
                // Inicializácia pri prvom bare po vstupe
                if (_initialStopShort == 0)
                {
                    _initialStopShort = _actualTradeEntryPrice + _actualTradeSL;
                    _trailStopPriceShort = _initialStopShort;
                    _atrAtEntry = _atrCurr;

                    if (DebugLogging)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] TRAIL INIT SHORT: Entry={_actualTradeEntryPrice:F2} InitStop={_initialStopShort:F2} ATR={_atrAtEntry:F4}");
                }

                // Profit v ATR jednotkách
                double profitPoints = _actualTradeEntryPrice - currentPrice;
                double profitInATR = (_atrAtEntry > 0) ? profitPoints / _atrAtEntry : 0;

                // Aktivácia trailing stopu
                if (!_trailActivatedShort && profitInATR >= TrailActivationATR)
                {
                    _trailActivatedShort = true;

                    if (DebugLogging)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] TRAIL ACTIVATED SHORT: ProfitATR={profitInATR:F2} >= Trigger={TrailActivationATR: F2}");
                }

                // Výpočet trailing stop ceny
                if (_trailActivatedShort)
                {
                    // Trail stop = Entry - TrailPct% z aktuálneho profitu
                    double newTrailStop = _actualTradeEntryPrice - (TrailPct / 100.0) * profitPoints;

                    // ATR Floor - stop nesmie byť bližšie ako ATRFloorMult × ATR
                    if (UseATRFloor)
                    {
                        double atrFloor = currentPrice + (ATRFloorMult * _atrCurr);
                        newTrailStop = Math.Max(newTrailStop, atrFloor);
                    }

                    // Stop sa môže len posúvať DOLE (pre short)
                    if (newTrailStop < _trailStopPriceShort)
                    {
                        _trailStopPriceShort = newTrailStop;

                        if (DebugLogging)
                            Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRAIL UPDATE SHORT: NewStop={_trailStopPriceShort:F2} Price={currentPrice:F2} Profit={profitPoints:F2}");
                    }
                }

                // Aplikuj stop - vždy použi lepší z (initial, trail)
                // DÔLEŽITÉ: Používame vzdialenosť od ENTRY PRICE, nie od current price!
                double activeStop = Math.Min(_initialStopShort, _trailStopPriceShort);
                double stopDistance = activeStop - _actualTradeEntryPrice;
                double stopLossDollars = stopDistance * bpv * _actualTradeContracts;

                if (stopLossDollars > 0)
                {
                    GenerateStopLoss(stopLossDollars);

                    if (DebugLogging && _trailActivatedShort)
                        Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] TRAIL STOP SHORT: StopPrice={activeStop:F2} Distance={stopDistance:F4} SL$={stopLossDollars:F2}");
                }
                else
                {
                    double minPerContract = 0.1; //  0.1 je ok ako "almost BE"
                    //if (amountPerContract <= 0) amountPerContract = minPerContract;

                    double amountTotal = minPerContract * _actualTradeContracts;
                    GenerateStopLoss(amountTotal);
                }
            }
        }

        // ================= HLAVNÁ CALCBAR METÓDA =================
        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close) return;

            if (DebugLogging)
                Output.WriteLine("-------------------------------------------------------------");

            if (!_ddLimitHit)
            {
                IsReachedMaxDailyDrawdown();
            }

            // Detekcia zatvorenia pozície - reset trailing stop state
            if (_prevMarketPosition != 0 && StrategyInfo.MarketPosition == 0)
            {
                // Pozícia sa zatvorila - reset trailing stop
                ResetTrailingStopState();

                if (DebugLogging)
                    Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] POSITION CLOSED - Trailing stop reset");
            }
            _prevMarketPosition = StrategyInfo.MarketPosition;

            if (StrategyInfo.MarketPosition != 0)
            {
                _inTrade = true;
                _actualTradeEntryPrice = StrategyInfo.AvgEntryPrice;
            }

            DateTime exNow = Bars.Time[0];
            int csNow = CurrentSessionId(exNow);
            int csPrev = _prevSessionId;

            if (csNow != -1)
            {
                SessionOHLC5(sessionStartTimeC, sessionEndTimeC, _ohlcVals);
            }

            bool sessionChanged = csNow != csPrev;

            if (sessionChanged)
            {
                if (csNow == -1)
                {
                    _prevSessionId = csNow;
                    _okayToCalc = false;
                }
                else
                {
                    if (StrategyInfo.MarketPosition != 0)
                    {
                        string json = BuildTradeAlertJson("CLOSE", 0, 0, 0, 0);
                        SendTelegramMessage(json);
                    }

                    ResetVWAPAccums();
                    _vwapHist.Clear();
                    _vwapPrev = double.NaN;

                    _okayToCalc = true;
                    _prevSessionId = csNow;

                    _stoplossThisSession = 0;
                    _noMoreTradesInSession = false;
                    _isLong = false;
                    _isShort = false;

                    // Reset trailing stop pri novej session
                    ResetTrailingStopState();

                    double eqNow = (InitialCapital > 0 ? InitialCapital : 100000.0) + NetProfit;
                    _sessionStartEquity = eqNow;
                    _sessionPercentLoss = 0.0;
                    _dayMaxDrawdown = 0.0;
                    _ddLimitHit = false;

                    if (DebugLogging)
                        Output.WriteLine($"{exNow:yyyy-MM-dd HH:mm} | SESSION RESET CS={csNow}");
                }
            }

            // ===== ATR (SMA TR) =====
            double prevClose = (Bars.CurrentBar > 0) ? Bars.Close[1] : Bars.Close[0];
            double tr = Math.Max(Bars.High[0], prevClose) - Math.Min(Bars.Low[0], prevClose);
            _trQ.Enqueue(tr); _trSum += tr;
            if (_trQ.Count > Math.Max(1, ATRLen)) _trSum -= _trQ.Dequeue();
            _atrCurr = _trQ.Count > 0 ? (_trSum / _trQ.Count) : tr;

            // ===== VWAP PRE/POST =====
            bool accumulateNow = _okayToCalc && (ResetMode != EResetMode.Never);
            UpdateVWAP(accumulateNow);

            // --- store VWAP history (after UpdateVWAP sets _vwapNow) ---
            _vwapPrev = _vwapNow;
            if (!double.IsNaN(_vwapNow) && !double.IsInfinity(_vwapNow) && _vwapNow != 0.0)
            {
                _vwapHist.Enqueue(_vwapNow);

                int maxKeep = Math.Max(2, VwapTrendLookback + 2);
                while (_vwapHist.Count > maxKeep)
                    _vwapHist.Dequeue();
            }


            bool canUseBands = _okayToCalc && (_ShareW > 0.0);

            //// Filtre
            //UpdateTrendEma(Bars.Close[0]);
            //double atrRatio = UpdateAtrRegimeAndGetRatio(_atrCurr);
            //bool volOk = IsVolRegimeOk(atrRatio);
            //bool compressionOk = IsBandWidthOk(_atrCurr);
            //bool vwapOk = _okayToCalc && _ShareW > 0.0 && _vwapNow > 0 && !double.IsNaN(_vwapNow) && !double.IsInfinity(_vwapNow);
            //bool distOk = vwapOk && IsDistanceFromVwapOk(_atrCurr, _vwapNow);

            //bool trendLongOk = IsTrendOkLong(_atrCurr);
            //bool trendShortOk = IsTrendOkShort(_atrCurr);

            //bool contextOk = volOk && compressionOk && distOk;


            // filtre
            UpdateTrendEma(Bars.Close[0]);

            double atrRatio = UpdateAtrRegimeAndGetRatio(_atrCurr);

            bool volOk = IsVolRegimeOk(atrRatio);
            bool compressionOk = IsBandWidthOk(_atrCurr);
            bool distOk = IsDistanceFromVwapOk(_atrCurr, _vwapNow);

            // Trend filter iba pre LONG
            bool trendLongOk = IsTrendOkLong(_atrCurr);
            bool trendShortOk = true; // SHORT trend filter ignorujeme

            // Long len nad VWAP + rising EMA
            bool emaRising = (!double.IsNaN(_emaTrendPrev) && !double.IsNaN(_emaTrend)) ? (_emaTrend > _emaTrendPrev) : true;
            bool longAboveVwap = (!double.IsNaN(_vwapNow)) ? (Bars.Close[0] > _vwapNow) : true;

            double vwapPast = GetVwapPast(VwapTrendLookback);
            bool vwapTrendOkLong =
                !double.IsNaN(vwapPast) &&
                IsVwapTrending(true, _vwapNow, vwapPast, _atrCurr);

            bool maxLongDistOk =
                IsWithinMaxLongDistFromVwap(Bars.Close[0], _vwapNow, _atrCurr);


            // spoločný context
            bool contextOk = volOk && compressionOk && distOk;



            // VWAP retest logic: 
            // 1. Previous bar closed above its VWAP
            // 2. Current bar retests VWAP (low touches/goes below VWAP)
            // 3. Close of retest candle is at or above VWAP (not below)
            double prevBarVwap = GetVwapPast(1);  // Get previous bar's VWAP from history
            bool vwapRetestCondition = canUseBands && (Bars.CurrentBar > 0) &&
                !double.IsNaN(prevBarVwap) &&                 // Previous VWAP is valid
                (Bars.Close[1] > prevBarVwap) &&              // Previous bar closed above its VWAP
                (Bars.Low[0] <= _vwapNow) &&                  // Retests VWAP (low touches/goes below)
                (Bars.Close[0] >= _vwapNow);                  // Close at or above VWAP (not below)
            
            bool longTrigRaw = vwapRetestCondition;

            bool shortTrigRaw = canUseBands && (Bars.CurrentBar > 0) &&
                                (Bars.Close[0] < _lowerNow) && (Bars.Close[1] >= _lowerPrevBar);

            //bool longTrig = longTrigRaw && contextOk && trendLongOk;
            //bool shortTrig = shortTrigRaw && contextOk && trendShortOk;
            //bool longTrig = longTrigRaw  && contextOk && trendLongOk && longAboveVwap && emaRising;
            bool longTrig = longTrigRaw
                && contextOk
                && trendLongOk
                && longAboveVwap
                && emaRising
                && maxLongDistOk
                && vwapTrendOkLong;

            bool shortTrig = shortTrigRaw && contextOk; // bez trendShortOk (ignorujeme trend filter pre short)


            double bandWidth = _upperNow - _lowerNow;
            double bandWidthAtr = (_atrCurr > 0) ? bandWidth / _atrCurr : double.NaN;

            if (DebugLogging && (longTrigRaw || shortTrigRaw))
            {
                Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] COMPDBG bandW={bandWidth:F4} ATR={_atrCurr:F4} " +
                                 $"bandW/Atr={bandWidthAtr:F2} min={BandWidthMinAtr: F2} compOk={compressionOk}");
            }

            int now = ToHHmm(exNow);
            bool inTradeWindow = InTimeWindow(now, MyStartTime, MyEndTime);
            bool allowEntry = inTradeWindow && !_ddLimitHit;

            // ===== DayPatterns =====
            bool dpLongOk = longTrig && (DayPatterns(MyPtnLY, _ohlcVals) && !DayPatterns(MyPtnLN, _ohlcVals));
            bool dpShortOk = shortTrig && (DayPatterns(MyPtnSY, _ohlcVals) && !DayPatterns(MyPtnSN, _ohlcVals));

            if (DebugLogging)
            {
                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] LONG 1= {longTrig} LONG 2= {DayPatterns(MyPtnLY, _ohlcVals)} LONG 3= {!DayPatterns(MyPtnLN, _ohlcVals)}");
                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] SHORT 1= {shortTrig} SHORT 2= {DayPatterns(MyPtnSY, _ohlcVals)} SHORT 3= {!DayPatterns(MyPtnSN, _ohlcVals)}");
            }

            // ===== Sizing =====
            double SL_pts = _atrCurr * ATRMult;
            double PT_pts = SL_pts * RRR_tp;
            double bpv = (BPV_Override > 0.0) ? BPV_Override : Bars.Info.BigPointValue;

            double equity = (InitialCapital > 0 ? InitialCapital : 100000.0) + NetProfit;
            double risk = Math.Max(0.0, RiskPercent) * equity;
            double SL_ = SL_pts * bpv;
            int vCon = (SL_ > 0.0) ? (int)Math.Floor(risk / SL_) : 0;
            double vConDouble = risk / SL_;

            bool hasPos = (StrategyInfo.MarketPosition != 0);

            if (DebugLogging && (!DebugOnlyInWindow || allowEntry))
            {
                Output.WriteLine(
                    $"{LT():yyyy-MM-dd HH:mm}] allowEntry={allowEntry} longTrig={longTrig} shortTrig={shortTrig} vCon={vCon}, " +
                    $"SL_pts={SL_pts:F2}, SL_${SL_: F2}, Eq={equity:F2}, ATR={_atrCurr:F2}, " +
                    $"U(0)={_upperNow:F2}, L(0)={_lowerNow:F2}, U(1)={_upperPrevBar: F2}, L(1)={_lowerPrevBar: F2}, " +
                    $"BPV={bpv:F6}, sessionId={csNow}, OkayToCalc={_okayToCalc}, Mode={ResetMode}"
                );
                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] TRIGGERS: longRaw={longTrigRaw} shortRaw={shortTrigRaw} long={longTrig} short={shortTrig}");
                Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] FILTERS: atrRatio={atrRatio:F2} volOk={volOk} compOk={compressionOk} distOk={distOk} trendL={trendLongOk} trendS={trendShortOk} contextOk={contextOk} vwap={_vwapNow:F2}");
            }

            if (!Parity_Test_Mode)
            {
                bool isLong = CurrentPosition.Side == EMarketPositionSide.Long;
                bool isShort = CurrentPosition.Side == EMarketPositionSide.Short;

                if (Bars.Status == EBarState.Close)
                {
                    if (allowEntry && vCon > 0 && canUseBands)
                    {
                        if (longTrig && dpLongOk && !isLong)
                        {
                            var maxLossReached = false;

                            if (!maxLossReached)
                            {
                                _longNext.Send(vCon);
                                _entryBar = Bars.CurrentBar;
                                _actualTradeSL = SL_pts;  // Ukladáme v POINTS, nie v dolároch
                                _actualTradeTP = PT_pts;
                                _isLong = true;
                                _isShort = false;
                                _actualTradeContracts = vCon;

                                // Reset trailing stop pre nový obchod
                                ResetTrailingStopState();

                                _entryPrice = Bars.Close[0];
                                _origSlPts = _actualTradeSL;
                                _beArmed = false;

                                string json = BuildTradeAlertJson(
                                    "LONG",
                                    Bars.Close[0],
                                    Bars.Close[0] - SL_pts,
                                    Bars.Close[0] + PT_pts,
                                    Math.Round(vConDouble, 2)
                                );

                                SendTelegramMessage(json);
                                Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] ---------- SENT LONG {vCon} @ {Bars.Close[0]: F2}");
                            }
                        }

                        if (shortTrig && dpShortOk && !isShort)
                        {
                            var maxLossReached = false;

                            if (!maxLossReached)
                            {
                                _shortNext.Send(vCon);
                                _entryBar = Bars.CurrentBar;
                                _actualTradeSL = SL_pts;  // Ukladáme v POINTS, nie v dolároch
                                _actualTradeTP = PT_pts;
                                _isShort = true;
                                _isLong = false;
                                _actualTradeContracts = vCon;

                                // Reset trailing stop pre nový obchod
                                ResetTrailingStopState();

                                _entryPrice = Bars.Close[0];
                                _origSlPts = _actualTradeSL;
                                _beArmed = false;

                                string json = BuildTradeAlertJson(
                                    "SHORT",
                                    Bars.Close[0],
                                    Bars.Close[0] + SL_pts,
                                    Bars.Close[0] - PT_pts,
                                    Math.Round(vConDouble, 2)
                                );

                                SendTelegramMessage(json);
                                Output.WriteLine($"{LT():yyyy-MM-dd HH: mm}] ---------- SENT SHORT {vCon} @ {Bars.Close[0]:F2}");
                            }
                        }

                        if (longTrig && dpLongOk && isLong && DebugLogging)
                            Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] LONG trig=TRUE ale už som LONG -> nevstupujem");

                        if (shortTrig && dpShortOk && isShort && DebugLogging)
                            Output.WriteLine($"{LT():yyyy-MM-dd HH:mm}] SHORT trig=TRUE ale už som SHORT -> nevstupujem");
                    }
                }

                // ===== TRAILING STOP MANAGEMENT =====
                if (StrategyInfo.MarketPosition != 0)
                {
                    if (UseTrailingStop)
                    {
                        // Použiť novú trailing stop logiku
                        ManageTrailingStop();
                    }
                    else
                    {
                        // Pôvodná logika - fixed stop
                        GenerateStopLoss(_actualTradeSL * bpv * _actualTradeContracts);
                    }

                    // Profit target ostáva rovnaký
                    if (RRR_tp > 0.0)
                    {
                        GenerateProfitTarget(_actualTradeTP * bpv * _actualTradeContracts);
                    }
                }

                if (ID == 0 || Bars.Time[0].DayOfWeek == DayOfWeek.Friday)
                    GenerateExitOnClose();
            }

            _upperPrevBar = _upperNow;
            _lowerPrevBar = _lowerNow;
        }
    }
}