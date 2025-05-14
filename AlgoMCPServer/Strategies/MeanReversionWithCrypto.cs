namespace AlgoMCPServer.Strategies {
    using Alpaca.Markets;

    public readonly record struct PositionInfo(decimal Quantity, long IntegerQuantity, decimal MarketValue);
    public readonly record struct AllocatedAccountInfo(decimal AllocatedBuyingPower, decimal AllocatedEquity, long Multiplier);

    internal sealed class MeanReversionWithCrypto : IStrategy {
        private readonly string API_KEY;

        private readonly string API_SECRET;

        private readonly string symbol;
        private readonly decimal accountAllocation; // Renamed from equityPercentage

        private readonly string symbolWithoutSlashes;

        private const decimal scale = 200;

        private IAlpacaCryptoDataClient alpacaCryptoDataClient;

        private IAlpacaTradingClient alpacaTradingClient;

        private IAlpacaStreamingClient alpacaStreamingClient;

        private IAlpacaCryptoStreamingClient alpacaCryptoStreamingClient;

        private Guid lastTradeId = Guid.NewGuid();

        private bool lastTradeOpen;

        private readonly List<Decimal> closingPrices = [];

        private bool isAssetShortable;

        private TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

        private CancellationToken cancellationToken;

        public MeanReversionWithCrypto(String key, String secret, String symbol, decimal accountAllocation, CancellationToken cancellationToken = default) {
            API_KEY = key;
            API_SECRET = secret;
            this.symbol = symbol;
            this.accountAllocation = accountAllocation;
            this.cancellationToken = cancellationToken;
            symbolWithoutSlashes = symbol.Replace("/", string.Empty);

            // Register callback for when cancellation is requested
            _ = cancellationToken.Register(() => {
                tcs.SetResult(0);
            });

            alpacaTradingClient = Environments.Paper.GetAlpacaTradingClient(new SecretKey(API_KEY, API_SECRET));

            alpacaCryptoDataClient = Environments.Paper.GetAlpacaCryptoDataClient(new SecretKey(API_KEY, API_SECRET));

            // Connect to Alpaca's websocket and listen for updates on our orders.
            alpacaStreamingClient = Environments.Paper.GetAlpacaStreamingClient(new SecretKey(API_KEY, API_SECRET));

            // Connect to Alpaca's websocket and listen for price updates.
            alpacaCryptoStreamingClient = Environments.Live.GetAlpacaCryptoStreamingClient(new SecretKey(API_KEY, API_SECRET));
        }

        public async Task Run() {
            await SetupClients();

            DateTime closingTime = await GetNextMarketClosingTimeAsync();

            // Initialize closing prices and get the last bar
            var lastBar = await InitializeClosingPricesAsync(closingTime);

            // Get an order started based on the most recent historical price update
            await HandleMinuteBar(lastBar);

            // Trigger orders at each minute bar update
            await ConfigureMinuteBarSubscription();

            // Wait until cancellation is requested
            await tcs.Task;

            // Perform cleanup when cancellation is requested
            if (cancellationToken.IsCancellationRequested) {
                Console.WriteLine("Cancellation requested. Closing positions and disconnecting...");
                await ClosePositionAtMarket();
                await alpacaCryptoStreamingClient.DisconnectAsync();
            }

        }

        public void Dispose() {
            alpacaTradingClient?.Dispose();
            alpacaCryptoDataClient?.Dispose();
            alpacaStreamingClient?.Dispose();
            alpacaCryptoStreamingClient?.Dispose();
        }

        private async Task<DateTime> GetNextMarketClosingTimeAsync() {
            var requestTodaysCalendar = CalendarRequest.GetForSingleDay(DateOnly.FromDateTime(DateTime.Today));

            var calendars = (await alpacaTradingClient
                .ListIntervalCalendarAsync(requestTodaysCalendar))
                .ToList();
            var todaysCalendar = calendars.First();

            var calendarDate = todaysCalendar.GetTradingDate();
            var closingTime = todaysCalendar.GetTradingCloseTimeUtc();

            return new DateTime(calendarDate.Year, calendarDate.Month, calendarDate.Day,
                               closingTime.Hour, closingTime.Minute, closingTime.Second);
        }

        private async Task<IBar> InitializeClosingPricesAsync(DateTime closingTime) {

            var requestMinuteBars = new HistoricalCryptoBarsRequest(symbol, BarTimeFrame.Minute);
            var bars = await alpacaCryptoDataClient.ListHistoricalBarsAsync(requestMinuteBars);

            var lastBars = bars.Items.TakeLast(21).Take(20);
            var lastBar = bars.Items.Last();

            foreach (var bar in lastBars) {
                if (bar.TimeUtc >= DateTime.UtcNow.AddHours(-1)) {
                    closingPrices.Add(bar.Close);
                }
            }

            Console.WriteLine($"Initialized with {closingPrices.Count} historical price points");
            return lastBar;
        }

        private async Task SetupClients() {
            await alpacaStreamingClient.ConnectAndAuthenticateAsync();

            ConfigureTradeUpdateEvent();

            // First, cancel any existing orders so they don't impact our buying power.
            await alpacaTradingClient.CancelAllOrdersAsync();

            isAssetShortable = (await alpacaTradingClient.GetAssetAsync(symbol)).Shortable;

            await alpacaCryptoStreamingClient.ConnectAndAuthenticateAsync();
            Console.WriteLine("Alpaca streaming client opened.");
        }

        private void ConfigureTradeUpdateEvent() {
            alpacaStreamingClient.OnTradeUpdate += trade => {
                if (trade.Order.OrderId != lastTradeId) {
                    return;
                }

                switch (trade.Event) {
                    case TradeEvent.Fill:
                        Console.WriteLine("Trade filled.");
                        lastTradeOpen = false;
                        break;
                    case TradeEvent.Rejected:
                        Console.WriteLine("Trade rejected.");
                        lastTradeOpen = false;
                        break;
                    case TradeEvent.Canceled:
                        Console.WriteLine("Trade canceled.");
                        lastTradeOpen = false;
                        break;
                }
            };
        }

        private async Task ConfigureMinuteBarSubscription() {
            var subscription = alpacaCryptoStreamingClient.GetMinuteBarSubscription(symbol);

            subscription.Received += async bar => {
                Console.WriteLine($"Got data {bar}");
                // If the market's close to closing, exit position and stop trading.
                var minutesUntilClose = (await alpacaTradingClient.GetClockAsync()).NextCloseUtc - DateTime.UtcNow;
                if (minutesUntilClose.TotalMinutes < 15) {
                    Console.WriteLine("Reached the end of trading window.");
                    await ClosePositionAtMarket();
                    await alpacaCryptoStreamingClient.DisconnectAsync();
                } else {
                    // Decide whether to buy or sell and submit orders.
                    await HandleMinuteBar(bar);
                }
            };

            await alpacaCryptoStreamingClient.SubscribeAsync(subscription);
        }

        // Waits until the clock says the market is open.
        // Note: if you wanted the algorithm to start trading right at market open, you would instead
        // use the method restClient.GetCalendarAsync() to get the open time and schedule execution
        // of your code based on that. However, this algorithm does not start trading until at least
        // 20 minutes after the market opens.
        private async Task AwaitMarketOpen() {
            while (!(await alpacaTradingClient.GetClockAsync()).IsOpen) {
                await Task.Delay(60000);
            }
        }

        // Determine whether our position should grow or shrink and submit orders.
        private async Task HandleMinuteBar(IBar bar) {
            UpdateClosingPrices(bar.Close);

            if (closingPrices.Count < 20) {
                Console.WriteLine($"Need 20 previous prices but only have {closingPrices.Count}. Waiting on more data...");
                return;
            }

            Console.WriteLine($"Average: {closingPrices.Average()}; Last Close: {bar.Close}");

            var deviation = closingPrices.Average() - bar.Close;

            await CancelOpenTradeIfNecessary();

            var accountInfo = await GetAccountInfo();
            var positionInfo = await GetCurrentPosition();

            if (deviation <= 0) {
                await ExecuteShortingStrategy(bar.Close, deviation, accountInfo, positionInfo);
            } else {
                await ExecuteLongingStrategy(bar.Close, deviation, accountInfo, positionInfo);
            }
        }

        private void UpdateClosingPrices(decimal closePrice) {
            closingPrices.Add(closePrice);
            if (closingPrices.Count > 20) {
                closingPrices.RemoveAt(0);
            }
            Console.WriteLine(closingPrices);
        }

        private async Task CancelOpenTradeIfNecessary() {
            if (lastTradeOpen) {
                await alpacaTradingClient.CancelOrderAsync(lastTradeId);
            }
        }

        private async Task<AllocatedAccountInfo> GetAccountInfo() {
            var account = await alpacaTradingClient.GetAccountAsync();

            // Use null coalescing operator to ensure non-nullable decimals
            var rawBuyingPower = account.BuyingPower * 0.10M ?? 0M;
            var rawEquity = account.Equity ?? 0M;
            var multiplier = (Int64)account.Multiplier;

            // Apply account allocation percentage
            var allocatedBuyingPower = rawBuyingPower * this.accountAllocation;
            var allocatedEquity = rawEquity * this.accountAllocation;

            return new AllocatedAccountInfo(allocatedBuyingPower, allocatedEquity, multiplier);
        }

        // Update GetCurrentPosition method to properly handle nullable decimals
        private async Task<PositionInfo> GetCurrentPosition() {
            decimal quantity = 0;
            long integerQuantity = 0;
            decimal marketValue = 0;

            try {
                var position = await alpacaTradingClient.GetPositionAsync(symbolWithoutSlashes);
                quantity = position.Quantity;
                integerQuantity = (long)Math.Floor(quantity);
                marketValue = position.MarketValue ?? 0M;
            }
            catch (Exception) {
                Console.WriteLine("No current position found");
            }

            return new PositionInfo(quantity, integerQuantity, marketValue);
        }

        private async Task ExecuteShortingStrategy(
            decimal currentPrice,
            decimal deviation,
            AllocatedAccountInfo accountInfo,
            PositionInfo positionInfo) {

            if (positionInfo.IntegerQuantity > 0) {
                // There is an existing long position we need to dispose of first
                Console.WriteLine($"Removing {positionInfo.MarketValue:C2} long position.");
                await SubmitOrder(positionInfo.IntegerQuantity, currentPrice, OrderSide.Sell);
                return;
            }

            var portfolioShare = CalculatePortfolioShare(deviation, currentPrice);
            var targetPositionValue = -1M * accountInfo.AllocatedEquity * accountInfo.Multiplier * portfolioShare;
            var amountToShort = targetPositionValue - positionInfo.MarketValue;

            if (amountToShort < 0) {
                await ExpandShortPosition(currentPrice, amountToShort, accountInfo.AllocatedBuyingPower);
            } else {
                await ShrinkShortPosition(currentPrice, amountToShort, positionInfo.IntegerQuantity);
            }
        }

        // Update ExpandShortPosition to handle nullable decimal parameters
        private async Task ExpandShortPosition(decimal price, decimal amountToShort, decimal buyingPower) {
            amountToShort = Math.Abs(amountToShort);
            if (amountToShort > buyingPower) {
                amountToShort = buyingPower;
            }

            var qty = (Int64)(amountToShort / price);
            if (isAssetShortable) {
                Console.WriteLine($"Adding {qty * price:C2} to short position.");
                await SubmitOrder(qty, price, OrderSide.Sell);
            } else {
                Console.WriteLine("Unable to place short order - asset is not shortable.");
            }
        }

        // Update ShrinkShortPosition to ensure consistent decimal handling
        private async Task ShrinkShortPosition(decimal price, decimal amountToShort, long positionQuantity) {
            var qty = (Int64)(amountToShort / price);
            if (qty > -1 * positionQuantity) {
                qty = -1 * positionQuantity;
            }

            Console.WriteLine($"Removing {qty * price:C2} from short position");
            await SubmitOrder(qty, price, OrderSide.Buy);
        }

        private async Task ExecuteLongingStrategy(
            decimal currentPrice,
            decimal deviation,
            AllocatedAccountInfo accountInfo,
            PositionInfo positionInfo) {

            var portfolioShare = CalculatePortfolioShare(deviation, currentPrice);
            var targetPositionValue = accountInfo.AllocatedEquity * accountInfo.Multiplier * portfolioShare;
            var amountToLong = targetPositionValue - positionInfo.MarketValue;

            if (positionInfo.IntegerQuantity < 0) {
                // There is an existing short position we need to dispose of first
                Console.WriteLine($"Removing {positionInfo.MarketValue:C2} short position.");
                await SubmitOrder(-positionInfo.IntegerQuantity, currentPrice, OrderSide.Buy);
                return;
            }

            if (amountToLong > 0) {
                await ExpandLongPosition(currentPrice, amountToLong, accountInfo.AllocatedBuyingPower);
            } else {
                await ShrinkLongPosition(currentPrice, amountToLong, positionInfo.IntegerQuantity);
            }
        }

        private async Task ExpandLongPosition(decimal price, decimal amountToLong, decimal buyingPower) {
            if (amountToLong > buyingPower) {
                amountToLong = buyingPower;
            }

            var qty = (Int32)(amountToLong / price);
            await SubmitOrder(qty, price, OrderSide.Buy);
            Console.WriteLine($"Adding {qty * price:C2} to long position.");
        }

        private async Task ShrinkLongPosition(decimal price, decimal amountToLong, long positionQuantity) {
            amountToLong = Math.Abs(amountToLong);
            var qty = (Int64)(amountToLong / price);
            if (qty > positionQuantity) {
                qty = positionQuantity;
            }

            if (isAssetShortable) {
                await SubmitOrder(qty, price, OrderSide.Sell);
                Console.WriteLine($"Removing {qty * price:C2} from long position");
            } else {
                Console.WriteLine("Unable to place short order - asset is not shortable.");
            }
        }

        private decimal CalculatePortfolioShare(decimal deviation, decimal price) {
            return Math.Abs(deviation) / price * scale;
        }

        // Submit an order if quantity is not zero.
        private async Task SubmitOrder(Int64 quantity, Decimal price, OrderSide side) {
            if (quantity == 0) {
                return;
            }
            try {
                var order = new NewOrderRequest(symbol, quantity, side, OrderType.Limit, TimeInForce.Gtc) {
                    LimitPrice = price
                };
                var orderResponse = await alpacaTradingClient.PostOrderAsync(order);

                lastTradeId = orderResponse.OrderId;
                lastTradeOpen = true;
            }
            catch (Exception e) {
                Console.WriteLine("Warning: " + e.Message); //-V5621
            }
        }

        private async Task ClosePositionAtMarket() {
            try {
                var positionQuantity = (await GetCurrentPosition()).Quantity;
                Console.WriteLine($"Closing {positionQuantity} qty position at market price.");
                NewOrderRequest order;
                if (positionQuantity > 0) {
                    order = new NewOrderRequest(symbol, OrderQuantity.Fractional(positionQuantity), OrderSide.Sell, OrderType.Market, TimeInForce.Gtc);
                    await alpacaTradingClient.PostOrderAsync(order);
                } else if (positionQuantity < 0) {
                    order = new NewOrderRequest(symbol, OrderQuantity.Fractional(Math.Abs(positionQuantity)), OrderSide.Buy, OrderType.Market, TimeInForce.Gtc);
                    await alpacaTradingClient.PostOrderAsync(order);
                }
            }
            catch (Exception e) //-V3163 //-V5606
            {
                Console.WriteLine($"{e} No current position found");
            }
        }
    }

}
