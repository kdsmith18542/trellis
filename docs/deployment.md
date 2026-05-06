# Trellis Deployment Guide

This guide covers deploying a Trellis node for production or staging environments.

## Prerequisites

- .NET 10 SDK
- Windows or Linux (Ubuntu 22.04+ recommended)
- 4GB RAM minimum
- RocksDB dependencies (installed automatically via NuGet)

## Deployment Options

### 1. Bare Metal / VM

1. **Build the artifacts**:
   ```bash
   dotnet publish src/Trellis.Node -c Release -o ./publish
   ```

2. **Configure `appsettings.json`**:
   Ensure `ApiListenUrl` and `P2PListenUrl` are set to correct public/private IPs.

3. **Run the node**:
   ```bash
   dotnet Trellis.Node.dll
   ```

### 2. Docker (Recommended)

1. **Build the image**:
   ```bash
   docker build -t trellis-node .
   ```

2. **Run the container**:
   ```bash
   docker run -d \
     -p 5000:5000 \
     -p 6000:6000 \
     -v trellis-data:/app/data \
     --name trellis-node \
     trellis-node
   ```

## Post-Deployment Checklist

- [ ] Check `/health` endpoint.
- [ ] Verify node appears in P2P peer list (if using seed nodes).
- [ ] Monitor `node.log` for any storage or consensus errors.
- [ ] Ensure firewall allows incoming traffic on P2P port (default 6000).

## Monitoring

Trellis includes a built-in monitoring dashboard at `http://localhost:5000/explorer.html`.
For enterprise monitoring, use the gRPC endpoints to export metrics to Prometheus/Grafana.
