---
id: T-0042
title: IBlobStorageClient + AzureBlobStorageClient + per-container access policy
status: done
size: M
owner: dotnet-backend
created: 2026-05-27
updated: 2026-05-27
depends_on: [T-0001]
blocks: [T-0041]
adrs: [0011]
phase: 3
---

# T-0042 — Blob storage adapter

## Scope

Per ADR 0011. Storage adapter pattern + the four launch containers (`product-images` public-read, `order-attachments` / `invoices` / `maker-documents` private). No HTTP file-streaming endpoints in this ticket — those land per-feature (T-0041 product images, T-0061+ order attachments, etc.).

### Core.Domain (`Storage/`)
- `IBlobStorageClient.cs` — adapter interface per the role doc shape: `UploadAsync`, `DownloadAsync` (returns `BlobDownload` wrapping the stream + headers), `DeleteAsync`, `ExistsAsync`. All return `BusinessResult<T>` — no exceptions cross the boundary (same shape as T-0031 / T-0032 adapters).
- `BlobDownload` record — `Stream`, `ContentType`, `ContentLength`, `ETag?`. The CALLER owns disposal of `Stream` (the controller writes it through the HTTP response and disposes after).
- `BlobContainer` constants — `ProductImages`, `OrderAttachments`, `Invoices`, `MakerDocuments` + `All` array + `IsPublicRead(container)` helper.

### Core.Domain.Common
- `BusinessErrorMessage.{BlobNotFound, BlobUploadFailed, BlobDownloadFailed, BlobInvalidContainer, BlobInvalidPath}`.

### Infra.Azure.Storage.Blobs/
- `AzureBlobStorageOptions.cs` — two credential modes:
  - `ConnectionString` (local dev / CI / Azurite) — highest priority when set.
  - `ServiceUri` + `DefaultAzureCredential` (staging / production via Managed Identity; the App Service identity needs `Storage Blob Data Contributor` on the storage account).
  - `ClientApplicationId` for `x-ms-client-request-id` correlation. Defaults to `"makables-backend"`.
- `AzureBlobStorageClient.cs` — implementation. Container allow-list (rejects anything outside `BlobContainer.All` so a typo can't silently create a runtime container with default-private access policy that would break the public product-images path). Conservative path safety: no leading `/`, no `\`, no `.` / `..` segments, ≤1024 chars (Azure limit). `RequestFailedException` translation: 404 → `Error.NotFound("blob")`, other Azure errors → `Error.Transient(BusinessErrorMessage.Blob{Upload,Download}Failed)`. Logs the Azure status code on failure so ops can correlate with App Insights traces.

### Config
- `AddMakablesBlobStorage.cs` — registers options with `ValidateOnStart` (requires either `ConnectionString` or absolute-https `ServiceUri`), constructs `BlobServiceClient` (connection-string wins; else `Uri` + `DefaultAzureCredential`), and registers `IBlobStorageClient` as singleton. Same hardening shape as T-0031 / T-0032 options.
- Wired into all four Web hosts' `Program.cs`.
- `Makables.Config.csproj` adds `Azure.Storage.Blobs` + `Azure.Identity` package refs because the DI extension constructs both types directly.

### Tests (+23 facts; 756 total = 674 unit + 82 integration)
- `Infra/Storage/AzureBlobStorageClientValidationTests.cs` — pins the validation guard paths (invalid container, invalid path shapes including `..` traversal + `\\` + double-slash + leading-slash + 1024+ chars). Real SDK calls deferred to a future Azurite integration suite — the guard tests prove the adapter rejects malformed input BEFORE any network I/O.
- `BlobContainer.All` + `IsPublicRead` theory.
- Integration test config files (`HostStartup`, `JwtAuth`) updated with `AzureBlobStorage:ConnectionString = "UseDevelopmentStorage=true"` stub so all four hosts boot under `ValidateOnStart`.

### Out of scope
- Actual HTTP file-streaming endpoints — those land per-feature (T-0041 product-image upload+download, T-0061+ order attachments, T-0096+ invoices).
- Per-container provisioning (`product-images` public access policy) — ships via Bicep (T-0016 deploy templates) before staging deploy.
- Azurite-backed integration tests of the SDK round-trip — out of scope for this ticket; the validation guard tests + the host-boot smoke prove the wiring.
- Image transformations / responsive sizes — ADR 0011 §"Image transformations" notes this is post-MVP via a backend image-proxy.

## Acceptance criteria
- **AC-1** `IBlobStorageClient` exposes the four-method surface the role doc specifies; all return `BusinessResult<T>`; no `RequestFailedException` crosses the boundary.
- **AC-2** Container allow-list rejects anything outside the four launch containers with `Error.Validation(container, BlobInvalidContainer)` — caught before any network I/O.
- **AC-3** Path safety check rejects leading `/`, `\\`, `.`/`..` segments, double-slash, empty / whitespace, and paths >1024 chars (Azure limit).
- **AC-4** A 404 from Azure surfaces as `Error.NotFound("blob")`; other `RequestFailedException` becomes `Error.Transient(Blob{Upload,Download}Failed)` with the status code logged.
- **AC-5** `AzureBlobStorageOptions.ValidateOnStart` requires either `ConnectionString` (dev/CI) OR absolute-https `ServiceUri` (Managed Identity) — both empty crashes the host at boot.
- **AC-6** All four Web hosts boot under `ValidateOnStart` with the integration-test stub `UseDevelopmentStorage=true`; 82 integration tests still pass.
- **AC-7** `BlobContainer.IsPublicRead` returns true ONLY for `product-images`; the other three are private.
- **AC-8** 756 tests pass (674 unit + 82 integration; +23 new T-0042 facts).
- **AC-9** CLAUDE.md hygiene: no `Console.*`; `Core.Domain` no third-party packages; HTTP not yet wired but the adapter is a thin mapping layer.

## Status log
- 2026-05-27 done. Build clean, 756 tests pass. Awaiting dual reviewer per workflow.
