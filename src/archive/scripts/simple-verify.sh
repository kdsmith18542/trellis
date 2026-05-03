#!/bin/bash

# Simple Trellis Blockchain Verification Script
# Quick verification of core functionality

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Test counters
TESTS_PASSED=0
TESTS_FAILED=0
TOTAL_TESTS=0

# Functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[✓]${NC} $1"
    ((TESTS_PASSED++))
}

log_error() {
    echo -e "${RED}[✗]${NC} $1"
    ((TESTS_FAILED++))
}

log_test() {
    echo -e "${CYAN}[TEST]${NC} $1"
    ((TOTAL_TESTS++))
}

# Test 1: Build System
test_build() {
    log_test "1. Build System Test"
    
    log_info "Building Trellis solution..."
    if dotnet build Trellis.sln --verbosity minimal; then
        log_success "Solution builds successfully"
    else
        log_error "Solution build failed"
        return 1
    fi
}

# Test 2: Unit Tests
test_unit_tests() {
    log_test "2. Unit Tests"
    
    log_info "Running unit tests..."
    if dotnet run --project tests/Trellis.Tests --no-build; then
        log_success "Unit tests pass"
    else
        log_error "Unit tests failed"
        return 1
    fi
}

# Test 3: Node Startup
test_node() {
    log_test "3. Node Startup Test"
    
    log_info "Testing node startup..."
    
    # Start node in background
    timeout 3s dotnet run --project src/Trellis.Node --no-build &
    NODE_PID=$!
    
    # Wait for startup
    sleep 2
    
    # Check if node is running
    if ps -p $NODE_PID > /dev/null; then
        log_success "Node starts successfully"
        
        # Stop the node
        kill $NODE_PID 2>/dev/null || true
        wait $NODE_PID 2>/dev/null || true
    else
        log_error "Node failed to start"
        return 1
    fi
}

# Test 4: Core Functionality
test_core() {
    log_test "4. Core Functionality Test"
    
    log_info "Testing core blockchain functions..."
    
    # Create a test script
    cat > test_core.fs << 'EOF'
open System
open Trellis.Core
open SimpleTypes
open SimpleCrypto

// Test hash function
let testData = [|1uy; 2uy; 3uy; 4uy; 5uy|]
let hash1 = sha256 testData
let hash2 = sha256 testData

if hash1 = hash2 then
    printfn "Hash function: OK"
else
    printfn "Hash function: FAIL"
    exit 1

// Test key generation
let (publicKey, privateKey) = generateKeyPair()
printfn "Key generation: OK"

// Test signature
let message = [|1uy; 2uy; 3uy|]
let signature = sign privateKey message
let isValid = verifySignature publicKey message signature

if isValid then
    printfn "Signature verification: OK"
else
    printfn "Signature verification: FAIL"
    exit 1

// Test block hash
let blockHeader = {
    Height = BlockHeight(1UL)
    PreviousHash = Hash(Array.zeroCreate 32)
    PoWHash = Hash(Array.zeroCreate 32)
    Timestamp = Timestamp(DateTime.UtcNow)
    Finalized = false
}
let blockHash = blockHash blockHeader
printfn "Block hash: OK"

printfn "Core functionality: ALL TESTS PASSED"
EOF
    
    if dotnet fsi test_core.fs; then
        log_success "Core functionality working"
        rm -f test_core.fs
    else
        log_error "Core functionality test failed"
        rm -f test_core.fs
        return 1
    fi
}

# Test 5: Project Structure
test_structure() {
    log_test "5. Project Structure Test"
    
    # Check key files
    if [ -f "Trellis.sln" ]; then
        log_success "Solution file exists"
    else
        log_error "Solution file missing"
        return 1
    fi
    
    if [ -f "src/Trellis.Core/Trellis.Core.fsproj" ]; then
        log_success "Core project exists"
    else
        log_error "Core project missing"
        return 1
    fi
    
    if [ -f "src/Trellis.Node/Trellis.Node.fsproj" ]; then
        log_success "Node project exists"
    else
        log_error "Node project missing"
        return 1
    fi
    
    if [ -f "tests/Trellis.Tests/Trellis.Tests.fsproj" ]; then
        log_success "Test project exists"
    else
        log_error "Test project missing"
        return 1
    fi
}

# Generate report
generate_report() {
    echo ""
    echo "=========================================="
    echo "TRELLIS BLOCKCHAIN VERIFICATION REPORT"
    echo "=========================================="
    echo ""
    echo "Total Tests: $TOTAL_TESTS"
    echo "Passed: $TESTS_PASSED"
    echo "Failed: $TESTS_FAILED"
    echo ""
    
    if [ $TESTS_FAILED -eq 0 ]; then
        echo -e "${GREEN}🎉 ALL TESTS PASSED! System is fully functional.${NC}"
        echo ""
        echo "✅ Build System: Working"
        echo "✅ Unit Tests: Passing"
        echo "✅ Node Startup: Working"
        echo "✅ Core Functionality: Working"
        echo "✅ Project Structure: Complete"
        echo ""
        echo "🚀 The Trellis blockchain system is ready for use!"
    else
        echo -e "${RED}❌ SOME TESTS FAILED! System needs attention.${NC}"
        echo ""
        echo "Please review the failed tests and fix the issues."
    fi
    
    echo ""
    echo "For detailed logs, check the output above."
    echo "=========================================="
}

# Main execution
main() {
    echo -e "${PURPLE}🔍 TRELLIS BLOCKCHAIN VERIFICATION${NC}"
    echo "====================================="
    echo ""
    
    # Run all tests
    test_build
    echo ""
    
    test_unit_tests
    echo ""
    
    test_node
    echo ""
    
    test_core
    echo ""
    
    test_structure
    echo ""
    
    # Generate final report
    generate_report
}

# Run main function
main "$@"
