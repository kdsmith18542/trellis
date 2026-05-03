#!/bin/bash

# Trellis Testnet Stop Script
# Stop the persistent testnet

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
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

# Stop the testnet
stop_testnet() {
    log_info "Stopping Trellis testnet..."
    
    # Stop all services
    docker-compose -f docker-compose.testnet.yml down
    
    log_success "Testnet stopped successfully"
}

# Clean up resources
cleanup() {
    log_info "Cleaning up resources..."
    
    # Remove containers
    docker-compose -f docker-compose.testnet.yml rm -f
    
    # Remove volumes (optional)
    read -p "Remove testnet data? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        docker-compose -f docker-compose.testnet.yml down -v
        rm -rf data/ logs/
        log_success "Testnet data removed"
    else
        log_info "Testnet data preserved"
    fi
}

# Main execution
main() {
    echo "🛑 Stopping Trellis Testnet"
    echo "=========================="
    echo ""
    
    # Stop testnet
    stop_testnet
    echo ""
    
    # Cleanup
    cleanup
    echo ""
    
    log_success "Testnet shutdown complete!"
    echo ""
    echo "To restart the testnet, run:"
    echo "  ./scripts/launch-testnet.sh"
}

# Run main function
main "$@"
