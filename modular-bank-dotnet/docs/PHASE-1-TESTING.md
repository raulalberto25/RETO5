# Phase 1 Testing Guide (Accounts MS Extraction + YARP Gateway)

## Prerequisites

- Docker and Docker Compose installed
- curl or Postman for API testing
- .NET 10 SDK (for local development)

---

## Setup & Deployment

### Option A: Docker Compose (Recommended)

```bash
# From repository root
docker-compose up -d

# Wait for all services to be healthy (check docker-compose logs)
docker-compose logs -f

# Services will be available at:
# - Gateway: http://localhost:5000
# - Monolith (direct): http://localhost:5010
# - Accounts MS (direct): http://localhost:5001
```

### Option B: Local Development

**Terminal 1 - Monolith:**
```bash
cd src/ModularBank
export ASPNETCORE_ENVIRONMENT=Development
export Features__UseAccountsMS=false  # Use in-process, not HTTP
dotnet run
# Runs on http://localhost:5000
```

**Terminal 2 - Accounts Service:**
```bash
cd services/accounts-service
export ASPNETCORE_ENVIRONMENT=Development
dotnet run
# Runs on http://localhost:5001
```

---

## Test Scenarios

### Test 1: Health Checks

```bash
# Gateway health
curl http://localhost:5000/health
# Expected: "ok"

# Monolith health (direct)
curl http://localhost:5010/health
# Expected: "ok"

# Accounts MS health (direct)
curl http://localhost:5001/health
# Expected: "ok"
```

**Status:** ✅ Services are running

---

### Test 2: User Registration

```bash
# Register user
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user1@example.com",
    "password": "SecurePassword123",
    "name": "John Doe"
  }'

# Expected Response (201 Created):
# {
#   "accessToken": "eyJhbGc...",
#   "refreshToken": "random-base64..."
# }
```

**Extract tokens:**
```bash
ACCESS_TOKEN="<accessToken from response>"
REFRESH_TOKEN="<refreshToken from response>"
```

**Status:** ✅ User created, JWT tokens issued

---

### Test 3: Create Account (via Gateway)

```bash
curl -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'

# Expected Response (201 Created):
# {
#   "id": "550e8400-e29b-41d4-a716-446655440000",
#   "accountNumber": "ACC123abc456DEF",
#   "balance": 0
# }
```

**Extract account ID:**
```bash
ACCOUNT_ID="<id from response>"
```

**Status:** ✅ Account created via Accounts MS (through gateway)

---

### Test 4: List Accounts (via Gateway)

```bash
curl -X GET http://localhost:5000/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN"

# Expected Response (200 OK):
# [
#   {
#     "id": "550e8400-e29b-41d4-a716-446655440000",
#     "accountNumber": "ACC123abc456DEF",
#     "balance": 0
#   }
# ]
```

**Status:** ✅ Account listed (data flows through gateway)

---

### Test 5: Check Balance (via Gateway)

```bash
curl -X GET http://localhost:5000/accounts/$ACCOUNT_ID/balance \
  -H "Authorization: Bearer $ACCESS_TOKEN"

# Expected Response (200 OK):
# {
#   "amount": "0.0000"
# }
```

**Status:** ✅ Balance retrieved via Accounts MS

---

### Test 6: Direct Call to Accounts MS (Bypass Gateway)

```bash
# This tests that Accounts MS is independent
curl -X GET http://localhost:5001/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN"

# Expected Response (200 OK):
# [same account list as Test 4]
```

**Status:** ✅ Accounts MS accessible directly (not just via gateway)

---

### Test 7: Gateway Routing Verification

**Test routing to different backends:**

```bash
# Route to Accounts MS
curl -I http://localhost:5000/accounts
# Should succeed (200)

# Route to Monolith (/auth)
curl -I http://localhost:5000/auth/me
# Should succeed

# Route to Monolith (/notifications)
curl -X GET http://localhost:5000/notifications \
  -H "Authorization: Bearer $ACCESS_TOKEN"
# Should succeed (empty list expected)
```

**Status:** ✅ Gateway correctly routes requests

---

### Test 8: JWT Authorization Enforcement

```bash
# Try without token
curl http://localhost:5000/accounts
# Expected: 401 Unauthorized

# Try with invalid token
curl -X GET http://localhost:5000/accounts \
  -H "Authorization: Bearer invalid-token"
# Expected: 401 Unauthorized

# Try with valid token
curl -X GET http://localhost:5000/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN"
# Expected: 200 OK
```

**Status:** ✅ Authorization working correctly

---

### Test 9: Error Handling

```bash
# Try to get balance for non-existent account
curl -X GET http://localhost:5000/accounts/00000000-0000-0000-0000-000000000000/balance \
  -H "Authorization: Bearer $ACCESS_TOKEN"
# Expected: 404 Not Found

# Try to access someone else's account (would require second user)
# Expected: 403 Forbidden
```

**Status:** ✅ Error handling working

---

### Test 10: Full Transfer Flow (Monolith Integration)

```bash
# Create a second account for testing
curl -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'
# Save this ACCOUNT_ID as TARGET_ACCOUNT_ID

# Now test transfer (through monolith, which calls Accounts MS)
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ACCOUNT_ID'",
    "targetAccountId": "'$TARGET_ACCOUNT_ID'",
    "amount": 100,
    "reference": "Test transfer"
  }'

# Expected: 422 Unprocessable Entity (insufficient funds)
# Message: "Insufficient funds: balance 0, attempting to debit 100"
```

**Status:** ✅ Monolith correctly calls Accounts MS for balance checks

---

## Verification Checklist

- [ ] All 3 services healthy (health checks pass)
- [ ] User can register and get JWT tokens
- [ ] Account created successfully via gateway
- [ ] Accounts MS responds independently (not just via gateway)
- [ ] Gateway correctly routes `/accounts/*` to Accounts MS
- [ ] Gateway correctly routes other paths to Monolith
- [ ] JWT authorization enforced (401 without token)
- [ ] Error handling returns correct status codes
- [ ] Transfers work (attempt with insufficient funds)
- [ ] Logs show request flowing through all services

---

## Troubleshooting

### Issue: Connection refused to postgres-accounts

**Cause:** Database not ready
```bash
# Check if container is healthy
docker-compose ps

# Wait for health check to pass
docker-compose logs postgres-accounts
```

### Issue: 502 Bad Gateway from YARP

**Cause:** Backend service not responding
```bash
# Check if backend service is healthy
docker-compose logs accounts-service
# or
docker-compose logs monolith

# Verify service URLs in appsettings
cat gateway/appsettings.json
```

### Issue: 401 Unauthorized even with token

**Cause:** JWT secret mismatch
```bash
# Ensure JWT secret is same across services
grep -r "Jwt:Secret" appsettings*.json
# All should use same secret in Development
```

### Issue: Account not found in Accounts MS

**Cause:** Data in wrong database
```bash
# Connect to postgres-accounts
docker-compose exec postgres-accounts psql -U bank -d finbank_accounts -c "SELECT * FROM accounts.accounts;"

# Should show created accounts
```

---

## Performance Baseline (Phase 1)

For Phase 1 setup, expected metrics:

| Operation | Latency | Route |
|-----------|---------|-------|
| GET /health | <10ms | Direct |
| POST /auth/register | 50-100ms | Monolith |
| POST /accounts | 100-200ms | Gateway → Accounts MS |
| GET /accounts | 50-100ms | Gateway → Accounts MS |
| GET /accounts/{id}/balance | 50-100ms | Gateway → Accounts MS |

Note: Latencies increase by ~50ms due to gateway routing (network call + HTTP overhead vs in-process).

---

## Next Steps (Phase 2)

- Extract Transfers as MS2
- Implement RabbitMQ for event-driven communication
- Add saga choreography pattern
- Implement Outbox pattern for reliable event delivery

---

## Cleanup

```bash
# Stop all services
docker-compose down

# Remove volumes (to start fresh)
docker-compose down -v
```
