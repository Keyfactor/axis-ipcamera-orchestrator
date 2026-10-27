v1.2.0
- feat(tls): `Use SSL` now controls HTTP vs HTTPS; added `Bypass TLS Validation` certificate store property to skip certificate validation when using HTTPS
- fix(tls): Replaced custom Axis.Root/Axis.Intermediate chain-pinning with standard local trust-store validation; camera's `SERIALNUMBER` is matched against `Store Path` only on a hostname/SAN mismatch against an otherwise-trusted chain, with a specific error message on mismatch
- fix(inventory): Fixed Inventory reporting Success with zero certificates when the camera returned none
- feat(connection): Added optional `HTTP Port` (used when `Use SSL` is false) and `HTTPS Port` (used when `Use SSL` is true) certificate store properties; a port set replaces any port in `Client Machine`
- fix(odkg): Reenrollment no longer reports a Warning when the previous certificate can't be deleted because the camera won't remove it (e.g. the factory device ID certificate on a first reenrollment); a previous certificate still bound to another Certificate Usage is now left in place instead of attempting deletion
- fix(odkg): ODKG with Certificate Usage `Other` no longer reports a Warning; clearer errors when Command returns no certificate for the CSR and when the `Trust` usage is selected

v1.1.0
- fix(logging): Improved exception handling to be more robust for HTTP client initialization
- fix(auth): Implemented .NET native credential negotiation to support both Basic & Digest auth policies
- chore(docs): Updated doctool release actions to v5
- chore(build): Updated .NET target frameworks to net8.0 and net10.0
- fix(odkg): Fixed incorrect mapping of ECDSA algorithm to ECP. Updated to ECDSA.
- feat(odkg): Implemented alias versioning to enable reuse of existing alias names in automation processes

v1.0.2
- fix(logs): Removed logging of plaintext cert store Server Password 
- fix(keystore): Updated Keystore type to be dynamic instead of a fixed Enum to allow compatibility across different cameras/firmware

v1.0.1
- chore(docs): Add screenshots to docs

v1.0.0
- Initial Public Version