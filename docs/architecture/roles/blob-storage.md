---
role: BlobStorage
kind: adapter
status: accepted
---

# BlobStorage

## Responsibility

Store, retrieve, and stream files (product images, order attachments, invoice PDFs, shipping labels, maker documents). Adapter pattern.

## Collaborators

- (Callers pass byte streams and paths — this role does not interpret content)

## Knows

- The upstream service (Azure Blob Storage at launch)
- The container layout per ADR 0011 (`product-images`, `order-attachments`, `invoices`, `maker-documents`)
- The blob-path convention: `{country_code}/{entity}/{entity_id}/{filename}`

## Does NOT know

- Authorization (the calling controller checks ownership)
- File content semantics (it stores bytes; type validation is the caller's responsibility)
- Image transformations (a future image-proxy role, separate)

## Interface

```csharp
Task<BusinessResult> UploadAsync(string container, string path, Stream content, string contentType, CancellationToken ct)
Task<BusinessResult<Stream>> DownloadAsync(string container, string path, CancellationToken ct)
Task<BusinessResult> DeleteAsync(string container, string path, CancellationToken ct)
Task<BusinessResult<bool>> ExistsAsync(string container, string path, CancellationToken ct)
```

## Implementations

- **AzureBlobStorageClient** (`Infra.Azure.Storage.Blobs/`)
- Future: S3BlobStorageClient, MinioBlobStorageClient

All access is server-mediated (ADR 0011 — no direct browser → blob URLs).

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Storage/IBlobStorageClient.cs`.

## Related

- ADRs: 0011 (this role's defining ADR)
- Roles: `product`, `order`, `invoice`
