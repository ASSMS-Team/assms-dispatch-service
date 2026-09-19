# US-05B — View Technician Details and List

## Endpoints

| Method | Path | Result |
| --- | --- | --- |
| `GET` | `/api/technicians` | `200 OK` and an array of Dispatch-owned technicians. An empty array is a valid result. |
| `GET` | `/api/technicians/{id}` | `200 OK` with the stored technician, or `404 Not Found` when the id is unknown. |

Both responses include the technician id, reference, full name, region, status, skills, optional contact details and audit timestamps. The list is ordered by full name and then reference; skills are ordered alphabetically.

## Ownership boundary

The endpoints query `technicians` and `technician_skills` in `dispatchdb` only. They do not retrieve, create, or modify Customer & Asset `StaffAccount` records.

## Frontend routes

Dispatcher and Manager users can access `/technicians` and `/technicians/{id}`. The list presents reference/name, region, skills and active state; the detail screen presents the complete stored record. Agent and Technician roles are denied by the existing role guard.
