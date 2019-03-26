﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Crypto.Core.Common;
using Crypto.Core.Strategies;
using CryptoMarketClient.Common;

namespace CryptoMarketClient.Strategies {
    [Serializable]
    public abstract class TickerStrategyBase  : StrategyBase {
        public TickerStrategyBase() {
        }
        
        public double BuyLevel { get; set; }
        public double SellLevel { get; set; }

        protected virtual OrderBookEntry GetAvailableToBuy(double maxBuy) {
            OrderBookEntry res = new OrderBookEntry();
            double maxDeposit = ApplyFee(MaxActualBuyDeposit);
            lock(Ticker.OrderBook.Asks) {
                foreach(OrderBookEntry entry in Ticker.OrderBook.Asks) {
                    if(maxBuy != 0 && entry.Value > maxBuy)
                        break;
                    res.Value = entry.Value;
                    double amount = maxDeposit / entry.Value;
                    if(amount > entry.Amount)
                        amount = entry.Amount;
                    res.Amount += amount;
                    maxDeposit -= amount * entry.Value;
                    if(maxDeposit == 0)
                        break;
                }
            }
            return res;
        }

        protected virtual OrderBookEntry GetAvailableToSell(double minSell) {
            OrderBookEntry res = new OrderBookEntry();
            double maxDeposit = ApplyFee(MaxActualSellDeposit);
            res.Amount = maxDeposit;
            lock(Ticker.OrderBook.Bids) {
                foreach(OrderBookEntry entry in Ticker.OrderBook.Bids) {
                    if(minSell != 0 && entry.Value < minSell)
                        break;
                    res.Value = entry.Value;
                    res.Amount += entry.Amount;
                    maxDeposit -= entry.Amount;
                    if(maxDeposit <= 0)
                        break;
                }
            }
            res.Amount = Math.Min(res.Amount, ApplyFee(MaxActualSellDeposit));
            return res;
        }
        
        public override bool InitializeCore() {
            Ticker = DataProvider.GetExchange(TickerInfo.Exchange).GetTicker(TickerInfo.Ticker);
            if(Ticker == null)
                return false;
            return true;
        }

        

        BuySellStrategyState state;
        public BuySellStrategyState State {
            get { return state; }
            set {
                if(State == value)
                    return;
                BuySellStrategyState prev = State;
                state = value;
                OnStateChanged(prev);
            }
        }

        public override string StateText => State.ToString();

        protected virtual void OnStateChanged(BuySellStrategyState prev) {
            Log(LogType.Log, string.Format("{0} -> {1}", prev, State), 0, 0, StrategyOperation.StateChanged);
        }

        protected void Buy() {
            OrderBookEntry e = GetAvailableToBuy(BuyLevel);
            TradingResult res = MarketBuy(e.Value, e.Amount);
            if(res == null)
                return;

            BoughtTotal += res.Total;
            MaxActualBuyDeposit -= res.Total + CalcFee(res.Total);
            MaxActualSellDeposit += res.Amount;
        }

        protected double CalcFee(double total) {
            return total * Ticker.Fee / 100.0;
        }

        protected void Sell() {
            OrderBookEntry e = GetAvailableToSell(SellLevel);
            TradingResult res = MarketSell(e.Value, e.Amount);
            if(res == null)
                return;

            SoldTotal += res.Amount;
            MaxActualBuyDeposit += res.Total;
            MaxActualSellDeposit -= res.Amount + CalcFee(res.Total);
        }

        Ticker ticker;
        [XmlIgnore]
        public Ticker Ticker {
            get {
                if(ticker == null && TickerInfo != null)
                    ticker = GetTicker();
                return ticker;
            }
            set {
                if(Ticker == value)
                    return;
                ticker = value;
                OnTickerChanged();
            }
        }

        public override void Assign(StrategyBase from) {
            base.Assign(from);
            TickerStrategyBase st = from as TickerStrategyBase;
            if(st == null)
                return;
            TickerInfo = st.TickerInfo;
        }

        double maxActualDeposit = -1;
        public double MaxActualDeposit {
            get {
                if(maxActualDeposit < 0)
                    maxActualDeposit = GetMaxActualDepositByPriority();
                return maxActualDeposit;
            }
        }
        public double ApplyFee(double deposit) { 
            return (100.0 - Ticker.Fee) / 100.0 * deposit;
        }
        public bool CanBuyMore {
            get { return MaxActualBuyDeposit > 0.001; }
        }
        public bool CanSellMore {
            get { return MaxActualSellDeposit > 0.001; }
        }
        double GetMaxActualDepositByPriority() {
            if(Manager == null || Account == null || Ticker == null)
                return MaxAllowedDeposit;
            double res = Account.GetBalance(Ticker.BaseCurrency);
            foreach(StrategyBase s in Manager.Strategies) {
                if(s == this)
                    break;
                res -= s.MaxAllowedDeposit;
            }
            return res;
        }

        protected override void OnMaxAllowedDepositChanged() {
            base.OnMaxAllowedDepositChanged();
        }

        TickerNameInfo tickerInfo;
        public TickerNameInfo TickerInfo {
            get { return tickerInfo; }
            set {
                if(TickerInfo == value)
                    return;
                tickerInfo = value;
                OnTickerInfoChanged();
            }
        }

        void OnTickerInfoChanged() {
            if(TickerInfo == null) {
                Ticker = null;
                return;
            }
            Ticker t = GetTicker();
            if(t != null)
                Ticker = t;
        }

        protected virtual Ticker GetTicker() {
            if(DataProvider == null)
                return null;
            Exchange e = DataProvider.GetExchange(TickerInfo.Exchange);
            if(e == null)
                return null;
            return e.GetTicker(TickerInfo.Ticker);
        }

        void OnTickerChanged() {
            if(Ticker == null) {
                TickerInfo = null;
                return;
            }
            TickerInfo = new TickerNameInfo() { Exchange = Ticker.Exchange.Type, Ticker = Ticker.Name };
        }

        public virtual TickerInputInfo CreateTickerInputInfo() {
            return new TickerInputInfo() { Exchange = TickerInfo.Exchange, TickerName = TickerInfo.Ticker, OrderBook = true, TradeHistory = true, Ticker = Ticker };
        }

        public override StrategyInputInfo CreateInputInfo() {
            StrategyInputInfo s = new StrategyInputInfo();
            TickerInputInfo t = CreateTickerInputInfo();
            t.Ticker = Ticker;
            s.Tickers.Add(t);
            return s;
        }

        public override bool Start() {
            if(!base.Start())
                return false;

            StrategyInputInfo inputInfo = CreateInputInfo();
            bool res = DataProvider.Connect(inputInfo);
            if(res)
                Ticker = inputInfo.Tickers[0].Ticker;

            if(MaxActualBuyDeposit == -1)
                MaxActualBuyDeposit = MaxActualDeposit;

            return res;
        }

        public override bool Stop() {
            if(!base.Stop())
                return false;
            return DataProvider.Disconnect(CreateInputInfo());
        }

        protected TradingResult AddDemoTradingResult(double rate, double amount, OrderType type) {
            TradingResult res = new TradingResult() { Amount = amount, Type = type, Date = DateTime.Now, OrderNumber = -1, Total = rate * amount };
            res.Trades.Add(new TradeEntry() { Amount = amount, Date = DateTime.Now, Id = "demo", Rate = rate, Total = rate * amount, Type = type });
            return res;
        }

        protected virtual TradingResult MarketBuy(double rate, double amount) {
            TradingResult res = null;
            if(!DemoMode) {
                res = Ticker.Buy(rate, amount);
                if(res == null)
                    Log(LogType.Error, "", rate, amount, StrategyOperation.MarketBuy);
                return res;
            }
            else {
                res = AddDemoTradingResult(rate, amount, OrderType.Buy);
            }
            TradeHistory.Add(res);
            Log(LogType.Success, "", rate, amount, StrategyOperation.MarketBuy);
            return res;
        }
        protected virtual TradingResult MarketSell(double rate, double amount) {
            TradingResult res = null;
            if(!DemoMode) {
                res = Ticker.Sell(rate, amount);
                if(res == null)
                    Log(LogType.Error, "", rate, amount, StrategyOperation.MarketSell);
                return res;
            }
            else {
                res = AddDemoTradingResult(rate, amount, OrderType.Sell);
            }
            TradeHistory.Add(res);
            Log(LogType.Success, "", rate, amount, StrategyOperation.MarketSell);
            return res;
        }
        protected virtual TradingResult PlaceBid(double rate, double amount) {
            TradingResult res = null;
            if(!DemoMode) {
                res = Ticker.Buy(rate, amount);
                if(res == null) 
                    Log(LogType.Error, "", rate, amount, StrategyOperation.LimitBuy);
                return res;
            }
            else {
                res = AddDemoTradingResult(rate, amount, OrderType.Buy);
            }
            Log(LogType.Success, "", rate, amount, StrategyOperation.LimitBuy);
            return res;
        }
        protected virtual TradingResult PlaceAsk(double rate, double amount) {
            TradingResult res = null;
            if(!DemoMode) {
                res = Ticker.Sell(rate, amount);
                if(res == null)
                    Log(LogType.Error, "", rate, amount, StrategyOperation.LimitSell);
                return res;
            }
            else
                res = AddDemoTradingResult(rate, amount, OrderType.Sell);
            Log(LogType.Error, "", rate, amount, StrategyOperation.LimitSell);
            return res;
        }
        public override List<StrategyValidationError> Validate() {
            List<StrategyValidationError> list = base.Validate();
            CheckTickerSpecified(list);
            CheckAccountMatchExchange(list);
            return list;
        }
        void CheckTickerSpecified(List<StrategyValidationError> list) {
            if(TickerInfo == null || string.IsNullOrEmpty(TickerInfo.Ticker))
                list.Add(new StrategyValidationError() { DataObject = this, Description = string.Format("Ticker not specified.", GetTickerName()), PropertyName = "Ticker", Value = GetTickerName() });
            else if(Ticker == null)
                list.Add(new StrategyValidationError() { DataObject = this, Description = string.Format("Ticker {0} not found.", GetTickerName()), PropertyName = "Ticker", Value = GetTickerName() });
        }

        string GetTickerName() {
            return TickerInfo == null ? "[empty]" : TickerInfo.ToString();    
        }
        protected void CheckAccountMatchExchange(List<StrategyValidationError> list) {
            if(AccountId == Guid.Empty || TickerInfo == null)
                return;
            if(TickerInfo.Exchange != Account.Exchange.Type)
                list.Add(new StrategyValidationError() { DataObject = this, Description = string.Format("Ticker's exchange {0} does not match account's {1}.", TickerInfo.Exchange, Account.Exchange.Type), PropertyName = "Ticker", Value = GetTickerName() });
        }
    }

    public enum BuySellStrategyState {
        WaitingForBuy,
        WaitingForSell
    }
}
