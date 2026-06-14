## Summary

- 

## Verification

- [ ] `dotnet build AAIA.ModuleManager.sln`
- [ ] Windows checked, if affected
- [ ] macOS checked, if affected
- [ ] Linux checked, if affected

## Platform/Auth/API Gate

Check this section before merging any change that touches one of these areas:

- `MarketplaceApiUrl`
- login, registration, TOTP, JWT, ETW account state
- config file location or persisted account fields
- backend endpoint paths or request/response contracts
- Windows/macOS/Linux-specific behavior

Confirmation:

- [ ] Not applicable; this PR does not change platform/auth/API behavior.
- [ ] Applicable; Windows, macOS, and Linux behavior has been compared or the missing platform check is documented below.
- [ ] If `MarketplaceApiUrl` changed, all platforms still use the same intended API for the same account namespace.
- [ ] If auth/TOTP changed, the request/response contract is still compatible with the Marketplace API.
- [ ] If config persistence changed, migration/clearing steps are documented for all affected platforms.

Notes for missing checks or intentional differences:

- 
