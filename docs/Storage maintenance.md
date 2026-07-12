# Storage Maintenance

Octans continuously treats the database and content-addressed filesystem as two parts of one store. Storage maintenance provides durable, restartable jobs that reconcile those parts without silently deleting original bytes.

## Background behavior

The maintenance worker only starts work while no import job or download is queued or running. Manual scans and repairs are durable queue entries, so requesting one does not compete with foreground work. Interrupted scans are restarted from the beginning after application startup; interrupted repair jobs resume with their unresolved findings.

When automatic scans are enabled, Octans queues a scan after the configured interval has elapsed. The defaults are:

```json
"StorageMaintenance": {
  "AutomaticScansEnabled": true,
  "AutomaticScanIntervalDays": 7,
  "IdlePollSeconds": 30,
  "PersistenceBatchSize": 100
}
```

The maintenance dashboard is available at `/maintenance/storage` under **database → storage health**.

## Scan coverage

A scan records durable findings for:

- missing originals and thumbnails;
- orphaned originals and thumbnails;
- malformed storage filenames;
- files in the wrong hash bucket or deterministic path;
- duplicate originals and thumbnails;
- original bytes that do not match their recorded SHA-256 hash;
- extension and content-type metadata that disagrees with the stored content.

Deleted database rows are not expected to retain physical content. Any remaining bytes for them are therefore reported as orphaned and preserved until a repair is explicitly requested.

## Repairs and quarantine

The safe repair action can:

- regenerate missing thumbnails from an intact original;
- detect and repair extension/content-type metadata, moving the original to its corrected deterministic path;
- relocate files that are merely misplaced;
- move orphaned, duplicate, malformed, or hash-mismatched files into quarantine.

Quarantine lives under `AppRoot/db/maintenance/quarantine/<repair-job-id>/` and preserves the content-store-relative path. Repair never silently deletes an original file. Failures are recorded per finding and do not stop the remainder of the repair job.

## API

- `POST /api/maintenance/storage/scans` queues a manual scan.
- `GET /api/maintenance/storage/jobs` lists recent durable jobs.
- `GET /api/maintenance/storage/jobs/{id}` returns job progress and outcome.
- `GET /api/maintenance/storage/scans/{scanJobId}/findings` returns paged, filterable findings.
- `POST /api/maintenance/storage/scans/{scanJobId}/repairs` queues selected safe repair actions.
- `POST /api/maintenance/storage/jobs/{id}/cancel` requests cancellation.

Findings accept optional `resolution`, `type`, `skip`, and `take` query parameters. The maximum page size is 1,000.
