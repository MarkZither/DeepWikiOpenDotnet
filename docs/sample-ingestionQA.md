Query	What it should surface
What is the Vortex Cache eviction policy and its default configuration?	PLRU-K4, 4-level generational aging, vortex.eviction_policy table
What is the FlareMesh gossip protocol port number?	Port 7441
What is issue VX-1182 and what is the workaround?	Generational pinning bug, vortex.plru_promotion_bias=1.15
Which HelioForge release was codenamed Sandpiper?	4.7.0
Who is the architecture owner for HelioForge?	Priya Nandakumar
What is Zentriq's DORA compliance tracking ID?	ZQ-DORA-2024-0087
How many Vortex Cache nodes run per zone in Halesford?	24 nodes
What replaced the Laminar Buffer subsystem?	Vortex Cache

*Strong RAG signal:* If the model answers "4.7.0 Sandpiper" or "VX-1182 / plru_promotion_bias" it retrieved the document. If it says "I don't know" or hallucinates, retrieval failed.
