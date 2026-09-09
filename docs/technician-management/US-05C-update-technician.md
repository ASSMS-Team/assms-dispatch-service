# US-05C — Update Technician Skills and Region

## Endpoint

`PUT /api/technicians/{id}` updates the Dispatch-owned technician's full name, region, skills, status, and optional contact details. The technician reference remains immutable.

The request requires a supported region and at least one non-empty skill. A successful response returns the saved technician. Unknown identifiers return `404 Not Found`; invalid skills or regions return `400 Bad Request` with field-specific validation details.

## Atomic persistence

The repository updates the technician row, deletes the previous skill set, and inserts the replacement skill set inside one database transaction. Validation occurs before the transaction begins. Therefore an invalid request performs no database update, and a database failure rolls back all changed rows.

## Service boundary

This endpoint reads and writes `technicians` and `technician_skills` in `dispatchdb` only. It does not create, query, or change Customer & Asset `StaffAccount` data.
