#!/bin/bash

# Trellis Testnet CLI
# Interactive command-line interface for the Trellis testnet

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to print colored output
print_header() {
    echo -e "${CYAN}🌐 Trellis Testnet CLI${NC}"
    echo -e "${CYAN}====================${NC}"
    echo ""
}

print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if testnet is running
check_testnet_status() {
    local running_processes=$(ps aux | grep -E "(Trellis|dotnet.*Trellis)" | grep -v grep | wc -l)
    if [ $running_processes -gt 0 ]; then
        return 0
    else
        return 1
    fi
}

# Function to get node status
get_node_status() {
    local node_id=$1
    local port=$2
    
    if curl -s "http://localhost:$port/health" > /dev/null 2>&1; then
        echo "✅ Online"
    else
        echo "❌ Offline"
    fi
}

# Function to get blockchain info
get_blockchain_info() {
    local node_id=$1
    local port=$2
    
    echo "📊 Blockchain Info for $node_id:"
    echo "  Status: $(get_node_status $node_id $port)"
    
    # Try to get chain state
    local chain_state=$(curl -s "http://localhost:$port/api/chain-state" 2>/dev/null || echo "{}")
    if [ "$chain_state" != "{}" ]; then
        echo "  Chain State: $chain_state"
    fi
}

# Function to send transaction
send_transaction() {
    local from_node=$1
    local to_node=$2
    local amount=$3
    
    print_status "Sending transaction from $from_node to $to_node for $amount tokens..."
    
    # This would integrate with the actual gRPC API
    echo "Transaction sent successfully!"
}

# Function to mine block
mine_block() {
    local node_id=$1
    
    print_status "Triggering mining on $node_id..."
    
    # This would integrate with the actual mining API
    echo "Mining triggered on $node_id!"
}

# Function to show network topology
show_network_topology() {
    print_status "Network Topology:"
    echo ""
    echo "🌐 Trellis Testnet Network"
    echo "========================="
    echo ""
    echo "Nodes:"
    for i in {1..5}; do
        local port=$((8000 + i))
        local status=$(get_node_status "node-$i" $port)
        echo "  Node $i (port $port): $status"
    done
    echo ""
    echo "P2P Connections:"
    netstat -tlnp | grep -E "900[0-9]" | head -5 || echo "  No P2P connections found"
}

# Function to show statistics
show_statistics() {
    print_status "Testnet Statistics:"
    echo ""
    
    # Count running processes
    local running_nodes=$(ps aux | grep -E "Trellis" | grep -v grep | wc -l)
    echo "Active Nodes: $running_nodes"
    
    # Check log files
    if [ -d "testnet/logs" ]; then
        local log_files=$(ls testnet/logs/*.log 2>/dev/null | wc -l)
        echo "Log Files: $log_files"
    fi
    
    # Check data directories
    if [ -d "testnet/data" ]; then
        local data_dirs=$(ls testnet/data/ 2>/dev/null | wc -l)
        echo "Data Directories: $data_dirs"
    fi
}

# Function to show logs
show_logs() {
    local node_id=$1
    local lines=${2:-20}
    
    if [ -z "$node_id" ]; then
        print_error "Please specify a node ID (node-1, node-2, etc.)"
        return 1
    fi
    
    local log_file="testnet/logs/$node_id.log"
    if [ -f "$log_file" ]; then
        print_status "Showing last $lines lines of $node_id logs:"
        echo ""
        tail -n $lines "$log_file"
    else
        print_error "Log file $log_file not found"
    fi
}

# Function to start testnet
start_testnet() {
    print_status "Starting Trellis Testnet..."
    
    if check_testnet_status; then
        print_warning "Testnet is already running!"
        return 1
    fi
    
    cd /home/keith/trellis/testnet
    ./start-testnet.sh &
    
    print_success "Testnet started in background"
    print_status "Use 'trellis-testnet-cli.sh status' to check status"
}

# Function to stop testnet
stop_testnet() {
    print_status "Stopping Trellis Testnet..."
    
    if ! check_testnet_status; then
        print_warning "Testnet is not running"
        return 1
    fi
    
    pkill -f "Trellis" 2>/dev/null || true
    pkill -f "dotnet.*Trellis" 2>/dev/null || true
    
    print_success "Testnet stopped"
}

# Function to restart testnet
restart_testnet() {
    print_status "Restarting Trellis Testnet..."
    stop_testnet
    sleep 2
    start_testnet
}

# Function to clean testnet
clean_testnet() {
    print_status "Cleaning Trellis Testnet..."
    
    stop_testnet
    
    if [ -d "testnet" ]; then
        rm -rf testnet/data/*
        rm -rf testnet/logs/*
        print_success "Testnet data cleaned"
    else
        print_warning "Testnet directory not found"
    fi
}

# Function to show help
show_help() {
    print_header
    echo "Usage: $0 [command] [options]"
    echo ""
    echo "Commands:"
    echo "  start                    Start the testnet"
    echo "  stop                     Stop the testnet"
    echo "  restart                  Restart the testnet"
    echo "  status                   Show testnet status"
    echo "  nodes                    Show node status"
    echo "  network                  Show network topology"
    echo "  stats                    Show testnet statistics"
    echo "  logs <node-id> [lines]   Show logs for a node"
    echo "  clean                    Clean testnet data"
    echo "  help                     Show this help"
    echo ""
    echo "Examples:"
    echo "  $0 start                 # Start the testnet"
    echo "  $0 status               # Check testnet status"
    echo "  $0 logs node-1 50       # Show last 50 lines of node-1 logs"
    echo "  $0 network              # Show network topology"
    echo ""
}

# Main command processing
case "${1:-help}" in
    start)
        start_testnet
        ;;
    stop)
        stop_testnet
        ;;
    restart)
        restart_testnet
        ;;
    status)
        print_header
        if check_testnet_status; then
            print_success "Testnet is running"
            show_statistics
        else
            print_warning "Testnet is not running"
        fi
        ;;
    nodes)
        print_header
        show_network_topology
        ;;
    network)
        print_header
        show_network_topology
        ;;
    stats)
        print_header
        show_statistics
        ;;
    logs)
        print_header
        show_logs "$2" "$3"
        ;;
    clean)
        print_header
        clean_testnet
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Unknown command: $1"
        echo ""
        show_help
        exit 1
        ;;
esac
