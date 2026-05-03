namespace Trellis.Node

open System
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open System.Net.NetworkInformation
open System.Net.Sockets
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Trellis.Core
open SecurityAudit

/// Security audit service for continuous monitoring
type SecurityAuditService(logger: ILogger<SecurityAuditService>) =
    inherit BackgroundService()
    
    let mutable lastAuditReport: SecurityAuditReport option = None
    let mutable auditCount = 0
    
    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        async {
            logger.LogInformation("Starting Security Audit Service...")
            
            while not stoppingToken.IsCancellationRequested do
                try
                    // Run security audit every hour
                    do! Async.Sleep(TimeSpan.FromHours(1.0))
                    
                    if not stoppingToken.IsCancellationRequested then
                        logger.LogInformation("Running scheduled security audit...")
                        let report = SecurityAuditor.runSecurityAudit()
                        lastAuditReport <- Some report
                        auditCount <- auditCount + 1
                        
                        // Log security score
                        logger.LogInformation("Security audit completed. Score: {Score}/100, Vulnerabilities: {Count}", 
                            report.SecurityScore, report.TotalVulnerabilities)
                        
                        // Alert on critical vulnerabilities
                        if report.CriticalCount > 0 then
                            logger.LogError("CRITICAL SECURITY ALERT: {Count} critical vulnerabilities found!", report.CriticalCount)
                        
                        if report.HighCount > 0 then
                            logger.LogWarning("HIGH SECURITY ALERT: {Count} high severity vulnerabilities found!", report.HighCount)
                        
                        // Export report if vulnerabilities found
                        if report.TotalVulnerabilities > 0 then
                            let reportJson = SecurityAuditor.exportSecurityAuditToJson report
                            logger.LogInformation("Security audit report exported: {Length} characters", reportJson.Length)
                
                with
                | ex -> logger.LogError(ex, "Error in security audit service")
            
            logger.LogInformation("Security Audit Service stopped.")
        } |> Async.StartAsTask :> Task
    
    /// Get the latest security audit report
    member this.GetLatestAuditReport() : SecurityAuditReport option =
        lastAuditReport
    
    /// Get security audit statistics
    member this.GetAuditStatistics() : (int * int * int) =
        match lastAuditReport with
        | Some report -> (report.SecurityScore, report.TotalVulnerabilities, auditCount)
        | None -> (0, 0, auditCount)
    
    /// Force immediate security audit
    member this.RunImmediateAudit() : SecurityAuditReport =
        logger.LogInformation("Running immediate security audit...")
        let report = SecurityAuditor.runSecurityAudit()
        lastAuditReport <- Some report
        auditCount <- auditCount + 1
        report

/// Security monitoring service
type SecurityMonitoringService(logger: ILogger<SecurityMonitoringService>) =
    inherit BackgroundService()
    
    let mutable securityMetrics: Map<string, obj> = Map.empty
    let mutable alertCount = 0
    let mutable lastCpuSample = DateTime.UtcNow
    let mutable lastCpuTime = 0.0
    
    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        async {
            logger.LogInformation("Starting Security Monitoring Service...")
            
            while not stoppingToken.IsCancellationRequested do
                try
                    // Monitor security metrics every 5 minutes
                    do! Async.Sleep(TimeSpan.FromMinutes(5.0))
                    
                    if not stoppingToken.IsCancellationRequested then
                        this.UpdateSecurityMetrics()
                        this.CheckSecurityAlerts()
                
                with
                | ex -> logger.LogError(ex, "Error in security monitoring service")
            
            logger.LogInformation("Security Monitoring Service stopped.")
        } |> Async.StartAsTask :> Task
    
    member private this.UpdateSecurityMetrics() : unit =
        // Update various security metrics
        let currentTime = DateTime.UtcNow
        securityMetrics <- 
            securityMetrics
            |> Map.add "lastUpdate" currentTime
            |> Map.add "alertCount" alertCount
            |> Map.add "systemUptime" (box (DateTime.UtcNow - Process.GetCurrentProcess().StartTime))
    
    member private this.CheckSecurityAlerts() : unit =
        // Check for security anomalies
        let currentTime = DateTime.UtcNow
        
        // Check for unusual network activity
        let networkActivity = this.GetNetworkActivityLevel()
        if networkActivity > 90 then
            logger.LogWarning("High network activity detected: {Activity}%", networkActivity)
            alertCount <- alertCount + 1
        
        // Example: Check for memory usage
        let memoryUsage = GC.GetTotalMemory(false)
        if memoryUsage > 100_000_000L then // 100MB
            logger.LogWarning("High memory usage detected: {Memory} bytes", memoryUsage)
            alertCount <- alertCount + 1
    
    /// Get network activity level (0-100)
    member private this.GetNetworkActivityLevel() : int =
        // Calculate network activity based on system metrics
        let cpuUsage = this.GetCpuUsage()
        let memoryUsage = this.GetMemoryUsage()
        let networkConnections = this.GetActiveConnections()
        
        // Combine metrics to determine activity level
        let activityScore = 
            (cpuUsage * 0.4) + 
            (memoryUsage * 0.3) + 
            (networkConnections * 0.3)
        
        min 100 (int activityScore)
    
    /// Get CPU usage percentage
    member private this.GetCpuUsage() : float =
        // Comprehensive CPU usage monitoring implementation
        try
            let currentProcess = Process.GetCurrentProcess()
            let currentTime = DateTime.UtcNow
            let currentCpuTime = currentProcess.TotalProcessorTime.TotalMilliseconds
            let timeDiff = (currentTime - lastCpuSample).TotalMilliseconds
            
            if timeDiff > 0.0 && lastCpuSample <> DateTime.MinValue then
                let cpuDiff = currentCpuTime - lastCpuTime
                let cpuUsage = (cpuDiff / timeDiff) * 100.0
                
                // Update for next calculation
                lastCpuSample <- currentTime
                lastCpuTime <- currentCpuTime
                
                // Consider system load average for more comprehensive monitoring
                let systemLoad = this.GetSystemLoadAverage()
                let adjustedUsage = min 100.0 (cpuUsage * 0.7 + systemLoad * 0.3)
                
                max 0.0 adjustedUsage
            else
                // First measurement
                lastCpuSample <- currentTime
                lastCpuTime <- currentCpuTime
                0.0
        with
        | ex ->
            logger.LogWarning(ex, "Failed to get CPU usage, using fallback")
            // Fallback to basic calculation
            let currentProcess = Process.GetCurrentProcess()
            let cpuTime = currentProcess.TotalProcessorTime.TotalMilliseconds
            let currentTime = DateTime.UtcNow
            let timeDiff = (currentTime - currentProcess.StartTime).TotalMilliseconds
            
            if timeDiff > 0.0 then
                min 100.0 ((cpuTime / timeDiff) * 100.0)
            else
                0.0
    
    /// Get system load average (simplified implementation)
    member private this.GetSystemLoadAverage() : float =
        try
            // Simplified system load calculation
            let currentProcess = Process.GetCurrentProcess()
            let cpuTime = currentProcess.TotalProcessorTime.TotalMilliseconds
            let upTime = (DateTime.UtcNow - currentProcess.StartTime).TotalMilliseconds
            
            if upTime > 0.0 then
                (cpuTime / upTime) * 100.0
            else
                0.0
        with
        | ex ->
            logger.LogDebug(ex, "Could not get system load average")
            0.0
    
    /// Get memory usage percentage
    member private this.GetMemoryUsage() : float =
        let totalMemory = GC.GetTotalMemory(false)
        let maxMemory = 1024L * 1024L * 1024L // 1GB limit
        min 100.0 ((float totalMemory / float maxMemory) * 100.0)
    
    /// Get active network connections count
    member private this.GetActiveConnections() : float =
        // Actual network connection tracking implementation
        try
            let activeConnections =
                // Count TCP connections in different states
                let tcpConnections =
                    let ipProps = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    try
                        let tcpConns = ipProps.GetActiveTcpConnections()
                        tcpConns |> Array.filter (fun conn -> conn.State <> TcpState.Closed) |> Array.length
                    with
                    | _ -> 0
                
                // Count UDP connections
                let udpConnections =
                    let ipProps = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    try
                        let udpListeners = ipProps.GetActiveUdpListeners()
                        udpListeners.Length
                    with
                    | _ -> 0
                
                // Count network interfaces activity (simplified)
                let networkInterfaces =
                    try
                        let nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        nics |> Array.filter (fun ni -> ni.OperationalStatus = OperationalStatus.Up) |> Array.length
                    with
                    | _ -> 0
                
                tcpConnections + udpConnections + networkInterfaces
            
            float activeConnections
        with
        | ex ->
            logger.LogWarning(ex, "Failed to get active connections, using fallback")
            // Fallback: estimate based on process network activity
            try
                let currentProcess = Process.GetCurrentProcess()
                // Try to estimate based on handle count and thread count as proxy
                let estimatedConnections = min 50.0 (float (currentProcess.Threads.Count * 2 + currentProcess.HandleCount / 10))
                estimatedConnections
            with
            | _ -> 10.0 // Default fallback value
    
    /// Get current security metrics
    member this.GetSecurityMetrics() : Map<string, obj> =
        securityMetrics
    
    /// Get alert count
    member this.GetAlertCount() : int =
        alertCount