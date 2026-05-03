#!/bin/bash

# Trellis Beta Testnet Launch Script
# Phase V: Institutional Beta Launch

set -e

echo "🚀 Launching Trellis Beta Testnet - Phase V: Institutional Beta"
echo "=============================================================="

# Configuration
NETWORK_ID="trellis-beta-testnet"
TESTNET_DURATION="30d"
REWARD_POOL="10000000"
MIN_STAKE="10000"
COMPLIANCE_SCORE="0.7"
KYC_SCORE="0.8"
REPUTATION="0.6"
MAX_SLASHING="2"

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

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    # Check if kubectl is available
    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl is not installed or not in PATH"
        exit 1
    fi
    
    # Check if docker is available
    if ! command -v docker &> /dev/null; then
        log_error "docker is not installed or not in PATH"
        exit 1
    fi
    
    # Check if dotnet is available
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet is not installed or not in PATH"
        exit 1
    fi
    
    log_success "All prerequisites met"
}

# Build the beta testnet application
build_beta_testnet() {
    log_info "Building Trellis Beta Testnet application..."
    
    # Build the solution
    dotnet build Trellis.sln --configuration Release
    
    if [ $? -eq 0 ]; then
        log_success "Beta testnet application built successfully"
    else
        log_error "Failed to build beta testnet application"
        exit 1
    fi
}

# Create Docker image for beta testnet
create_docker_image() {
    log_info "Creating Docker image for beta testnet..."
    
    # Create Dockerfile for beta testnet
    cat > Dockerfile.beta << EOF
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet build "src/Trellis.Beta/Trellis.Beta.fsproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/Trellis.Beta/Trellis.Beta.fsproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Trellis.Beta.dll"]
EOF
    
    # Build Docker image
    docker build -f Dockerfile.beta -t trellis/beta:latest .
    
    if [ $? -eq 0 ]; then
        log_success "Docker image created successfully"
    else
        log_error "Failed to create Docker image"
        exit 1
    fi
}

# Deploy to Kubernetes
deploy_to_kubernetes() {
    log_info "Deploying beta testnet to Kubernetes..."
    
    # Apply the beta testnet configuration
    kubectl apply -f k8s/beta-testnet.yaml
    
    if [ $? -eq 0 ]; then
        log_success "Beta testnet deployed to Kubernetes"
    else
        log_error "Failed to deploy beta testnet to Kubernetes"
        exit 1
    fi
}

# Wait for deployment to be ready
wait_for_deployment() {
    log_info "Waiting for beta testnet deployment to be ready..."
    
    kubectl wait --for=condition=available --timeout=300s deployment/trellis-beta-coordinator -n trellis-beta
    
    if [ $? -eq 0 ]; then
        log_success "Beta testnet deployment is ready"
    else
        log_error "Beta testnet deployment failed to become ready"
        exit 1
    fi
}

# Get deployment status
get_deployment_status() {
    log_info "Getting beta testnet deployment status..."
    
    echo "=== Deployment Status ==="
    kubectl get deployments -n trellis-beta
    
    echo ""
    echo "=== Pod Status ==="
    kubectl get pods -n trellis-beta
    
    echo ""
    echo "=== Service Status ==="
    kubectl get services -n trellis-beta
    
    echo ""
    echo "=== ConfigMap Status ==="
    kubectl get configmaps -n trellis-beta
}

# Display testnet information
display_testnet_info() {
    log_info "Beta Testnet Information:"
    echo "=========================="
    echo "Network ID: $NETWORK_ID"
    echo "Testnet Duration: $TESTNET_DURATION"
    echo "Reward Pool: $REWARD_POOL tokens"
    echo "Minimum Stake: $MIN_STAKE tokens"
    echo "Compliance Score: $COMPLIANCE_SCORE"
    echo "KYC Score: $KYC_SCORE"
    echo "Reputation: $REPUTATION"
    echo "Max Slashing Events: $MAX_SLASHING"
    echo ""
    echo "Access Points:"
    echo "- HTTP API: http://localhost:8080"
    echo "- gRPC API: localhost:9090"
    echo "- Metrics: http://localhost:9090/metrics"
    echo ""
    echo "Corporate Validator Onboarding:"
    echo "- KYC verification required"
    echo "- Compliance score >= $COMPLIANCE_SCORE"
    echo "- Reputation >= $REPUTATION"
    echo "- Minimum stake: $MIN_STAKE tokens"
    echo "- Maximum slashing events: $MAX_SLASHING"
}

# Main execution
main() {
    echo "Starting Trellis Beta Testnet Launch Process..."
    echo ""
    
    # Step 1: Check prerequisites
    check_prerequisites
    echo ""
    
    # Step 2: Build application
    build_beta_testnet
    echo ""
    
    # Step 3: Create Docker image
    create_docker_image
    echo ""
    
    # Step 4: Deploy to Kubernetes
    deploy_to_kubernetes
    echo ""
    
    # Step 5: Wait for deployment
    wait_for_deployment
    echo ""
    
    # Step 6: Get deployment status
    get_deployment_status
    echo ""
    
    # Step 7: Display testnet information
    display_testnet_info
    echo ""
    
    log_success "🎉 Trellis Beta Testnet launched successfully!"
    log_info "Phase V: Institutional Beta is now active"
    log_info "Corporate validators can now begin onboarding process"
    
    echo ""
    echo "Next Steps:"
    echo "1. Corporate validators should complete KYC verification"
    echo "2. Validators should stake minimum $MIN_STAKE tokens"
    echo "3. Validators should maintain compliance score >= $COMPLIANCE_SCORE"
    echo "4. Validators should maintain reputation >= $REPUTATION"
    echo "5. Monitor testnet metrics and performance"
    echo ""
    echo "For support, contact the Trellis development team"
}

# Run main function
main "$@"
