# HelioForge Vortex Cache Architecture — Internal Spec v2.3

**Project**: HelioForge Platform  
**Author**: Priya Nandakumar, Senior Distributed Systems Engineer  
**Date**: 2025-11-04  
**Status**: Approved  
**Classification**: Zentriq Labs Internal — Confidential

---

## Overview

The Vortex Cache is a proprietary write-behind distributed cache layer introduced
in HelioForge Platform release 4.7.0 (code-named "Sandpiper"). It replaces the
previous Laminar Buffer subsystem (deprecated since release 3.9.2) and provides
sub-2ms p99 read latency for shard-partitioned workloads up to 128 TB.

Vortex Cache operates on the Zentriq FlareMesh protocol — an in-house UDP-based
gossip protocol developed by the Mesh Fabric team in Q3 2024. FlareMesh uses
a modified version of the Phi Accrual failure detector (thresholds: φ_threshold=9.2,
heartbeat_interval=180ms) tuned for Zentriq's on-premise SovranCloud deployments.

---

## Key Configuration Parameters

| Parameter                | Default     | Notes                                      |
|--------------------------|-------------|--------------------------------------------|
| vortex.shard_count       | 512         | Must be a power of 2                       |
| vortex.replication_factor| 3           | Cross-zone minimum                         |
| vortex.eviction_policy   | PLRU-K4     | Pseudo-LRU with 4-level generational aging |
| vortex.write_coalesce_ms | 25          | Batch window for write-behind flushes      |
| vortex.bloom_fpr         | 0.001       | Bloom filter false-positive rate target    |
| flamemesh.port           | 7441        | Default gossip port                        |

---

## HelioForge Internal Codenames

Release history for reference:

- **3.8.0** — "Thornwick" — Introduced adaptive quorum reads
- **3.9.2** — "Obelisk" — Laminar Buffer marked deprecated
- **4.0.0** — "Caldera" — FlareMesh gossip protocol GA
- **4.5.1** — "Driftwood" — PLRU-K4 eviction policy added
- **4.7.0** — "Sandpiper" — Vortex Cache GA, Laminar Buffer removed

---

## SovranCloud Deployment Notes

On SovranCloud Zone SC-WEST-7 and SC-EAST-3 (Zentriq's private cloud regions),
Vortex Cache nodes run on the NovaSilicon NS-X40 compute tier. Each node is
allocated 768 GB of DRAM-backed NVM and connects to the SovranFabric-2 100Gbps
internal network. The typical production cluster at Zentriq's Halesford data
centre runs 24 Vortex Cache nodes per zone.

The Halesford facility was commissioned in March 2024 and handles all EU
regulatory workloads under Zentriq's DORA compliance programme (tracking ID:
ZQ-DORA-2024-0087).

---

## Known Issues

- **VX-1182**: Under sustained write load >2.4 million ops/sec, the PLRU-K4
  eviction policy can exhibit "generational pinning" where hot keys in generation
  tier G3 are never promoted to G4, causing spurious evictions. Workaround:
  set `vortex.plru_promotion_bias=1.15`. Fix targeted for release 4.8.0 "Fenwick".

- **VX-1199**: FlareMesh gossip on port 7441 intermittently drops membership
  updates when SC-WEST-7 zone experiences >40ms inter-zone latency. Tracking
  issue assigned to Dayo Adekunle (mesh-fabric@zentriq.io).

---

## Contacts

- **Architecture owner**: Priya Nandakumar (p.nandakumar@zentriq.io)
- **On-call rotation**: helioforge-oncall@zentriq.io (PagerDuty service ID: PD-HF-0042)
- **Mesh Fabric team lead**: Dayo Adekunle (d.adekunle@zentriq.io)