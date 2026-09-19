# US-05A — Create Technician

`POST /api/technicians` creates a Dispatch-owned technician record. It does not
create or query a Customer & Asset `StaffAccount`, create login credentials, or
introduce a cross-service foreign key.

## Request

```json
{
  "reference": "TEC-032",
  "fullName": "Tharindu Jayasena",
  "region": "WESTERN",
  "skills": ["Electrical", "AC"],
  "status": "ACTIVE",
  "phone": "+94 77 123 4567",
  "email": "tharindu@assms.lk"
}
```

`reference`, `fullName`, `region`, `skills`, and `status` are required. The
reference is normalized to uppercase and must be unique. `region` uses the same
nine province values as Job Service. At least one non-empty skill is required;
phone and email are optional.

## Responses

- `201 Created` returns the persisted Technician and a `Location` header for
  `GET /api/technicians/{id}`.
- `400 Bad Request` returns field-keyed validation errors for missing or invalid
  input, including an empty skill list.
- `409 Conflict` returns a field-keyed `reference` error when it is already in use.
- `404 Not Found` from `GET /api/technicians/{id}` means no Dispatch record has
  that identifier.

## Persistence

`database/migrations/V01__create_technicians.sql` creates `technicians` and
`technician_skills` in `dispatchdb`. The service writes the record and all skills
inside one transaction, so a failed skill insert cannot leave a partial
Technician record behind.
