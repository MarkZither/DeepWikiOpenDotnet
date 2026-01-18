# Deployment Checklist

Complete pre-deployment verification for Development, Staging, and Production environments.

## Pre-Deployment Requirements

- [ ] All source code committed and pushed
- [ ] All tests passing locally
- [ ] Code review completed
- [ ] Architecture Constitution verified
- [ ] No outstanding bugs or TODOs related to core functionality
- [ ] Documentation updated

---

## Development Environment

**Target**: Local machine or development server  
**Time to Deploy**: 5-10 minutes

### Database Setup

- [ ] SQL Server 2025 OR PostgreSQL 17+ with pgvector running
- [ ] Database "deepwiki" created
- [ ] Connection string configured in `appsettings.Development.json`
- [ ] EF Core migrations applied: `dotnet ef database update`
- [ ] Test data seeded (if applicable)

### Application Configuration

- [ ] User Secrets configured: `dotnet user-secrets set ConnectionStrings:SqlServer "..."`
- [ ] `appsettings.Development.json` exists with all required keys
- [ ] Logging level set to Debug for development
- [ ] CORS configured if needed
- [ ] Health check endpoint accessible at `/health`

### Code & Build

- [ ] `dotnet build` succeeds with no warnings
- [ ] `dotnet test` - all unit tests pass
- [ ] Code coverage ≥90% verified locally
- [ ] No compiler warnings or errors
- [ ] StyleCop/analyzer rules addressed

### Verification

- [ ] Can retrieve documents: `GET /api/documents/{id}`
- [ ] Can search vectors: `POST /api/search` with embedding
- [ ] Health check returns Healthy: `GET /health`
- [ ] Logs show successful startup
- [ ] No unhandled exceptions in logs

### Sign-Off

- [ ] Developer: __________________ Date: __________
- [ ] Notes: ________________________________________________________________

---

## Staging Environment

**Target**: Pre-production server(s)  
**Time to Deploy**: 15-30 minutes  
**Prerequisites**: Development checklist complete

### Database Setup

- [ ] SQL Server 2025 OR PostgreSQL 17+ running on staging server
- [ ] Database "deepwiki_staging" created
- [ ] Connection string stored in Azure Key Vault
- [ ] Connection string retrieved via Managed Identity (if using Azure)
- [ ] EF Core migrations applied from release branch
- [ ] Test data loaded (production-like dataset)
- [ ] Backup procedures verified and tested

### Application Configuration

- [ ] Application secrets stored in Key Vault (not in code)
- [ ] `appsettings.Staging.json` configured with staging URLs
- [ ] Logging level set to Information
- [ ] Health check configured and accessible
- [ ] HTTPS/TLS enabled with valid certificate
- [ ] CORS configured for staging endpoints only

### Deployment Artifact

- [ ] Docker image built: `docker build -t deepwiki:staging .`
- [ ] Image pushed to container registry
- [ ] Image scanned for vulnerabilities: `docker scan deepwiki:staging`
- [ ] Image tested locally before pushing
- [ ] Tag matches version: `deepwiki:v1.0.0-staging`

### Network & Security

- [ ] Network policies configured (firewall rules)
- [ ] Database access restricted to application servers only
- [ ] Secrets not logged or exposed in error messages
- [ ] Connection encryption enabled (TLS/Encrypt=true)
- [ ] Sensitive endpoints protected (if applicable)

### Load Testing

- [ ] Load test with 1000+ concurrent users (if applicable)
- [ ] Performance targets verified:
  - [ ] Vector query <500ms @ 10K documents
  - [ ] Bulk upsert <10 seconds for 1000 documents
  - [ ] Memory usage <500MB sustained
- [ ] Database performance under load acceptable
- [ ] No memory leaks in sustained operation (24h test)

### Integration Testing

- [ ] All integration tests pass: `dotnet test tests/DeepWiki.Data.SqlServer.Tests/`
- [ ] Integration tests pass on staging database
- [ ] Cross-database parity verified (if supporting both)
- [ ] Health checks pass on staging
- [ ] Dependency health checks validated

### Documentation

- [ ] Deployment runbook updated for staging
- [ ] Configuration documented (environment variables, secrets)
- [ ] Known issues documented
- [ ] Monitoring dashboards created
- [ ] Alert rules configured

### Verification

- [ ] Application starts cleanly
- [ ] All endpoints respond correctly
- [ ] Health check returns Healthy
- [ ] Database operations work (CRUD + vector search)
- [ ] Error handling verified (test with bad inputs)
- [ ] No sensitive data in logs
- [ ] Performance baseline captured

### Sign-Off

- [ ] QA Lead: __________________ Date: __________
- [ ] Ops/DevOps: ________________ Date: __________
- [ ] Notes: ________________________________________________________________

---

## Production Environment

**Target**: Production server(s)  
**Time to Deploy**: 30-60 minutes (includes rollback plan)  
**Prerequisites**: Staging checklist complete + approved change request

### Change Management

- [ ] Change request submitted and approved
- [ ] Maintenance window scheduled or zero-downtime plan approved
- [ ] Rollback plan documented and tested
- [ ] Team notifications sent (if needed)
- [ ] Stakeholders informed of deployment

### Database Setup

- [ ] SQL Server 2025 OR PostgreSQL 17+ with pgvector running
- [ ] High availability configured (mirroring, replication, or clustering)
- [ ] Database backups running on schedule
- [ ] Backup restoration tested
- [ ] Connection string stored in Azure Key Vault
- [ ] Access via Managed Identity configured
- [ ] Database encryption at rest enabled
- [ ] TDE (SQL Server) or pgcrypto (PostgreSQL) configured

### Application Configuration

- [ ] All secrets in Key Vault (zero secrets in code/config files)
- [ ] `appsettings.Production.json` secured and reviewed
- [ ] Logging level set to Warning (not Debug)
- [ ] Structured logging configured (JSON format)
- [ ] Log destination configured (Application Insights, Datadog, etc.)
- [ ] Health check endpoints accessible
- [ ] HTTPS/TLS configured with production certificate
- [ ] HSTS header enabled
- [ ] CORS restricted to production domains only

### Deployment Artifact

- [ ] Docker image signed and verified
- [ ] Image pulled from secure registry only
- [ ] Image version matches release tag: `deepwiki:v1.0.0`
- [ ] Image tested in production-like environment
- [ ] Image scanned for vulnerabilities (zero critical issues)
- [ ] Image layers documented

### Network & Security

- [ ] WAF (Web Application Firewall) configured if applicable
- [ ] DDoS protection enabled
- [ ] Network segmentation verified (database isolated)
- [ ] Ingress/egress rules whitelisted
- [ ] TLS 1.3 enforced (minimum TLS 1.2)
- [ ] Certificate pinning considered (if appropriate)
- [ ] VPN or private networking used for admin access
- [ ] API rate limiting configured (if public endpoint)

### High Availability

- [ ] Multi-region deployment verified (if applicable)
- [ ] Load balancer configured and tested
- [ ] Health check integration with load balancer
- [ ] Auto-scaling rules configured (if using cloud)
- [ ] Circuit breaker patterns for dependencies
- [ ] Graceful shutdown implemented
- [ ] Zero-downtime deployment strategy tested

### Performance & Optimization

- [ ] Database indexes analyzed and optimized
- [ ] HNSW index parameters tuned for production scale
- [ ] Connection pooling optimized
- [ ] Caching strategy implemented (if applicable)
- [ ] Pagination enforced for list endpoints
- [ ] Query N+1 problems eliminated
- [ ] Memory profiling done with production-like load
- [ ] CPU usage acceptable under expected load

### Monitoring & Alerting

- [ ] Application performance monitoring (APM) configured
- [ ] Metrics exported: response times, error rates, throughput
- [ ] Custom alerts for:
  - [ ] Database connection failures
  - [ ] Vector query timeouts (>1s)
  - [ ] Memory usage >70%
  - [ ] Disk usage >80%
  - [ ] Health check failures
  - [ ] Error rate >1%
- [ ] Log aggregation and search enabled
- [ ] Dashboards created for key metrics
- [ ] On-call escalation configured
- [ ] Runbooks created for common incidents

### Backup & Disaster Recovery

- [ ] Full backup strategy documented
- [ ] Backup frequency verified (at least daily)
- [ ] Point-in-time recovery (PITR) tested
- [ ] Disaster recovery plan tested
- [ ] RTO (Recovery Time Objective) <1 hour documented
- [ ] RPO (Recovery Point Objective) <15 minutes documented
- [ ] Cross-region replication configured (if applicable)
- [ ] Backup encryption enabled

### Compliance & Audit

- [ ] Security audit completed (if required)
- [ ] Compliance checks passed (GDPR, HIPAA, etc., if applicable)
- [ ] Access control lists reviewed
- [ ] Audit logging enabled
- [ ] Data retention policies configured
- [ ] Privacy impact assessment completed (if required)

### Documentation

- [ ] Production runbook updated
- [ ] Architecture diagram current
- [ ] Configuration documented (environment variables)
- [ ] Troubleshooting guide updated
- [ ] Known limitations documented
- [ ] Performance tuning guide created
- [ ] Disaster recovery procedure documented

### Pre-Deployment Testing

- [ ] Smoke tests pass in production environment
- [ ] All API endpoints respond correctly
- [ ] Database operations verified
- [ ] Vector search tested with production scale data
- [ ] Health check endpoint returns Healthy
- [ ] Error handling tested (simulate failures)
- [ ] Logging verified (check log aggregation)
- [ ] Performance metrics acceptable

### Deployment Execution

- [ ] Blue-green or canary deployment strategy used (if applicable)
- [ ] Deployment monitored in real-time
- [ ] Rollback procedure ready to execute
- [ ] Team in communication during deployment
- [ ] Status updates provided to stakeholders

### Post-Deployment Verification

- [ ] Application responding normally
- [ ] All endpoints working correctly
- [ ] Health check shows Healthy
- [ ] No errors in production logs
- [ ] Database performing well
- [ ] Performance metrics match baseline
- [ ] No spike in error rates
- [ ] User-facing functionality verified
- [ ] Database backups running
- [ ] Monitoring alerts working

### Sign-Off

- [ ] DevOps/SRE: _________________ Date: __________
- [ ] Database Admin: ______________ Date: __________
- [ ] Security: _____________________ Date: __________
- [ ] Engineering Manager: __________ Date: __________
- [ ] Notes: ________________________________________________________________

---

## Post-Deployment

### 24-Hour Verification

- [ ] No critical incidents
- [ ] Error rates normal
- [ ] Performance baseline maintained
- [ ] Database health good
- [ ] Backups completed successfully
- [ ] No unusual resource usage

### 7-Day Review

- [ ] Production metrics reviewed
- [ ] No performance degradation
- [ ] Incident review (if any)
- [ ] User feedback positive
- [ ] All monitoring alerts functional
- [ ] Lessons learned documented

### 30-Day Review

- [ ] Deployment considered stable
- [ ] Next optimization phase planned
- [ ] Performance report generated
- [ ] Cost analysis (if cloud)
- [ ] Security audit results reviewed

---

## Rollback Plan

### Triggering Rollback

Rollback if any of:
- [ ] Critical errors in production logs
- [ ] Error rate >5%
- [ ] Health check returns Unhealthy
- [ ] Database connectivity issues
- [ ] Performance degradation >50% from baseline
- [ ] Data corruption detected
- [ ] Security vulnerability discovered

### Rollback Procedure

1. **Immediate Actions**:
   ```bash
   # Redirect traffic to previous version
   kubectl set image deployment/deepwiki deepwiki=deepwiki:v1.0.0-previous
   # OR
   docker service update --image deepwiki:v1.0.0-previous deepwiki_api
   ```

2. **Database Rollback** (if schema changed):
   ```bash
   # Revert to previous migration
   dotnet ef database update [previous-migration] -p src/DeepWiki.Data.SqlServer
   ```

3. **Verification**:
   - [ ] Traffic routed to previous version
   - [ ] Health checks passing
   - [ ] No errors in logs
   - [ ] Database state consistent
   - [ ] Users can access functionality

4. **Post-Rollback Analysis**:
   - [ ] Root cause identified
   - [ ] Incident report filed
   - [ ] Fix developed and tested
   - [ ] Re-deployment scheduled

---

## Emergency Contacts

| Role | Name | Phone | Email |
|------|------|-------|-------|
| On-Call | | | |
| DevOps | | | |
| Database Admin | | | |
| Security | | | |
| Manager | | | |

---

## Notes

- **Last Updated**: January 18, 2026
- **Version**: 1.0.0
- **Status**: Production Checklist
