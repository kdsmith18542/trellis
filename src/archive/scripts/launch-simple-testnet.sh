#!/bin/bash

# Trellis Simple Testnet Launch Script
# Launch a simplified testnet for community testing

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

log_testnet() {
    echo -e "${CYAN}[TESTNET]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    # Check if docker is available
    if ! command -v docker &> /dev/null; then
        log_error "Docker is not installed or not in PATH"
        exit 1
    fi
    
    # Check if docker-compose is available
    if ! command -v docker-compose &> /dev/null; then
        log_error "Docker Compose is not installed or not in PATH"
        exit 1
    fi
    
    # Check if dotnet is available
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet is not installed or not in PATH"
        exit 1
    fi
    
    log_success "All prerequisites met"
}

# Create testnet directories
create_directories() {
    log_info "Creating testnet directories..."
    
    mkdir -p data/{testnet,node-1,node-2,node-3}
    mkdir -p logs/{node-1,node-2,node-3}
    
    log_success "Directories created"
}

# Build the testnet
build_testnet() {
    log_info "Building Trellis testnet..."
    
    # Build the core system first
    dotnet build src/Trellis.Core/Trellis.Core.fsproj --configuration Release
    dotnet build src/Trellis.Node/Trellis.Node.fsproj --configuration Release
    
    # Build Docker images
    docker-compose -f docker-compose.simple.yml build
    
    log_success "Testnet built successfully"
}

# Launch the testnet
launch_testnet() {
    log_info "Launching Trellis testnet..."
    
    echo ""
    echo "🌐 Starting Trellis Simple Testnet"
    echo "==================================="
    echo ""
    echo "This will launch a testnet with:"
    echo "  • 3 blockchain nodes (2 miners, 1 validator)"
    echo "  • Testnet coordinator"
    echo "  • Block explorer (http://localhost:3000)"
    echo "  • API endpoints (http://localhost:8080)"
    echo ""
    echo "Network will run continuously for community testing."
    echo ""
    
    # Start the testnet
    docker-compose -f docker-compose.simple.yml up -d
    
    log_success "Testnet launched successfully"
}

# Verify testnet health
verify_testnet() {
    log_info "Verifying testnet health..."
    
    # Wait for services to start
    sleep 30
    
    # Check coordinator health
    if curl -f http://localhost:8080/health > /dev/null 2>&1; then
        log_success "Coordinator is healthy"
    else
        log_warning "Coordinator health check failed"
    fi
    
    # Check nodes
    for i in {1..3}; do
        if curl -f http://localhost:900$i/health > /dev/null 2>&1; then
            log_success "Node $i is healthy"
        else
            log_warning "Node $i health check failed"
        fi
    done
    
    # Check block explorer
    if curl -f http://localhost:3000 > /dev/null 2>&1; then
        log_success "Block explorer is accessible"
    else
        log_warning "Block explorer not accessible"
    fi
}

# Display testnet information
display_testnet_info() {
    echo ""
    echo "🎉 TRELLIS SIMPLE TESTNET IS LIVE!"
    echo "=================================="
    echo ""
    echo "🌐 Network Information:"
    echo "  Network ID: trellis-testnet-v1"
    echo "  Node Count: 3 nodes"
    echo "  Mining Nodes: 2 nodes"
    echo "  Validator Nodes: 1 node"
    echo ""
    echo "🔗 Access Points:"
    echo "  • Block Explorer: http://localhost:3000"
    echo "  • API Endpoint: http://localhost:8080"
    echo ""
    echo "📊 Node Endpoints:"
    echo "  • Node 1: http://localhost:9001 (Miner)"
    echo "  • Node 2: http://localhost:9002 (Miner)"
    echo "  • Node 3: http://localhost:9003 (Validator)"
    echo ""
    echo "🛠️ Management Commands:"
    echo "  • View logs: docker-compose -f docker-compose.simple.yml logs"
    echo "  • Stop testnet: docker-compose -f docker-compose.simple.yml down"
    echo "  • Restart testnet: docker-compose -f docker-compose.simple.yml restart"
    echo "  • View status: docker-compose -f docker-compose.simple.yml ps"
    echo ""
    echo "🎯 Community Testing:"
    echo "  • View blockchain: http://localhost:3000"
    echo "  • API documentation: http://localhost:8080/docs"
    echo ""
    echo "The testnet will run continuously for community testing!"
    echo "Use Ctrl+C to stop this script (testnet will continue running)"
    echo ""
}

# Main execution
main() {
    echo -e "${PURPLE}🌐 TRELLIS SIMPLE TESTNET LAUNCHER${NC}"
    echo "=========================================="
    echo ""
    echo "Launching simplified testnet for community testing..."
    echo ""
    
    # Step 1: Check prerequisites
    check_prerequisites
    echo ""
    
    # Step 2: Create directories
    create_directories
    echo ""
    
    # Step 3: Build testnet
    build_testnet
    echo ""
    
    # Step 4: Launch testnet
    launch_testnet
    echo ""
    
    # Step 5: Verify testnet
    verify_testnet
    echo ""
    
    # Step 6: Display information
    display_testnet_info
    
    # Keep running to show status
    log_testnet "Testnet is running. Press Ctrl+C to stop monitoring (testnet will continue)"
    while true; do
        sleep 60
        log_info "Testnet is running... $(date)"
    done
}

# Run main function
main "$@"
