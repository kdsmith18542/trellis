# API Reference

Every Trellis node exposes a REST API and a WebSocket endpoint for real-time streaming.

## REST API

### Health Check

```
GET /health
```

Returns node status and subscriber count.

```json
{
  "status": "ok",
  "version": "10.0",
  "subscribers": 0
}
```

### Block by Hash

```
GET /block/{hash}
```

Returns the full block with transactions.

```json
{
  "header": {
    "hash": "AB12...",
    "height": 42,
    "timestamp": "2026-05-02T20:00:00Z",
    "transactions": [...]
  }
}
```

### Validators

```
GET /validators
```

Returns the current validator set with stake information.

```json
[
  {
    "address": "821d57cd...",
    "stake": 1000000000000000000,
    "isActive": true
  }
]
```

### Chain State

```
GET /chain-state
```

Returns current chain summary.

```json
{
  "blockHeight": 42,
  "lastBlockHash": "ab12...",
  "validatorCount": 1,
  "baseFeePerGas": 1000000000
}
```

### Account

```
GET /account/{address}
```

Returns account balance and nonce.

```json
{
  "address": "821d57cd...",
  "balance": 1000000,
  "nonce": 0
}
```

## WebSocket

```
ws://localhost:5000/stream
```

### Events

Once connected, the server pushes JSON events:

**New Block:**
```json
{
  "type": "block",
  "data": {
    "hash": "ab12...",
    "height": 43,
    "timestamp": "2026-05-02T20:01:00Z"
  }
}
```

**New Transaction:**
```json
{
  "type": "transaction",
  "data": {
    "hash": "cd34...",
    "from": "821d...",
    "to": "ef56..."
  }
}
```

**Heartbeat:**
```json
{
  "type": "heartbeat",
  "data": {
    "connectedClients": 1,
    "uptime": "00:05:00"
  }
}
```
