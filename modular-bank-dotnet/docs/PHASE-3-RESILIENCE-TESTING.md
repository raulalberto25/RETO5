# Phase 3 Testing Guide (Resilience Patterns)

## Overview

This guide tests the resilience patterns implemented in Transfers MS:
- **Circuit Breaker** (Polly): Opens after 5 failures in 30 seconds
- **Retry** (Polly): 3 attempts with exponential backoff (1s, 2s, 4s)
- **Timeout** (Polly): 30 seconds per request

These patterns protect against cascading failures when Accounts MS is unavailable.

---

## Prerequisites

- Phase 2 fully working (docker-compose up -d)
- curl or Postman for API testing
- Ability to stop/start services

---

## Test Setup

### Get Admin Access

```bash
# Register and get tokens (same as Phase 2)
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@example.com",
    "password": "SecurePassword123",
    "name": "Alice"
  }'

# Save token
ACCESS_TOKEN="<accessToken>"
```

### Create Test Account

```bash
# Create account (calls Accounts MS, should succeed)
ACCOUNT_ID=$(curl -s -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' | jq -r '.id')

echo "Test Account ID: $ACCOUNT_ID"
```

---

## Test 1: Normal Operation (No Failures)

**Objective:** Verify resilience patterns don't interfere with normal operation

```bash
# Attempt transfer (will fail: insufficient funds, but Accounts MS works)
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 100,
    "reference": "Test transfer"
  }'

# Expected: 422 (insufficient funds, not service unavailable)
# Logs: Should see "Found account ... via HTTP (with resilience)"
```

**Verification:**
```bash
# Check logs
docker-compose logs transfers-service | grep -i "with resilience"
# Should show successful calls
```

**Status:** ✅ Pass if returns 422 (not 503)

---

## Test 2: Retry Policy (Transient Failures)

**Objective:** Verify retries happen for transient failures

**Simulate transient failure (network latency spike):**

```bash
# Add network delay to simulate transient failure
# Note: This requires tc (traffic control) on Linux
# For Docker container:

docker-compose exec accounts-service \
  sh -c 'tc qdisc add dev eth0 root netem delay 500ms'

# Now attempt transfer
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 50
  }'

# Remove delay
docker-compose exec accounts-service \
  sh -c 'tc qdisc del dev eth0 root netem delay 500ms'
```

**Verification:**
```bash
# Check logs for retry messages
docker-compose logs transfers-service | grep -i "retry"
# Should see: "Retry 1 after Xms due to..."
# Should eventually succeed after retries
```

**Status:** ✅ Pass if succeeds after 1-3 retries

---

## Test 3: Circuit Breaker (Cascading Failures)

**Objective:** Verify circuit breaker opens after repeated failures

### Step 1: Stop Accounts MS

```bash
# Stop the Accounts service
docker-compose stop accounts-service
docker-compose logs accounts-service
```

### Step 2: Trigger 5 Failures (Opens Circuit)

```bash
# Attempt transfer 1 (fail, retry 3x, total 1 failure)
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 50
  }'

# Repeat ~5 times to accumulate 5+ failures
for i in {1..5}; do
  curl -X POST http://localhost:5000/transfers \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -d '{
      "sourceAccountId": "'$ACCOUNT_ID'",
      "targetAccountId": "'$(uuidgen)'",
      "amount": 50
    }'
  
  echo "Attempt $i"
  sleep 1
done
```

**Expected Progression:**
- Attempts 1-5: Return quickly (circuit open immediately, no retries)
- Error message: "Circuit breaker is open for Accounts MS"

**Verification:**
```bash
# Check logs
docker-compose logs transfers-service | grep -i "circuit breaker"

# Expected output:
# "Circuit breaker opened for 30s after failures..."
# "Circuit breaker is open for Accounts MS"
```

**Status:** ✅ Pass if circuit opens after 5 failures

---

## Test 4: Circuit Breaker Half-Open State

**Objective:** Verify circuit enters half-open state after timeout

```bash
# After 30 seconds (circuit timeout), start Accounts MS
docker-compose start accounts-service
docker-compose logs accounts-service

# Wait 5 seconds for service to be ready
sleep 5

# Attempt one transfer (will test half-open → closed transition)
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 50
  }'
```

**Expected Progression:**
1. Circuit was open for 30s
2. Accounts MS comes back online
3. Circuit enters half-open state
4. One call probes the endpoint
5. If succeeds: circuit closes (reset)
6. Subsequent calls go through normally

**Verification:**
```bash
# Check logs for state transitions
docker-compose logs transfers-service | grep -i "circuit breaker"

# Expected lines:
# "Circuit breaker half-open, testing next call"
# "Circuit breaker closed, resuming calls"
```

**Status:** ✅ Pass if circuit closes after Accounts MS recovers

---

## Test 5: Timeout Policy

**Objective:** Verify requests timeout after 30 seconds

```bash
# Add extreme delay (60 seconds) to Accounts MS
docker-compose exec accounts-service \
  sh -c 'tc qdisc add dev eth0 root netem delay 60000ms' &

# Attempt transfer (will timeout after 30s)
time curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 50
  }'

# Remove delay
docker-compose exec accounts-service \
  sh -c 'tc qdisc del dev eth0 root netem delay 60000ms'
```

**Expected:**
- Request times out after ~30 seconds (not 60)
- Returns error (not hanging)

**Verification:**
```bash
# Check logs for timeout
docker-compose logs transfers-service | grep -i "timeout"

# Time command should show ~30s elapsed
```

**Status:** ✅ Pass if times out after 30s

---

## Test 6: Full Resilience Chain

**Objective:** Verify retry → circuit breaker → timeout work together

```bash
# Scenario: Accounts MS is slow and intermittently failing

# 1. Add delay (30-50% will fail)
docker-compose exec accounts-service \
  sh -c 'tc qdisc add dev eth0 root netem delay 150ms loss 30%' &

# 2. Attempt multiple transfers
for i in {1..10}; do
  echo "Transfer attempt $i:"
  curl -X POST http://localhost:5000/transfers \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -d '{
      "sourceAccountId": "'$ACCOUNT_ID'",
      "targetAccountId": "'$(uuidgen)'",
      "amount": $((i*10))"
    }' \
    2>/dev/null | jq '.error // .id'
  
  sleep 1
done

# 3. Clean up
docker-compose exec accounts-service \
  sh -c 'tc qdisc del dev eth0 root netem delay 150ms loss 30%'
```

**Expected Behavior:**
- Some requests retry and succeed
- After 5 failures: circuit opens (fast fails)
- Circuit recovers after 30s
- Retry + circuit breaker work together

**Verification:**
```bash
# Comprehensive log check
docker-compose logs transfers-service | grep -E "circuit breaker|retry|resilience"

# Count events
echo "=== Retries ==="
docker-compose logs transfers-service | grep -c "Retry"

echo "=== Circuit breaker events ==="
docker-compose logs transfers-service | grep -c "circuit breaker"

echo "=== Successful calls ==="
docker-compose logs transfers-service | grep -c "resilience"
```

**Status:** ✅ Pass if mix of retries, circuit breaks, and successes

---

## Test 7: Outbox Resilience (Bonus)

**Objective:** Verify Outbox pattern works even when Accounts MS fails

### Scenario:
1. Accounts MS is down (no debit/credit possible)
2. Transfer recorded (Transfer + OutboxEntry saved)
3. OutboxWorker keeps retrying event publishing
4. When Accounts MS comes back: debit/credit succeed via events

```bash
# 1. Stop Accounts MS
docker-compose stop accounts-service

# 2. Attempt transfer (will fail due to circuit breaker)
# But outbox entry should be created
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$(uuidgen)'",
    "amount": 75
  }'

# 3. Check outbox entries (should have unpublished entries)
docker-compose exec postgres-transfers psql -U bank -d finbank_transfers -c \
  "SELECT id, event_type, published_at FROM transfers.outbox_entries WHERE published_at IS NULL;"

# 4. Restart Accounts MS
docker-compose start accounts-service
sleep 5

# 5. OutboxWorker will detect unpublished entries and retry
# Check logs
docker-compose logs transfers-service | grep -i "outbox"
```

**Expected:**
- Transfer recorded even though Accounts MS was down
- Outbox entries created with `published_at = NULL`
- When Accounts MS comes back, OutboxWorker publishes events
- Notifications/Audit eventually created

**Verification:**
```bash
# Check if notifications were created (eventually)
sleep 5
curl -X GET http://localhost:5000/notifications \
  -H "Authorization: Bearer $ACCESS_TOKEN" | jq '.[] | .type'

# Should eventually show "TransferSent" from the delayed transfer
```

**Status:** ✅ Pass if outbox entries published after recovery

---

## Metrics to Monitor

### Circuit Breaker Health
```bash
# Command to check circuit state logs
docker-compose logs transfers-service | grep -i "circuit breaker" | tail -10
```

### Retry Patterns
```bash
# Count retry attempts
docker-compose logs transfers-service | grep -c "Retry"
```

### Timeout Events
```bash
# Check for timeouts
docker-compose logs transfers-service | grep -i "timeout"
```

### Error Rate
```bash
# Count errors vs successes
echo "Successes:"
docker-compose logs transfers-service | grep -c "resilience"
echo "Failures:"
docker-compose logs transfers-service | grep -c "failed\|error" | head -5
```

---

## Performance Baseline (with Resilience)

| Scenario | Latency | Notes |
|---|---|---|
| Normal (no failures) | 100-200ms | Unaffected by resilience |
| 1 retry | 150-350ms | Adds 1-2s delay (backoff) |
| Circuit open | <10ms | Fails fast (no call to service) |
| Circuit half-open | 30-50ms | Single probe request |
| Timeout | ~30s | After 30s elapsed |

---

## Cleanup

```bash
# Remove any network delays
docker-compose exec accounts-service \
  sh -c 'tc qdisc del dev eth0 root netem' 2>/dev/null

# Or restart services
docker-compose restart accounts-service transfers-service

# Check health
docker-compose ps
```

---

## Summary

**Resilience patterns successfully implemented when:**
- ✅ Retries work for transient failures
- ✅ Circuit breaker opens after 5 failures
- ✅ Circuit breaker closes after 30s timeout
- ✅ Timeout terminates after 30s
- ✅ Outbox works despite Accounts MS down
- ✅ Normal operation unaffected

**Test this locally before deploying to production.**
