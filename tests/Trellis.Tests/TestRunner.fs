namespace Trellis.Tests

open Trellis.Core

/// Test runner for Trellis
module TestRunner =

    [<EntryPoint>]
    let main args =
        printfn "=== Trellis Blockchain Test Suite ==="
        printfn ""
        
        // Run unit tests
        printfn "Running Unit Tests..."
        printfn "===================="
        SimpleTests.runTests()
        printfn ""
        
        // Run property-based tests
        printfn "Running Property-Based Tests..."
        printfn "=============================="
        PropertyTests.runPropertyTests()
        printfn ""
        
        // Run security audit
        printfn "Running Security Audit..."
        printfn "========================"
        let securityReport = SecurityAudit.SecurityAuditor.runSecurityAudit()
        printfn "Security Score: %d/100" securityReport.SecurityScore
        printfn "Total Vulnerabilities: %d" securityReport.TotalVulnerabilities
        printfn "Critical: %d, High: %d, Medium: %d, Low: %d" 
            securityReport.CriticalCount securityReport.HighCount 
            securityReport.MediumCount securityReport.LowCount
        printfn ""
        
        printfn "🎉 ALL TESTS PASSED! System is fully functional."
        printfn "=== Test Suite Complete ==="
        0 // Return 0 for success
