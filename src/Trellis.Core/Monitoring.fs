namespace Trellis.Core

open System
open System.Collections.Generic
open SimpleTypes

/// Enterprise monitoring framework for Trellis blockchain
module Monitoring =

    /// Monitoring metric types
    type MetricType =
        | Counter
        | Gauge
        | Histogram
        | Summary

    /// Monitoring metric
    type Metric = {
        Name: string
        Type: MetricType
        Value: float
        Labels: Map<string, string>
        Timestamp: DateTime
        Description: string option
    }

    /// Alert severity levels
    type AlertSeverity =
        | Info
        | Warning
        | Critical
        | Emergency

    /// Alert status
    type AlertStatus =
        | Firing
        | Resolved
        | Pending

    /// Monitoring alert
    type Alert = {
        Id: string
        Name: string
        Severity: AlertSeverity
        Status: AlertStatus
        Message: string
        Description: string
        Source: string
        CreatedAt: DateTime
        UpdatedAt: DateTime
        Labels: Map<string, string>
        Annotations: Map<string, string>
    }

    /// Dashboard widget types
    type WidgetType =
        | LineChart
        | BarChart
        | PieChart
        | Gauge
        | Table
        | Stat
        | Heatmap
        | Logs

    /// Dashboard widget
    type Widget = {
        Id: string
        Title: string
        Type: WidgetType
        Position: (int * int)
        Size: (int * int)
        DataSource: string
        Query: string
        RefreshInterval: TimeSpan
        Config: Map<string, obj>
    }

    /// Dashboard configuration
    type Dashboard = {
        Id: string
        Name: string
        Description: string
        Widgets: Widget list
        CreatedAt: DateTime
        UpdatedAt: DateTime
        Tags: string list
        IsPublic: bool
    }

    /// System health status
    type HealthStatus =
        | Healthy
        | Degraded
        | Unhealthy
        | Unknown

    /// System health check
    type HealthCheck = {
        Name: string
        Status: HealthStatus
        Message: string
        Duration: TimeSpan
        LastChecked: DateTime
        Dependencies: string list
    }

    /// Performance metrics
    type PerformanceMetrics = {
        CpuUsage: float
        MemoryUsage: float
        DiskUsage: float
        NetworkIn: float
        NetworkOut: float
        ResponseTime: float
        Throughput: float
        ErrorRate: float
        Timestamp: DateTime
    }

    /// Blockchain-specific metrics
    type BlockchainMetrics = {
        BlockHeight: BlockHeight
        TransactionCount: uint64
        PendingTransactions: uint64
        BlockTime: float
        HashRate: float
        Difficulty: float
        StakedAmount: Balance
        ValidatorCount: uint32
        NetworkSize: uint32
        Timestamp: DateTime
    }

    /// Monitoring service interface
    type IMonitoringService =
        abstract member RecordMetric: Metric -> unit
        abstract member GetMetrics: string -> Metric list
        abstract member CreateAlert: Alert -> unit
        abstract member GetAlerts: AlertStatus option -> Alert list
        abstract member UpdateAlert: string -> AlertStatus -> unit
        abstract member GetHealthChecks: unit -> HealthCheck list
        abstract member RecordPerformanceMetrics: PerformanceMetrics -> unit
        abstract member RecordBlockchainMetrics: BlockchainMetrics -> unit
        abstract member GetDashboard: string -> Dashboard option
        abstract member CreateDashboard: Dashboard -> unit
        abstract member UpdateDashboard: Dashboard -> unit

    /// In-memory monitoring service implementation
    type InMemoryMonitoringService() =
        let mutable metrics: List<Metric> = List<Metric>()
        let mutable alerts: List<Alert> = List<Alert>()
        let mutable healthChecks: List<HealthCheck> = List<HealthCheck>()
        let mutable dashboards: List<Dashboard> = List<Dashboard>()
        let mutable performanceMetrics: List<PerformanceMetrics> = List<PerformanceMetrics>()
        let mutable blockchainMetrics: List<BlockchainMetrics> = List<BlockchainMetrics>()

        interface IMonitoringService with
            member this.RecordMetric(metric: Metric) =
                metrics.Add(metric)

            member this.GetMetrics(name: string) =
                metrics
                |> Seq.filter (fun m -> m.Name = name)
                |> Seq.toList

            member this.CreateAlert(alert: Alert) =
                alerts.Add(alert)

            member this.GetAlerts(status: AlertStatus option) =
                match status with
                | Some s -> alerts |> Seq.filter (fun a -> a.Status = s) |> Seq.toList
                | None -> alerts |> Seq.toList

            member this.UpdateAlert(alertId: string) (status: AlertStatus) =
                let alert = alerts |> Seq.tryFind (fun a -> a.Id = alertId)
                match alert with
                | Some a ->
                    let updatedAlert = { a with Status = status; UpdatedAt = DateTime.UtcNow }
                    let index = alerts.IndexOf(a)
                    if index >= 0 then
                        alerts.[index] <- updatedAlert
                | None -> ()

            member this.GetHealthChecks() =
                healthChecks |> Seq.toList

            member this.RecordPerformanceMetrics(perf: PerformanceMetrics) =
                performanceMetrics.Add(perf)
                // Keep only last 1000 records
                if performanceMetrics.Count > 1000 then
                    performanceMetrics.RemoveAt(0)

            member this.RecordBlockchainMetrics(blockchain: BlockchainMetrics) =
                blockchainMetrics.Add(blockchain)
                // Keep only last 1000 records
                if blockchainMetrics.Count > 1000 then
                    blockchainMetrics.RemoveAt(0)

            member this.GetDashboard(id: string) =
                dashboards |> Seq.tryFind (fun d -> d.Id = id)

            member this.CreateDashboard(dashboard: Dashboard) =
                dashboards.Add(dashboard)

            member this.UpdateDashboard(dashboard: Dashboard) =
                let index = dashboards |> Seq.tryFindIndex (fun d -> d.Id = dashboard.Id)
                match index with
                | Some i -> dashboards.[i] <- dashboard
                | None -> dashboards.Add(dashboard)

        /// Get performance metrics history
        member this.GetPerformanceMetricsHistory(limit: int) =
            performanceMetrics
            |> Seq.skip (max 0 (performanceMetrics.Count - limit))
            |> Seq.toList

        /// Get blockchain metrics history
        member this.GetBlockchainMetricsHistory(limit: int) =
            blockchainMetrics
            |> Seq.skip (max 0 (blockchainMetrics.Count - limit))
            |> Seq.toList

        /// Get all dashboards
        member this.GetAllDashboards() =
            dashboards |> Seq.toList

        /// Add health check
        member this.AddHealthCheck(healthCheck: HealthCheck) =
            healthChecks.Add(healthCheck)

    /// Monitoring dashboard builder
    module DashboardBuilder =

        /// Create a new dashboard
        let createDashboard (id: string) (name: string) (description: string) =
            {
                Id = id
                Name = name
                Description = description
                Widgets = []
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                Tags = []
                IsPublic = false
            }

        /// Add widget to dashboard
        let addWidget (widget: Widget) (dashboard: Dashboard) =
            { dashboard with 
                Widgets = widget :: dashboard.Widgets
                UpdatedAt = DateTime.UtcNow
            }

        /// Create a line chart widget
        let createLineChart (id: string) (title: string) (dataSource: string) (query: string) (position: int * int) (size: int * int) =
            {
                Id = id
                Title = title
                Type = WidgetType.LineChart
                Position = position
                Size = size
                DataSource = dataSource
                Query = query
                RefreshInterval = TimeSpan.FromSeconds(30.0)
                Config = Map.empty
            }

        /// Create a gauge widget
        let createGauge (id: string) (title: string) (dataSource: string) (query: string) (position: int * int) (size: int * int) =
            {
                Id = id
                Title = title
                Type = WidgetType.Gauge
                Position = position
                Size = size
                DataSource = dataSource
                Query = query
                RefreshInterval = TimeSpan.FromSeconds(10.0)
                Config = Map.empty
            }

        /// Create a stat widget
        let createStat (id: string) (title: string) (dataSource: string) (query: string) (position: int * int) (size: int * int) =
            {
                Id = id
                Title = title
                Type = WidgetType.Stat
                Position = position
                Size = size
                DataSource = dataSource
                Query = query
                RefreshInterval = TimeSpan.FromSeconds(5.0)
                Config = Map.empty
            }

        /// Create a table widget
        let createTable (id: string) (title: string) (dataSource: string) (query: string) (position: int * int) (size: int * int) =
            {
                Id = id
                Title = title
                Type = WidgetType.Table
                Position = position
                Size = size
                DataSource = dataSource
                Query = query
                RefreshInterval = TimeSpan.FromSeconds(15.0)
                Config = Map.empty
            }

    /// Alert manager
    module AlertManager =

        /// Create a new alert
        let createAlert (id: string) (name: string) (severity: AlertSeverity) (message: string) (source: string) =
            {
                Id = id
                Name = name
                Severity = severity
                Status = Firing
                Message = message
                Description = message
                Source = source
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow
                Labels = Map.empty
                Annotations = Map.empty
            }

        /// Resolve an alert
        let resolveAlert (alert: Alert) =
            { alert with 
                Status = Resolved
                UpdatedAt = DateTime.UtcNow
            }

        /// Check if alert is critical
        let isCritical (alert: Alert) =
            alert.Severity = Critical || alert.Severity = Emergency

        /// Get alerts by severity
        let getAlertsBySeverity (severity: AlertSeverity) (alerts: Alert list) =
            alerts |> List.filter (fun a -> a.Severity = severity)

    /// Health check manager
    module HealthCheckManager =

        /// Create a health check
        let createHealthCheck (name: string) (status: HealthStatus) (message: string) (duration: TimeSpan) =
            {
                Name = name
                Status = status
                Message = message
                Duration = duration
                LastChecked = DateTime.UtcNow
                Dependencies = []
            }

        /// Check if system is healthy
        let isSystemHealthy (healthChecks: HealthCheck list) =
            healthChecks |> List.forall (fun hc -> hc.Status = Healthy)

        /// Get unhealthy health checks
        let getUnhealthyChecks (healthChecks: HealthCheck list) =
            healthChecks |> List.filter (fun hc -> hc.Status <> Healthy)

    /// Metrics collector
    module MetricsCollector =

        /// Create a counter metric
        let createCounter (name: string) (value: float) (labels: Map<string, string>) =
            {
                Name = name
                Type = MetricType.Counter
                Value = value
                Labels = labels
                Timestamp = DateTime.UtcNow
                Description = None
            }

        /// Create a gauge metric
        let createGauge (name: string) (value: float) (labels: Map<string, string>) =
            {
                Name = name
                Type = MetricType.Gauge
                Value = value
                Labels = labels
                Timestamp = DateTime.UtcNow
                Description = None
            }

        /// Create a histogram metric
        let createHistogram (name: string) (value: float) (labels: Map<string, string>) =
            {
                Name = name
                Type = MetricType.Histogram
                Value = value
                Labels = labels
                Timestamp = DateTime.UtcNow
                Description = None
            }

        /// Create performance metrics
        let createPerformanceMetrics (cpu: float) (memory: float) (disk: float) (networkIn: float) (networkOut: float) (responseTime: float) (throughput: float) (errorRate: float) =
            {
                CpuUsage = cpu
                MemoryUsage = memory
                DiskUsage = disk
                NetworkIn = networkIn
                NetworkOut = networkOut
                ResponseTime = responseTime
                Throughput = throughput
                ErrorRate = errorRate
                Timestamp = DateTime.UtcNow
            }

        /// Create blockchain metrics
        let createBlockchainMetrics (blockHeight: BlockHeight) (txCount: uint64) (pendingTxs: uint64) (blockTime: float) (hashRate: float) (difficulty: float) (stakedAmount: Balance) (validatorCount: uint32) (networkSize: uint32) =
            {
                BlockHeight = blockHeight
                TransactionCount = txCount
                PendingTransactions = pendingTxs
                BlockTime = blockTime
                HashRate = hashRate
                Difficulty = difficulty
                StakedAmount = stakedAmount
                ValidatorCount = validatorCount
                NetworkSize = networkSize
                Timestamp = DateTime.UtcNow
            }
