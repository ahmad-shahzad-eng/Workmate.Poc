# Quickstart: validate spec 007

## Prerequisites

- Mailpit running (`mailpit &`), as for spec 005.
- **Reset once** (`dotnet run -- --Poc:ResetDatabaseOnStartup=true`): new `Ui_CompanyBranding` table and the `Workmate.Branding.Manage` grant.
- `dotnet build`, then run the app.

## Automated: `python3 demo.py`

Steps 1–13 and the restart check must still pass unchanged (FR-011). New **step 14** ([contracts/api.md](./contracts/api.md)):

| Check | Proves |
|---|---|
| 14a | Seeded branding: Acme reads `#0f6e6e` and Globex `#4a4fb0`, each with a logo URL that returns an image (FR-012) |
| 14b | Khalid saves Acme's name and colour; Sara (Acme) reads them; Ali (Globex) still reads Globex's (US2 AC1, Principle IV) |
| 14c | The vendor admin acting as Acme, then as Globex, reads each one's branding; acting as nobody reads the product defaults (US2 AC2) |
| 14d | Refused, each with a message: colour `red`, a 201 KB file, a text file named `.png`, an SVG containing `<script>` or an `onload=` attribute (US2 AC3, FR-007) |
| 14e | Contrast: `#ffff00` → `onBrandColor #000000`; `#0f6e6e` → `#ffffff`; `brandTextColor` for `#ffff00` reaches ≥ 4.5:1 on white (computed in `demo.py`) (SC-005) |
| 14f | Sara gets 403 on `PUT branding`; anonymous `GET branding/public?company=Acme Trading` returns only the public fields; the logo response has the CSP `sandbox` and `nosniff` headers (FR-008, R8) |
| 14g | After 14b, Sara's next "submitted" email in Mailpit has the new colour and display name in its HTML part (FR-009) |
| 14h | `GET /` links only same-origin theme assets (no `http(s)://` to another host), and each one returns 200 (FR-013) |
| 14i | `DELETE branding` puts Acme back to the product defaults; re-saving the seed values restores the demo look (US2 AC5) |

## Manual (UI)

1. Sign in as Sara, Khalid and the vendor admin: the side menu shows only each person's sections, the top bar shows their name and company, and Inbox shows its count (US1 AC1–3).
2. As Khalid → **Branding**: upload a logo, pick a colour (try a very light one and check the button text stays readable), save; reload as Sara to see it; Globex unchanged.
3. Phone width (360 px): the sidebar becomes the mobile menu and every section is usable (US1 AC5, SC-004).
4. RTL toggle: the sidebar moves to the right and the layout mirrors (theme `.rtl.css`).
5. A leave request in both renderers (native and SurveyJS) looks consistent with the theme and still submits (US1 AC4).
6. The sign-in screen: pick Acme, then Globex; logo, name and colour follow. Open a set-password link from Mailpit: it shows that company's branding.
7. An email in Mailpit (http://localhost:8025) shows the brand colour and display name.
