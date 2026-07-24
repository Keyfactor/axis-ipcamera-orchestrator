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