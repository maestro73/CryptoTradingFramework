﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoMarketClient.Exchanges.Bittrex {
    public class BittrexIncrementalUpdateDataProvider : IIncrementalUpdateDataProvider {
        public void Update(Ticker ticker, IncrementalUpdateInfo info) {
            for(int i = 0; i < info.BidsUpdates.Count; i++) {
                string[] item = info.BidsUpdates[i];
                ticker.OrderBook.ApplyIncrementalUpdate(OrderBookEntryType.Bid, item[1], item[2]);
            }
            for(int i = 0; i < info.AsksUpdates.Count; i++) {
                string[] item = info.AsksUpdates[i];
                ticker.OrderBook.ApplyIncrementalUpdate(OrderBookEntryType.Ask, item[1], item[2]);
            }
            for(int i = 0; i < info.TradeUpdates.Count; i++) {
                string[] item = info.TradeUpdates[i];
                TradeInfoItem trade = new TradeInfoItem(null, ticker) {
                        Type = item[1][0] == 'S' ? TradeType.Sell : TradeType.Buy, RateString = item[2], AmountString = item[3], Time = new DateTime(Convert.ToInt64(item[4])).ToLocalTime()
                };
                ticker.TradeHistory.Insert(0, trade);
                CandleStickChartHelper.UpdateVolumes(ticker.CandleStickData, trade, ticker.CandleStickPeriodMin);
            }
            ticker.OnApplyIncrementalUpdate();
        }
        public void ApplySnapshot(JObject jObject, Ticker ticker) {
            ticker.OrderBook.BeginUpdate();
            try {
                ticker.OrderBook.Clear();
                OrderBook orderBook = ticker.OrderBook;

                JArray jbids = jObject.Value<JArray>("Z");
                JArray jasks = jObject.Value<JArray>("S");

                List<OrderBookEntry> entries = orderBook.Asks;
                List<OrderBookEntry> entriesInverted = orderBook.AsksInverted;
                for(int i = 0; i < jasks.Count; i++) {
                    JObject item = (JObject)jasks[i];
                    entries.Add(new OrderBookEntry() { ValueString = item.Value<string>("R"), AmountString = item.Value<string>("Q") });
                    if(entriesInverted != null)
                        entriesInverted.Insert(0, new OrderBookEntry() { ValueString = item.Value<string>("R"), AmountString = item.Value<string>("Q") });
                }
                entries = orderBook.Bids;
                for(int i = 0; i < jbids.Count; i++) {
                    JObject item = (JObject)jbids[i];
                    entries.Add(new OrderBookEntry() { ValueString = item.Value<string>("R"), AmountString = item.Value<string>("Q") });
                }
            }
            finally {
                ticker.OrderBook.IsDirty = false;
                ticker.OrderBook.EndUpdate();
            }

            ticker.TradeHistory.Clear();
            JArray jtrades = jObject.Value<JArray>("f");
            foreach(JObject item in jtrades) {
                TradeInfoItem t = new TradeInfoItem(null, ticker);
                t.AmountString = item.Value<string>("Q");
                t.RateString = item.Value<string>("P");
                t.Time = new DateTime(Convert.ToInt64(item.Value<string>("T"))).ToLocalTime();
                t.TimeString = t.Time.ToLongTimeString();
                t.Total = t.Rate * t.Amount;
                t.Type = (item.Value<string>("OT")) == "BUY" ? TradeType.Buy : TradeType.Sell;
                ticker.TradeHistory.Add(t);
            }

            if(ticker.HasTradeHistorySubscribers) {
                ticker.RaiseTradeHistoryChanged(new TradeHistoryChangedEventArgs() { NewItems = ticker.TradeHistory });
            }
        }
    }
}
