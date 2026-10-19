# Feature Specification: Real sign-in and client onboarding

**Feature Branch**: `003-login-onboarding` | **Created**: 2026-10-20 | **Status**: Draft (seeded)

**Input**: Ahmad, 2026-10-20: "Add login. Sign-up must not be available for client users. The vendor admin creates an admin user for each client; that admin then adds employees and the organisation structure."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Vendor onboards a new client (Priority: P1)
Aramis Support signs in to the host (vendor) area, creates a company (tenant) "Initech" with the client admin's name and email. The admin receives credentials (or a set-password link) and can sign in to Initech only.

**Acceptance Scenarios**:
1. **Given** the vendor admin, **When** they create a company with an admin email, **Then** the tenant and its admin user exist, the admin has the company-admin permissions, and an invitation is sent (email in spec 005; shown on screen until then).
2. **Given** anyone who is not the vendor admin, **When** they try to create a company, **Then** it is refused.

### User Story 2 - Everyone signs in; nobody signs up (Priority: P1)
Employees sign in with email + password. There is no sign-up link or endpoint. The company is resolved from the user's account (or the sign-in page's company field), never from a header a user can change.

**Acceptance Scenarios**:
1. **Given** the sign-in page, **Then** there is no "Register" option, and the register endpoint returns 403/404.
2. **Given** a user of Acme, **When** they send Globex's tenant id, **Then** access is refused (same guarantee as README step 5, now with real auth).
3. **Given** wrong password N times, **Then** the account is locked for a period (ABP Identity lockout defaults).
4. **Given** a signed-in user, **Then** menus show only what their permissions allow (design, routing, employees, org: admins only).

### User Story 3 - Permissions replace the PoC flags (Priority: P2)
`IsAdmin` and the vendor-admin id become ABP permissions/roles: `Workmate.Forms.Design`, `Workmate.Routing.Manage`, `Workmate.Workflows.Design`, `Workmate.Employees.Manage`, `Workmate.Org.Manage`; host-only `Workmate.Tenants.Manage`.

### Edge Cases
- Client admin leaves: vendor admin can reset/assign another admin.
- Forgot password: set-password link by email (spec 005); until then the vendor admin resets it.
- Deactivated user with pending approval tasks: tasks go to the workflow fallback (spec 002 FR-010).

## Requirements *(mandatory)*
- **FR-001**: Use ABP open-source modules only: Identity, OpenIddict, Account, TenantManagement, PermissionManagement (+ their EF Core packages), all on ABP 9.1.0.
- **FR-002**: Self-registration MUST be disabled (`Abp.Account.IsSelfRegistrationEnabled = false`) and the register page/endpoint unavailable.
- **FR-003**: Tenants MUST move from the config store (`DemoTenants`, `AbpDefaultTenantStoreOptions`) to the TenantManagement store; demo tenants are seeded through it.
- **FR-004**: Each `Employee` MUST link to an Identity user (`UserId`), one user per employee per tenant; the vendor admin is a host user with no employee.
- **FR-005**: `CurrentEmployeeProvider` MUST resolve the employee from the signed-in user; `DemoUserMiddleware` and the `X-Demo-User` header are removed.
- **FR-006**: Admin checks MUST use permissions (Principle VII), not `IsAdmin`.
- **FR-007**: The demo UI MUST sign in via the OpenIddict authorization-code flow (or cookie sign-in for the PoC page) and call the API with the resulting token/cookie. [NEEDS CLARIFICATION: cookie sign-in on the same site (simplest for the PoC) or token-based for a future SPA/mobile client?]
- **FR-008**: `demo.py` MUST sign in as each demo user (password grant or a test-only token endpoint) instead of sending `X-Demo-User`.

## Success Criteria *(mandatory)*
- **SC-001**: Vendor creates a company + admin; admin signs in and sees an empty company (demo.py step 10).
- **SC-002**: No sign-up path exists; cross-tenant access is refused with real tokens.
- **SC-003**: demo.py steps 1–9 pass with real sign-in.

## Files likely touched
`Workmate.Poc.csproj` (ABP Identity/OpenIddict/Account/TenantManagement/PermissionManagement packages), `WorkmatePocModule.cs` (module dependencies, auth, remove config tenant store), `Data/PocDbContext.cs` (configure the module tables), `Data/DemoDataSeeder.cs` (seed tenants, users, roles, permissions), `Domain/DemoTenants.cs` (ids only, or removed), `Domain/Employee.cs` (`UserId`, `Email`; `IsAdmin` removed), `Http/DemoUserMiddleware.cs` (deleted), `Application/CurrentEmployeeProvider.cs`, every `*AppService.cs` (`[Authorize(permission)]`), new `Application/Permissions.cs`, new `Application/TenantOnboardingAppService.cs`, `Controllers/PocController.cs`, `wwwroot/index.html` (sign-in screen, remove "Acting as"), `demo.py`, `README.md`, `.specify/memory/constitution.md` (boundary on fake sign-in).

## Assumptions
- No MFA/SSO in the PoC; Azure AD / SSO per client is production work.
- One database for all tenants (shared schema), as today.
