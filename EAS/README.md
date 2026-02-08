# EAS (Emergency Alert System)

This folder contains provider implementations and shared models for handling emergency alerts.

Structure:

- `Providers/` - provider interfaces and helpers
- `Options/` - provider-specific configuration classes
- `AlertReady/` - Alert Ready (Canada) implementation (moved here)
- `NWS/` - EAS-NWS (United States) client implementation (fetches and parses NWS CAP feeds)

Next steps:
- Add unit tests and expand NWS CAP parsing and atom/geo handling.
- Consider splitting providers into separate projects if tighter isolation or independent versioning is required.
