#!/bin/bash

# Trellis Regnet Launch Script
# Launch a regression test network to verify the blockchain system

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_test() {
    echo -e "${CYAN}[TEST]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    # Check if dotnet is available
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet is not installed or not in PATH"
        exit 1
    fi
    
    log_success "All prerequisites met"
}

# Build the regnet
build_regnet() {
    log_info "Building Trellis Regnet..."
    
    # Build the solution
    dotnet build Trellis.sln --configuration Release
    
    if [ $? -eq 0 ]; then
        log_success "Regnet built successfully"
    else
        log_error "Failed to build regnet"
        exit 1
    fi
}

# Launch the regnet
launch_regnet() {
    log_info "Launching Trellis Regnet..."
    
    echo ""
    echo "🚀 Starting Trellis Regnet (Regression Test Network)"
    echo "=================================================="
    echo ""
    echo "This will:"
    echo "  - Start 3 blockchain nodes"
    echo "  - Create test transactions"
    echo "  - Mine blocks"
    echo "  - Sync the network"
    echo "  - Generate a test report"
    echo ""
    echo "Press Ctrl+C to stop the regnet test"
    echo ""
    
    # Run the regnet
    dotnet run --project src/Trellis.Regnet --configuration Release
}

# Main execution
main() {
    echo -e "${PURPLE}🌐 TRELLIS REGNET LAUNCHER${NC}"
    echo "============================="
    echo ""
    
    # Step 1: Check prerequisites
    check_prerequisites
    echo ""
    
    # Step 2: Build regnet
    build_regnet
    echo ""
    
    # Step 3: Launch regnet
    launch_regnet
}

# Run main function
main "$@"
