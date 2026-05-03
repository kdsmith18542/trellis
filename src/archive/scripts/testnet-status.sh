#!/bin/bash

# Trellis Testnet Status Script
# Check the status of the testnet

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

log_status() {
    echo -e "${CYAN}[STATUS]${NC} $1"
}

# Check Docker status
check_docker_status() {
    log_info "Checking Docker containers..."
    
    if docker-compose -f docker-compose.testnet.yml ps | grep -q "Up"; then
        log_success "Testnet containers are running"
        docker-compose -f docker-compose.testnet.yml ps
    else
        log_warning "No testnet containers are running"
    fi
}

# Check network health
check_network_health() {
    log_info "Checking network health..."
    
    # Check coordinator
    if curl -f http://localhost:8080/health > /dev/null 2>&1; then
        log_success "Coordinator is healthy"
    else
        log_error "Coordinator is not responding"
    fi
    
    # Check nodes
    for i in {1..5}; do
        if curl -f http://localhost:900$i/health > /dev/null 2>&1; then
            log_success "Node $i is healthy"
        else
            log_warning "Node $i is not responding"
        fi
    done
    
    # Check block explorer
    if curl -f http://localhost:3000 > /dev/null 2>&1; then
        log_success "Block explorer is accessible"
    else
        log_warning "Block explorer is not accessible"
    fi
    
    # Check faucet
    if curl -f http://localhost:8082/health > /dev/null 2>&1; then
        log_success "Faucet service is healthy"
    else
        log_warning "Faucet service is not responding"
    fi
}

# Get network statistics
get_network_stats() {
    log_info "Getting network statistics..."
    
    echo ""
    echo "📊 Network Statistics:"
    echo "======================"
    
    # Get network status
    if curl -f http://localhost:8080/api/network/status > /dev/null 2>&1; then
        echo "Network Status:"
        curl -s http://localhost:8080/api/network/status | jq . 2>/dev/null || curl -s http://localhost:8080/api/network/status
    fi
    
    echo ""
    
    # Get blockchain info
    if curl -f http://localhost:8080/api/blockchain/info > /dev/null 2>&1; then
        echo "Blockchain Info:"
        curl -s http://localhost:8080/api/blockchain/info | jq . 2>/dev/null || curl -s http://localhost:8080/api/blockchain/info
    fi
    
    echo ""
    
    # Get mining stats
    if curl -f http://localhost:8080/api/mining/stats > /dev/null 2>&1; then
        echo "Mining Statistics:"
        curl -s http://localhost:8080/api/mining/stats | jq . 2>/dev/null || curl -s http://localhost:8080/api/mining/stats
    fi
}

# Check resource usage
check_resources() {
    log_info "Checking resource usage..."
    
    echo ""
    echo "💻 Resource Usage:"
    echo "=================="
    
    # CPU and Memory usage
    docker stats --no-stream --format "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.NetIO}}" | grep trellis || echo "No Trellis containers running"
    
    echo ""
    
    # Disk usage
    echo "Disk Usage:"
    du -sh data/ logs/ 2>/dev/null || echo "No data directories found"
}

# Display access information
display_access_info() {
    echo ""
    echo "🌐 Access Information:"
    echo "====================="
    echo ""
    echo "• Block Explorer: http://localhost:3000"
    echo "• API Endpoint: http://localhost:8080"
    echo "• Faucet Service: http://localhost:8082"
    echo "• Monitoring: http://localhost:3001"
    echo "• Prometheus: http://localhost:9090"
    echo ""
    echo "📊 Node Endpoints:"
    echo "• Node 1: http://localhost:9001 (Miner)"
    echo "• Node 2: http://localhost:9002 (Miner)"
    echo "• Node 3: http://localhost:9003 (Validator)"
    echo "• Node 4: http://localhost:9004 (Miner)"
    echo "• Node 5: http://localhost:9005 (Validator)"
    echo ""
}

# Main execution
main() {
    echo -e "${PURPLE}📊 TRELLIS TESTNET STATUS${NC}"
    echo "============================="
    echo ""
    
    # Check Docker status
    check_docker_status
    echo ""
    
    # Check network health
    check_network_health
    echo ""
    
    # Get network statistics
    get_network_stats
    echo ""
    
    # Check resource usage
    check_resources
    echo ""
    
    # Display access information
    display_access_info
    
    echo "✅ Status check complete!"
}

# Run main function
main "$@"
