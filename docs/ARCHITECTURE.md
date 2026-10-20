# Workmate HRM PoC: Architecture

The diagrams render on GitHub, GitLab, Azure DevOps and in VS Code (Markdown preview with Mermaid support).
Colours show where each part comes from:

| Colour | Source | Licence |
| --- | --- | --- |
| Blue | **ABP Framework 9.1** (open-source packages only) | LGPL-3.0 |
| Orange | **Elsa Workflows 3.7** | MIT |
| Green | **SurveyJS Form Library 3.2** (renderer only) | MIT |
| Purple | **Our code** (Workmate.Poc) | ours |
| Grey | .NET 10 / ASP.NET Core / EF Core / SQLite | MIT / public domain |

## 1. Components and where they come from

```mermaid
flowchart TB
  classDef abp fill:#dbeafe,stroke:#1d4ed8,color:#0b1f4d
  classDef elsa fill:#ffedd5,stroke:#c2410c,color:#431407
  classDef survey fill:#dcfce7,stroke:#15803d,color:#052e16
  classDef ours fill:#ede9fe,stroke:#6d28d9,color:#2e1065
  classDef base fill:#f1f5f9,stroke:#475569,color:#0f172a

  subgraph Browser["Browser (wwwroot)"]
    UI["index.html<br/>demo UI: requests, inbox,<br/>form / workflow / routing designers"]:::ours
    RJS["js/renderers.js<br/>createRenderer: native + SurveyJS adapter"]:::ours
    SJS["SurveyJS Form Library<br/>survey-core + survey-js-ui<br/>(vendored in lib/surveyjs)"]:::survey
    UI --> RJS --> SJS
  end

  subgraph Host["ASP.NET Core host (.NET 10)"]
    direction TB
    subgraph Web["Web layer"]
      MW["DemoUserMiddleware<br/>fake sign-in (X-Demo-User)"]:::ours
      MT["Multi-tenancy middleware<br/>tenant from __tenant header"]:::abp
      UOW["Unit of Work middleware"]:::abp
      CTRL["PocController<br/>/api/poc/..."]:::ours
      MVC["ABP AspNetCore.Mvc<br/>controllers, validation, exceptions"]:::abp
    end

    subgraph App["Application layer"]
      AS["FormAppService, RoutingAppService,<br/>SubmissionAppService, TaskAppService,<br/>ApprovalWorkflowAppService"]:::ours
      PM["ProcessManager<br/>binding, start after commit,<br/>tasks, outcomes"]:::ours
      AR["AssignmentResolver<br/>line manager, role, user, fallback"]:::ours
      CEP["CurrentEmployeeProvider<br/>+ admin check"]:::ours
      APPB["ABP Ddd.Application<br/>ApplicationService base"]:::abp
    end

    subgraph Dom["Domain + data"]
      DOM["Forms, Processes,<br/>ApprovalWorkflows, Employee"]:::ours
      DBC["PocDbContext + DemoDataSeeder"]:::ours
      EFA["ABP EntityFrameworkCore.Sqlite<br/>repositories, IMultiTenant filter,<br/>audit fields"]:::abp
      TS["ABP config tenant store<br/>(Acme, Globex)"]:::abp
    end

    subgraph WE["WorkflowEngine (only door into Elsa)"]
      GW["IWorkflowGateway /<br/>ElsaWorkflowGateway"]:::ours
      GB["ApprovalWorkflowGraphBuilder<br/>designed steps to Elsa graph"]:::ours
      ACT["Custom activities<br/>HumanTask, CompleteProcess"]:::ours
      WFC["Code workflows<br/>leave-approval, hr-only-approval"]:::ours
    end

    subgraph Elsa["Elsa Workflows 3"]
      RT["Workflow runtime<br/>instances, bookmarks, resume"]:::elsa
      MG["Workflow management<br/>definitions, versions, publish"]:::elsa
      FC["Flowchart + built-in activities"]:::elsa
      EP["Elsa.Persistence.EFCore.Sqlite"]:::elsa
    end

    DI["ABP Autofac<br/>DI + module system"]:::abp
  end

  DB1[("workmate-poc.db<br/>Hr_, Forms_, Proc_ tables")]:::base
  DB2[("workmate-elsa.db<br/>Elsa tables")]:::base

  UI -->|"HTTP + JSON"| MW --> MT --> UOW --> CTRL
  CTRL --> MVC
  CTRL --> AS
  AS --> APPB
  AS --> PM
  AS --> CEP
  PM --> AR
  PM --> GW
  AS --> DOM
  DOM --> DBC --> EFA --> DB1
  MT --> TS
  GW --> RT
  GW --> MG
  GB --> FC
  ACT -->|"create task / finish"| PM
  RT --> ACT
  WFC --> FC
  RT --> EP
  MG --> EP
  EP --> DB2
```

## 2. Runtime flow: submit and approve

```mermaid
sequenceDiagram
  autonumber
  actor E as Employee (browser)
  participant UI as Renderer (native or SurveyJS)
  participant API as SubmissionAppService (ours)
  participant UOW as ABP Unit of Work
  participant PM as ProcessManager (ours)
  participant GW as ElsaWorkflowGateway (ours)
  participant ELSA as Elsa runtime
  participant HT as HumanTask activity (ours)
  actor A as Approver

  E->>UI: fill form (version N)
  UI->>API: POST /submissions (formKey, data)
  API->>API: validate against version N (required, types, options, visibleIf)
  API->>UOW: save submission + typed values, pick binding, create process
  UOW-->>PM: OnCompleted (after commit)
  PM->>GW: StartAsync(definition, input incl. tenant id)
  GW->>ELSA: create and run instance
  ELSA->>HT: run step
  HT->>PM: CreateTask (AssignmentResolver gives candidates, submitter excluded)
  HT-->>ELSA: bookmark, wait
  A->>API: POST /tasks/{id}/complete (Approve or Reject)
  API->>GW: InstanceExistsAsync (before commit)
  API->>UOW: mark task done, add timeline event
  UOW-->>GW: OnCompleted: ResumeAsync(instance, bookmark, outcome)
  GW->>ELSA: resume
  ELSA->>HT: next step, or CompleteProcess (final status to domain)
```

## 3. Admin design flow (no code)

```mermaid
flowchart LR
  classDef ours fill:#ede9fe,stroke:#6d28d9,color:#2e1065
  classDef elsa fill:#ffedd5,stroke:#c2410c,color:#431407
  classDef survey fill:#dcfce7,stroke:#15803d,color:#052e16

  FD["Form designer<br/>fields, visibleIf, pages,<br/>renderer setting"]:::ours --> FV["Form version<br/>(immutable once published)"]:::ours
  FV --> R1["Native renderer"]:::ours
  FV --> R2["SurveyJS adapter"]:::survey
  WD["Workflow designer<br/>steps, approver type,<br/>fallback, on-reject"]:::ours --> WV["Workflow version<br/>(own tables)"]:::ours
  WV --> GB["GraphBuilder"]:::ours --> PUB["Elsa definition<br/>published via gateway"]:::elsa
  RD["Routing<br/>field equals value, priority"]:::ours --> BIND["ProcessBinding<br/>form to workflow"]:::ours
  BIND --> PUB
```

## 4. Multi-tenancy

- Tenant comes from the `__tenant` header (ABP `UseMultiTenancy`) and is checked against the signed-in demo user.
- Every domain entity implements ABP `IMultiTenant`; ABP's data filter scopes every query to the current tenant.
- The tenant id travels in workflow input and is re-applied inside Elsa activities (Elsa's own multi-tenancy is not used).
- Designed workflows, routes, forms and renderer settings are tenant data in our tables; Elsa only holds generated definitions and running instances.

## 5. What production changes

| PoC | Production |
| --- | --- |
| `DemoUserMiddleware` fake sign-in | ABP Identity + OpenIddict + Account (spec 003) |
| Config tenant store | ABP TenantManagement (spec 003) |
| SQLite files | SQL Server; Elsa in its own schema on the same server |
| Start/resume via `OnCompleted` | ABP distributed event bus + outbox (Phase F) |
| Single project | ABP modular monolith: Forms, Processes, WorkflowEngine, HR modules |
