# AlertReady (Canada)

This folder contains the refactored Alert Ready implementation. The implementation now lives in the `EAS.AlertReady` namespace and the public compatibility wrapper `EAS.AlertReadyClient` remains at the package root for backwards compatibility.

Files of interest:
- `AlertReadyClient.cs` - full implementation (moved) for NAAD / Alert Ready feeds
- `../Options/AlertReadyOptions.cs` - configuration model (kept in `EAS` namespace for compatibility)

Guidance:
- To use the provider directly in new code, prefer `EAS.AlertReady.AlertReadyClient`.
- For compatibility with existing code, `EAS.AlertReadyClient` at the root namespace delegates to the implementation.
