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
    direction LR
    UI["index.html<br/>requests, inbox, designers"]:::ours
    RJS["js/renderers.js<br/>native renderer + SurveyJS adapter"]:::ours
    SJS["SurveyJS Form Library<br/>survey-core, survey-js-ui"]:::survey
    UI --> RJS --> SJS
  end

  subgraph Web["Web layer"]
    direction LR
    MW["DemoUserMiddleware<br/>fake sign-in"]:::ours
    MT["Multi-tenancy<br/>tenant resolver + config store"]:::abp
    UOW["Unit of Work"]:::abp
    MVC["AspNetCore.Mvc<br/>routing, validation, errors"]:::abp
    CTRL["PocController<br/>/api/poc/..."]:::ours
    MW --> MT --> UOW --> MVC --> CTRL
  end

  subgraph App["Application layer"]
    direction LR
    AS["App services<br/>Form, Routing, Submission,<br/>Task, ApprovalWorkflow"]:::ours
    APPB["Ddd.Application<br/>ApplicationService base"]:::abp
    PM["ProcessManager<br/>start after commit, tasks"]:::ours
    AR["AssignmentResolver<br/>manager, role, user, fallback"]:::ours
    AS --- APPB
    AS --> PM --> AR
  end

  subgraph WE["WorkflowEngine: only door into Elsa"]
    direction LR
    GW["IWorkflowGateway<br/>ElsaWorkflowGateway"]:::ours
    GB["ApprovalWorkflowGraphBuilder"]:::ours
    ACT["Activities<br/>HumanTask, CompleteProcess"]:::ours
    WFC["Code workflows"]:::ours
  end

  subgraph Elsa["Elsa Workflows 3.7"]
    direction LR
    RT["Runtime<br/>instances, bookmarks"]:::elsa
    MG["Management<br/>definitions, publish"]:::elsa
    FC["Flowchart activities"]:::elsa
    EP["Persistence.EFCore.Sqlite"]:::elsa
  end

  subgraph Data["Domain and data"]
    direction LR
    DOM["Entities<br/>Forms, Processes,<br/>ApprovalWorkflows, Employee"]:::ours
    DBC["PocDbContext<br/>DemoDataSeeder"]:::ours
    EFA["EntityFrameworkCore.Sqlite<br/>repositories, tenant filter"]:::abp
    DOM --> DBC --> EFA
  end

  DI["ABP Autofac: DI and module system (hosts everything)"]:::abp
  DB1[("workmate-poc.db<br/>Hr_, Forms_, Proc_")]:::base
  DB2[("workmate-elsa.db<br/>Elsa tables")]:::base

  Browser -->|HTTP + JSON| Web
  Web --> App
  App --> Data
  PM --> GW
  WE --> Elsa
  ACT -->|create task, finish| PM
  EFA --> DB1
  EP --> DB2
  Data ~~~ DI
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
