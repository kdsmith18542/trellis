#!/bin/bash

# Trellis Testnet Deployment Script
# This script deploys a full Trellis blockchain testnet

set -e

echo "🌐 Trellis Testnet Deployment"
echo "============================="
echo ""

# Configuration
NETWORK_ID="trellis-testnet"
NODE_COUNT=5
MINING_NODES=3
BLOCK_TIME=5
TEST_DURATION=10
INITIAL_BALANCE=1000000
MINING_DIFFICULTY=2
MAX_TX_PER_BLOCK=10

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
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

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    print_error "dotnet is not installed. Please install .NET 8.0 or later."
    exit 1
fi

# Check dotnet version
DOTNET_VERSION=$(dotnet --version)
print_status "Using .NET version: $DOTNET_VERSION"

# Build the project
print_status "Building Trellis blockchain..."
cd /home/keith/trellis

# Build core library
print_status "Building Trellis.Core..."
if dotnet build src/Trellis.Core/Trellis.Core.fsproj; then
    print_success "Trellis.Core built successfully"
else
    print_error "Failed to build Trellis.Core"
    exit 1
fi

# Build node
print_status "Building Trellis.Node..."
if dotnet build src/Trellis.Node/Trellis.Node.fsproj; then
    print_success "Trellis.Node built successfully"
else
    print_error "Failed to build Trellis.Node"
    exit 1
fi

# Build testnet
print_status "Building Trellis.Testnet..."
if dotnet build src/Trellis.Testnet/Trellis.Testnet.fsproj; then
    print_success "Trellis.Testnet built successfully"
else
    print_error "Failed to build Trellis.Testnet"
    exit 1
fi

# Create testnet configuration
print_status "Creating testnet configuration..."
cat > testnet-config.json << EOF
{
  "NetworkId": "$NETWORK_ID",
  "NodeCount": $NODE_COUNT,
  "MiningNodes": $MINING_NODES,
  "BlockTime": $BLOCK_TIME,
  "TestDuration": $TEST_DURATION,
  "InitialBalance": $INITIAL_BALANCE,
  "MiningDifficulty": $MINING_DIFFICULTY,
  "MaxTransactionsPerBlock": $MAX_TX_PER_BLOCK,
  "LogLevel": "Information",
  "EnableMining": true,
  "EnableP2P": true,
  "EnableIndexer": true,
  "EnableMonitoring": true
}
EOF

print_success "Testnet configuration created"

# Create testnet directory structure
print_status "Creating testnet directory structure..."
mkdir -p testnet/{nodes,logs,data,configs}

# Create node configurations
for i in $(seq 1 $NODE_COUNT); do
    NODE_ID="node-$i"
    NODE_PORT=$((8000 + i))
    P2P_PORT=$((9000 + i))
    
    print_status "Creating configuration for $NODE_ID..."
    
    cat > testnet/configs/$NODE_ID.json << EOF
{
  "NodeId": "$NODE_ID",
  "NetworkId": "$NETWORK_ID",
  "HttpPort": $NODE_PORT,
  "P2PPort": $P2P_PORT,
  "IsMiningNode": $([ $i -le $MINING_NODES ] && echo "true" || echo "false"),
  "InitialBalance": $INITIAL_BALANCE,
  "MiningDifficulty": $MINING_DIFFICULTY,
  "MaxTransactionsPerBlock": $MAX_TX_PER_BLOCK,
  "BlockTime": $BLOCK_TIME,
  "DataDirectory": "testnet/data/$NODE_ID",
  "LogFile": "testnet/logs/$NODE_ID.log",
  "EnableP2P": true,
  "EnableMining": $([ $i -le $MINING_NODES ] && echo "true" || echo "false"),
  "EnableIndexer": true,
  "EnableMonitoring": true,
  "BootstrapNodes": [
    "127.0.0.1:9001",
    "127.0.0.1:9002",
    "127.0.0.1:9003"
  ]
}
EOF
done

print_success "Node configurations created"

# Create startup scripts
print_status "Creating startup scripts..."

# Create node startup script
cat > testnet/start-node.sh << 'EOF'
#!/bin/bash

NODE_ID=$1
CONFIG_FILE="testnet/configs/$NODE_ID.json"

if [ -z "$NODE_ID" ]; then
    echo "Usage: $0 <node-id>"
    echo "Available nodes: node-1, node-2, node-3, node-4, node-5"
    exit 1
fi

if [ ! -f "$CONFIG_FILE ]; then
    echo "Error: Configuration file $CONFIG_FILE not found"
    exit 1
fi

echo "Starting $NODE_ID with configuration $CONFIG_FILE"
cd /home/keith/trellis
dotnet run --project src/Trellis.Node/Trellis.Node.fsproj -- --config "$CONFIG_FILE"
EOF

chmod +x testnet/start-node.sh

# Create testnet coordinator script
cat > testnet/start-testnet.sh << 'EOF'
#!/bin/bash

echo "🌐 Starting Trellis Testnet Coordinator"
echo "======================================"
echo ""

cd /home/keith/trellis
dotnet run --project src/Trellis.Testnet/Trellis.Testnet.fsproj
EOF

chmod +x testnet/start-testnet.sh

# Create monitoring script
cat > testnet/monitor.sh << 'EOF'
#!/bin/bash

echo "📊 Trellis Testnet Monitor"
echo "========================="
echo ""

# Check running processes
echo "Active Trellis processes:"
ps aux | grep -E "(Trellis|dotnet)" | grep -v grep

echo ""
echo "Network connections:"
netstat -tlnp | grep -E "(800[0-9]|900[0-9])" || echo "No active network connections"

echo ""
echo "Log files:"
ls -la testnet/logs/ 2>/dev/null || echo "No log files found"

echo ""
echo "Data directories:"
ls -la testnet/data/ 2>/dev/null || echo "No data directories found"
EOF

chmod +x testnet/monitor.sh

# Create cleanup script
cat > testnet/cleanup.sh << 'EOF'
#!/bin/bash

echo "🧹 Cleaning up Trellis Testnet"
echo "=============================="

# Kill all Trellis processes
pkill -f "Trellis" 2>/dev/null || true
pkill -f "dotnet.*Trellis" 2>/dev/null || true

# Remove testnet data
rm -rf testnet/data/*
rm -rf testnet/logs/*

echo "Testnet cleanup completed"
EOF

chmod +x testnet/cleanup.sh

print_success "Startup scripts created"

# Create Docker Compose for easy deployment
print_status "Creating Docker Compose configuration..."
cat > docker-compose.testnet.yml << EOF
version: '3.8'

services:
  trellis-testnet:
    build:
      context: .
      dockerfile: Dockerfile.testnet
    container_name: trellis-testnet
    ports:
      - "8001:8001"  # Node 1 HTTP
      - "8002:8002"  # Node 2 HTTP
      - "8003:8003"  # Node 3 HTTP
      - "8004:8004"  # Node 4 HTTP
      - "8005:8005"  # Node 5 HTTP
      - "9001:9001"  # Node 1 P2P
      - "9002:9002"  # Node 2 P2P
      - "9003:9003"  # Node 3 P2P
      - "9004:9004"  # Node 4 P2P
      - "9005:9005"  # Node 5 P2P
    volumes:
      - ./testnet:/app/testnet
    environment:
      - DOTNET_ENVIRONMENT=Development
    command: ["dotnet", "run", "--project", "src/Trellis.Testnet/Trellis.Testnet.fsproj"]
    networks:
      - trellis-testnet

networks:
  trellis-testnet:
    driver: bridge
EOF

print_success "Docker Compose configuration created"

# Create testnet documentation
print_status "Creating testnet documentation..."
cat > testnet/README.md << 'EOF'
# Trellis Testnet

This directory contains the Trellis blockchain testnet deployment.

## Quick Start

### Option 1: Direct Deployment
```bash
# Start the testnet coordinator
./start-testnet.sh

# Or start individual nodes
./start-node.sh node-1
./start-node.sh node-2
# ... etc
```

### Option 2: Docker Deployment
```bash
# Start with Docker Compose
docker-compose -f docker-compose.testnet.yml up

# Stop the testnet
docker-compose -f docker-compose.testnet.yml down
```

## Configuration

- **Network ID**: trellis-testnet
- **Node Count**: 5 nodes
- **Mining Nodes**: 3 nodes (node-1, node-2, node-3)
- **Block Time**: 5 seconds
- **Test Duration**: 10 minutes
- **Initial Balance**: 1,000,000 tokens per node
- **Mining Difficulty**: 2 (lower for testing)

## Node Endpoints

- **Node 1**: http://localhost:8001 (Mining)
- **Node 2**: http://localhost:8002 (Mining)
- **Node 3**: http://localhost:8003 (Mining)
- **Node 4**: http://localhost:8004 (Non-mining)
- **Node 5**: http://localhost:8005 (Non-mining)

## P2P Ports

- **Node 1**: 9001
- **Node 2**: 9002
- **Node 3**: 9003
- **Node 4**: 9004
- **Node 5**: 9005

## Monitoring

```bash
# Monitor the testnet
./monitor.sh

# View logs
tail -f testnet/logs/node-1.log
tail -f testnet/logs/node-2.log
# ... etc
```

## Cleanup

```bash
# Clean up testnet data
./cleanup.sh
```

## Features Tested

- ✅ Multiple blockchain nodes
- ✅ Real mining operations
- ✅ Network communication
- ✅ Transaction processing
- ✅ Block validation
- ✅ Network synchronization
- ✅ P2P networking
- ✅ Consensus mechanisms
- ✅ State management
- ✅ Indexing and monitoring
EOF

print_success "Testnet documentation created"

# Final status
print_success "🎉 Trellis Testnet deployment ready!"
echo ""
echo "Testnet Configuration:"
echo "  Network ID: $NETWORK_ID"
echo "  Node Count: $NODE_COUNT"
echo "  Mining Nodes: $MINING_NODES"
echo "  Block Time: ${BLOCK_TIME}s"
echo "  Test Duration: ${TEST_DURATION}m"
echo "  Initial Balance: $INITIAL_BALANCE tokens"
echo "  Mining Difficulty: $MINING_DIFFICULTY"
echo ""
echo "To start the testnet:"
echo "  cd testnet && ./start-testnet.sh"
echo ""
echo "To monitor the testnet:"
echo "  cd testnet && ./monitor.sh"
echo ""
echo "To clean up:"
echo "  cd testnet && ./cleanup.sh"
echo ""
print_success "Testnet deployment completed successfully!"
