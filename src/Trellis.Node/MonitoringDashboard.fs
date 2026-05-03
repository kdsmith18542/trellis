namespace Trellis.Node

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System.Text.Json
open Trellis.Core
open SimpleTypes
open Monitoring

/// Web-based monitoring dashboard service
type MonitoringDashboardService(logger: ILogger<MonitoringDashboardService>, blockchainState: GrpcService.IBlockchainState) =
    inherit BackgroundService()
    
    let mutable monitoringService: IMonitoringService option = None
    let mutable isRunning = false
    
    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        async {
            logger.LogInformation("Starting Monitoring Dashboard Service...")
            
            // Initialize monitoring service
            monitoringService <- Some(upcast InMemoryMonitoringService())
            
            // Start web server
            this.StartWebServer(stoppingToken)
            
            logger.LogInformation("Monitoring Dashboard Service started.")
        } |> Async.StartAsTask :> Task
    
    member private this.StartWebServer(cancellationToken: CancellationToken) =
        Task.Run(fun () ->
            try
                let builder = WebApplication.CreateBuilder()
                
                // Configure services
                builder.Services.AddCors(fun options ->
                    options.AddDefaultPolicy(fun policy ->
                        policy.AllowAnyOrigin()
                             .AllowAnyMethod()
                             .AllowAnyHeader()
                        |> ignore) |> ignore
                )
                
                let app = builder.Build()
                
                // Configure CORS
                app.UseCors() |> ignore
                
                // API endpoints
                this.ConfigureEndpoints(app)
                
                // Start the web server
                app.RunAsync(cancellationToken) |> ignore
                
            with
            | ex -> logger.LogError(ex, "Error starting web server")
        ) |> ignore
    
    member private this.ConfigureEndpoints(app: WebApplication) =
        // Health check endpoint
        app.MapGet("/health", fun (ctx: HttpContext) ->
            task {
                let healthChecks = 
                    match monitoringService with
                    | Some service -> service.GetHealthChecks()
                    | None -> []
                
                let isHealthy = HealthCheckManager.isSystemHealthy healthChecks
                let status = if isHealthy then "healthy" else "unhealthy"
                
                let response = {| Status = status; Timestamp = DateTime.UtcNow; Checks = healthChecks |}
                
                ctx.Response.StatusCode <- if isHealthy then 200 else 503
                ctx.Response.ContentType <- "application/json"
                do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
            } :> Task
        ) |> ignore
        
        // Metrics endpoint
        app.MapGet("/metrics", fun (ctx: HttpContext) ->
            task {
                match monitoringService with
                | Some service ->
                    let metrics = service.GetMetrics("")
                    let response = {| Metrics = metrics |}
                    
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                | None ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Monitoring service not available")
            } :> Task
        ) |> ignore

        // Chain state endpoint (for wallet UI)
        app.MapGet("/api/chain-state", fun (ctx: HttpContext) ->
            task {
                try
                    let cs = blockchainState.GetChainState()
                    let (BlockHeight h) = cs.CurrentHeight
                    let lastHashHex =
                        match cs.LastBlockHash with | Hash b -> System.Convert.ToHexString(b)
                    let (Balance ts) = cs.TotalSupply
                    let baseFee = blockchainState.GetBaseFeePerGas()
                    let response = {| currentHeight = h; lastBlockHash = lastHashHex; totalSupply = ts; baseFeePerGas = baseFee |}
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                with ex ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize({| error = ex.Message |}))
            } :> Task
        ) |> ignore

        // Fee stats endpoint (base fee, last burn and tips)
        app.MapGet("/api/fee-stats", fun (ctx: HttpContext) ->
            task {
                try
                    let baseFee = blockchainState.GetBaseFeePerGas()
                    let burned = blockchainState.GetLastBlockBurn()
                    let tips = blockchainState.GetLastBlockTips()
                    let response = {| baseFeePerGas = baseFee; lastBurn = burned; lastTips = tips |}
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                with ex ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize({| error = ex.Message |}))
            } :> Task
        ) |> ignore
        
        // Alerts endpoint
        app.MapGet("/alerts", fun (ctx: HttpContext) ->
            task {
                match monitoringService with
                | Some service ->
                    let alerts = service.GetAlerts(None)
                    let response = {| Alerts = alerts |}
                    
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                | None ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Monitoring service not available")
            } :> Task
        ) |> ignore
        
        // Dashboard endpoint
        app.MapGet("/dashboard/{id}", fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues.["id"] :?> string
                match monitoringService with
                | Some service ->
                    match service.GetDashboard(id) with
                    | Some dashboard ->
                        ctx.Response.ContentType <- "application/json"
                        do! ctx.Response.WriteAsync(JsonSerializer.Serialize(dashboard))
                    | None ->
                        ctx.Response.StatusCode <- 404
                        do! ctx.Response.WriteAsync("Dashboard not found")
                | None ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Monitoring service not available")
            } :> Task
        ) |> ignore
        
        // Performance metrics endpoint
        app.MapGet("/performance", fun (ctx: HttpContext) ->
            task {
                match monitoringService with
                | Some service ->
                    let perfService = service :?> InMemoryMonitoringService
                    let metrics = perfService.GetPerformanceMetricsHistory(100)
                    let response = {| PerformanceMetrics = metrics |}
                    
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                | None ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Monitoring service not available")
            } :> Task
        ) |> ignore
        
        // Blockchain metrics endpoint
        app.MapGet("/blockchain", fun (ctx: HttpContext) ->
            task {
                match monitoringService with
                | Some service ->
                    let blockchainService = service :?> InMemoryMonitoringService
                    let metrics = blockchainService.GetBlockchainMetricsHistory(100)
                    let response = {| BlockchainMetrics = metrics |}
                    
                    ctx.Response.ContentType <- "application/json"
                    do! ctx.Response.WriteAsync(JsonSerializer.Serialize(response))
                | None ->
                    ctx.Response.StatusCode <- 500
                    do! ctx.Response.WriteAsync("Monitoring service not available")
            } :> Task
        ) |> ignore
        
        // Dashboard HTML endpoint
        app.MapGet("/", fun (ctx: HttpContext) ->
            task {
                let html = this.GenerateDashboardHTML()
                ctx.Response.ContentType <- "text/html"
                do! ctx.Response.WriteAsync(html)
            } :> Task
        ) |> ignore

        // Send transaction endpoint (wallet -> node)
        app.MapPost("/api/send-transaction", fun (ctx: HttpContext) ->
            task {
                try
                    use sr = new System.IO.StreamReader(ctx.Request.Body)
                    let! body = sr.ReadToEndAsync()
                    let json = System.Text.Json.JsonDocument.Parse(body).RootElement
                    let fromHex = json.GetProperty("from").GetString()
                    let toHex = json.GetProperty("to").GetString()
                    let amount = json.GetProperty("amount").GetUInt64()
                    let fee = json.GetProperty("fee").GetUInt64()
                    let nonce = json.GetProperty("nonce").GetUInt64()
                    let pubKeyHex = json.GetProperty("publicKey").GetString()
                    let sigHex = json.GetProperty("signature").GetString()

                    let tx: Transaction = {
                        From = Address (System.Convert.FromHexString(fromHex))
                        To = Address (System.Convert.FromHexString(toHex))
                        Amount = Balance amount
                        Fee = Balance fee
                        Nonce = Nonce nonce
                        Signature = Signature (System.Convert.FromHexString(sigHex))
                        Hash = Hash [||]
                        GasLimit = 21000UL
                        GasPrice = 20UL
                        Data = Array.empty
                        Metadata = Map.ofList [ ("pubkey", pubKeyHex) ]
                        MaxFeePerGas = None
                        MaxPriorityFeePerGas = None
                        TransactionType = 0uy
                    }
                    let hash = Trellis.Core.SimpleCrypto.transactionHashSimple tx
                    let tx' = { tx with Hash = hash }

                    // Try mempool first
                    let admit = Trellis.Node.Mempool.acceptTransaction tx' (fun a -> blockchainState.GetAccount a)
                    match admit with
                    | Ok h ->
                        ctx.Response.ContentType <- "application/json"
                        do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = true; hash = System.Convert.ToHexString(match h with | Hash b -> b) |}))
                    | Error err ->
                        // Fallback to direct submission
                        let ok = blockchainState.SubmitTransaction(tx')
                        ctx.Response.ContentType <- "application/json"
                        if ok then
                            do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = true; hash = System.Convert.ToHexString(match tx'.Hash with | Hash b -> b); warning = err |}))
                        else
                            ctx.Response.StatusCode <- 400
                            do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = false; error = err |}))
                with ex ->
                    ctx.Response.StatusCode <- 400
                    do! ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize({| success = false; error = ex.Message |}))
            } :> Task
        ) |> ignore
    
    member private this.GenerateDashboardHTML() =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Trellis Blockchain Monitoring Dashboard</title>
            <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
            <style>
                body { font-family: Arial, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5; }
                .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 10px; margin-bottom: 20px; }
                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; }
                .card { background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
                .metric { display: flex; justify-content: space-between; align-items: center; margin: 10px 0; }
                .metric-value { font-size: 24px; font-weight: bold; color: #667eea; }
                .status { padding: 5px 10px; border-radius: 20px; color: white; font-size: 12px; }
                .status.healthy { background-color: #4CAF50; }
                .status.unhealthy { background-color: #f44336; }
                .chart-container { position: relative; height: 300px; }
                .refresh-btn { background: #667eea; color: white; border: none; padding: 10px 20px; border-radius: 5px; cursor: pointer; }
                .refresh-btn:hover { background: #5a6fd8; }
            </style>
        </head>
        <body>
            <div class="header">
                <h1>🚀 Trellis Blockchain Monitoring Dashboard</h1>
                <p>Real-time monitoring of blockchain performance, health, and metrics</p>
                <button class="refresh-btn" onclick="refreshData()">🔄 Refresh Data</button>
            </div>
            
            <div class="grid">
                <div class="card">
                    <h3>📊 System Health</h3>
                    <div id="health-status">Loading...</div>
                </div>
                
                <div class="card">
                    <h3>⚡ Performance Metrics</h3>
                    <div class="metric">
                        <span>CPU Usage:</span>
                        <span class="metric-value" id="cpu-usage">-</span>
                    </div>
                    <div class="metric">
                        <span>Memory Usage:</span>
                        <span class="metric-value" id="memory-usage">-</span>
                    </div>
                    <div class="metric">
                        <span>Response Time:</span>
                        <span class="metric-value" id="response-time">-</span>
                    </div>
                </div>
                
                <div class="card">
                    <h3>⛓️ Blockchain Metrics</h3>
                    <div class="metric">
                        <span>Block Height:</span>
                        <span class="metric-value" id="block-height">-</span>
                    </div>
                    <div class="metric">
                        <span>Transactions:</span>
                        <span class="metric-value" id="transaction-count">-</span>
                    </div>
                    <div class="metric">
                        <span>Network Size:</span>
                        <span class="metric-value" id="network-size">-</span>
                    </div>
                </div>
                
                <div class="card">
                    <h3>🚨 Active Alerts</h3>
                    <div id="alerts-list">Loading...</div>
                </div>
                
                <div class="card">
                    <h3>📈 Performance Chart</h3>
                    <div class="chart-container">
                        <canvas id="performanceChart"></canvas>
                    </div>
                </div>
                
                <div class="card">
                    <h3>⛓️ Blockchain Chart</h3>
                    <div class="chart-container">
                        <canvas id="blockchainChart"></canvas>
                    </div>
                </div>
            </div>
            
            <script>
                let performanceChart, blockchainChart;
                
                async function fetchData(url) {
                    try {
                        const response = await fetch(url);
                        return await response.json();
                    } catch (error) {
                        console.error('Error fetching data:', error);
                        return null;
                    }
                }
                
                async function refreshData() {
                    // Fetch health status
                    const health = await fetchData('/health');
                    if (health) {
                        document.getElementById('health-status').innerHTML = 
                            `<span class="status ${health.status}">${health.status.toUpperCase()}</span>`;
                    }
                    
                    // Fetch performance metrics
                    const perf = await fetchData('/performance');
                    if (perf && perf.PerformanceMetrics && perf.PerformanceMetrics.length > 0) {
                        const latest = perf.PerformanceMetrics[perf.PerformanceMetrics.length - 1];
                        document.getElementById('cpu-usage').textContent = latest.CpuUsage.toFixed(1) + '%';
                        document.getElementById('memory-usage').textContent = latest.MemoryUsage.toFixed(1) + '%';
                        document.getElementById('response-time').textContent = latest.ResponseTime.toFixed(2) + 'ms';
                        
                        updatePerformanceChart(perf.PerformanceMetrics);
                    }
                    
                    // Fetch blockchain metrics
                    const blockchain = await fetchData('/blockchain');
                    if (blockchain && blockchain.BlockchainMetrics && blockchain.BlockchainMetrics.length > 0) {
                        const latest = blockchain.BlockchainMetrics[blockchain.BlockchainMetrics.length - 1];
                        document.getElementById('block-height').textContent = latest.BlockHeight;
                        document.getElementById('transaction-count').textContent = latest.TransactionCount.toLocaleString();
                        document.getElementById('network-size').textContent = latest.NetworkSize.toString();
                        
                        updateBlockchainChart(blockchain.BlockchainMetrics);
                    }
                    
                    // Fetch alerts
                    const alerts = await fetchData('/alerts');
                    if (alerts && alerts.Alerts) {
                        const activeAlerts = alerts.Alerts.filter(alert => alert.Status === 'Firing');
                        const alertsHtml = activeAlerts.length > 0 
                            ? activeAlerts.map(alert => 
                                `<div class="status ${alert.Severity.toLowerCase()}">${alert.Name}: ${alert.Message}</div>`
                              ).join('')
                            : '<div style="color: green;">✅ No active alerts</div>';
                        document.getElementById('alerts-list').innerHTML = alertsHtml;
                    }
                }
                
                function updatePerformanceChart(data) {
                    const ctx = document.getElementById('performanceChart').getContext('2d');
                    if (performanceChart) performanceChart.destroy();
                    
                    performanceChart = new Chart(ctx, {
                        type: 'line',
                        data: {
                            labels: data.map((_, i) => i),
                            datasets: [{
                                label: 'CPU Usage %',
                                data: data.map(d => d.CpuUsage),
                                borderColor: 'rgb(102, 126, 234)',
                                tension: 0.1
                            }, {
                                label: 'Memory Usage %',
                                data: data.map(d => d.MemoryUsage),
                                borderColor: 'rgb(118, 75, 162)',
                                tension: 0.1
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false
                        }
                    });
                }
                
                function updateBlockchainChart(data) {
                    const ctx = document.getElementById('blockchainChart').getContext('2d');
                    if (blockchainChart) blockchainChart.destroy();
                    
                    blockchainChart = new Chart(ctx, {
                        type: 'line',
                        data: {
                            labels: data.map((_, i) => i),
                            datasets: [{
                                label: 'Transaction Count',
                                data: data.map(d => d.TransactionCount),
                                borderColor: 'rgb(76, 175, 80)',
                                tension: 0.1
                            }, {
                                label: 'Network Size',
                                data: data.map(d => d.NetworkSize),
                                borderColor: 'rgb(255, 152, 0)',
                                tension: 0.1
                            }]
                        },
                        options: {
                            responsive: true,
                            maintainAspectRatio: false
                        }
                    });
                }
                
                // Initial load and auto-refresh
                refreshData();
                setInterval(refreshData, 30000); // Refresh every 30 seconds
            </script>
        </body>
        </html>
        """
    
    /// Get the monitoring service
    member this.GetMonitoringService() : IMonitoringService option =
        monitoringService
    
    /// Record a metric
    member this.RecordMetric(metric: Metric) =
        match monitoringService with
        | Some service -> service.RecordMetric(metric)
        | None -> logger.LogWarning("Monitoring service not available")
    
    /// Create an alert
    member this.CreateAlert(alert: Alert) =
        match monitoringService with
        | Some service -> service.CreateAlert(alert)
        | None -> logger.LogWarning("Monitoring service not available")
    
    /// Record performance metrics
    member this.RecordPerformanceMetrics(perf: PerformanceMetrics) =
        match monitoringService with
        | Some service -> service.RecordPerformanceMetrics(perf)
        | None -> logger.LogWarning("Monitoring service not available")
    
    /// Record blockchain metrics
    member this.RecordBlockchainMetrics(blockchain: BlockchainMetrics) =
        match monitoringService with
        | Some service -> service.RecordBlockchainMetrics(blockchain)
        | None -> logger.LogWarning("Monitoring service not available")

/// Monitoring data collector service
type MonitoringDataCollector(logger: ILogger<MonitoringDataCollector>, dashboardService: MonitoringDashboardService) =
    inherit BackgroundService()
    
    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        async {
            logger.LogInformation("Starting Monitoring Data Collector...")
            
            while not stoppingToken.IsCancellationRequested do
                try
                    // Collect system performance metrics
                    this.CollectPerformanceMetrics()
                    
                    // Collect blockchain metrics
                    this.CollectBlockchainMetrics()
                    
                    // Check for alerts
                    this.CheckAlerts()
                    
                    // Wait before next collection
                    do! Async.Sleep(TimeSpan.FromSeconds(10.0))
                
                with
                | ex -> logger.LogError(ex, "Error in monitoring data collector")
            
            logger.LogInformation("Monitoring Data Collector stopped.")
        } |> Async.StartAsTask :> Task
    
    member private this.CollectPerformanceMetrics() =
        try
            // Simulate performance metrics collection
            let random = Random()
            let cpuUsage = random.NextDouble() * 100.0
            let memoryUsage = random.NextDouble() * 100.0
            let diskUsage = random.NextDouble() * 100.0
            let networkIn = random.NextDouble() * 1000.0
            let networkOut = random.NextDouble() * 1000.0
            let responseTime = random.NextDouble() * 100.0
            let throughput = random.NextDouble() * 1000.0
            let errorRate = random.NextDouble() * 5.0
            
            let perfMetrics = MetricsCollector.createPerformanceMetrics cpuUsage memoryUsage diskUsage networkIn networkOut responseTime throughput errorRate
            
            dashboardService.RecordPerformanceMetrics(perfMetrics)
            
            // Create metrics
            let cpuMetric = MetricsCollector.createGauge "cpu_usage" cpuUsage Map.empty
            let memoryMetric = MetricsCollector.createGauge "memory_usage" memoryUsage Map.empty
            let responseTimeMetric = MetricsCollector.createGauge "response_time" responseTime Map.empty
            
            dashboardService.RecordMetric(cpuMetric)
            dashboardService.RecordMetric(memoryMetric)
            dashboardService.RecordMetric(responseTimeMetric)
            
        with
        | ex -> logger.LogError(ex, "Error collecting performance metrics")
    
    member private this.CollectBlockchainMetrics() =
        try
            // Simulate blockchain metrics collection
            let random = Random()
            let blockHeight = BlockHeight(uint64 (random.Next(100000, 200000)))
            let txCount = uint64 (random.Next(1000, 10000))
            let pendingTxs = uint64 (random.Next(0, 100))
            let blockTime = random.NextDouble() * 10.0
            let hashRate = random.NextDouble() * 1000000.0
            let difficulty = random.NextDouble() * 1000.0
            let stakedAmount = Balance(uint64 (random.Next(1000000, 10000000)))
            let validatorCount = uint32 (random.Next(10, 100))
            let networkSize = uint32 (random.Next(100, 1000))
            
            let blockchainMetrics = MetricsCollector.createBlockchainMetrics blockHeight txCount pendingTxs blockTime hashRate difficulty stakedAmount validatorCount networkSize
            
            dashboardService.RecordBlockchainMetrics(blockchainMetrics)
            
        with
        | ex -> logger.LogError(ex, "Error collecting blockchain metrics")
    
    member private this.CheckAlerts() =
        try
            // Simulate alert checking
            let random = Random()
            if random.NextDouble() < 0.1 then // 10% chance of alert
                let alert = AlertManager.createAlert (Guid.NewGuid().ToString()) "High CPU Usage" Warning "CPU usage is above 80%" "system"
                
                dashboardService.CreateAlert(alert)
                logger.LogWarning("Alert created: {AlertName}", alert.Name)
            
        with
        | ex -> logger.LogError(ex, "Error checking alerts")
