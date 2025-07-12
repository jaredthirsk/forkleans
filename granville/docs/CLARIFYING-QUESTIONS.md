# Clarifying Questions

1. What specific game features (e.g., FPS, MOBA) are you targeting, and what are the expected RPC call frequencies (e.g., 60Hz updates)?
2. Are there any pre-implemented parts in the fork (e.g., prototype UDP integration) beyond the docs folder?
3. What reliability guarantees are needed for different RPC types (e.g., fire-and-forget for positions, reliable for scores)?
4. Do you have preferred benchmarking tools or metrics (e.g., focus on latency percentiles like p99)?
5. Any constraints on .NET versions or platforms for the extension?
6. How should zone discovery work (e.g., manual config, service registry)?
7. Are there plans for encryption over UDP, or is it application-specific?
8. What upstream Orleans features must remain untouched?