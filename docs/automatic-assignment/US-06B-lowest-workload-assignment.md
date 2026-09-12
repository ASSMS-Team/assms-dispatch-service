# US-06B — lowest-workload automatic assignment

After US-06A stores an eligible candidate set for a valid `JobCreated` event,
Dispatch selects one currently active candidate. It counts assignments with a
null `released_at` and a `job_status` other than `COMPLETED` or `CANCELLED`.

## Deterministic selection

Candidates are selected in this order:

1. Lowest open-job count.
2. Oldest `lastAssignedAt`; a technician with no historical assignment is first.
3. Ascending ordinal `technicianReference`.

The final comparison runs in C# with `StringComparer.Ordinal`, so the result
does not depend on the database server's text collation.

## Transaction and duplicate behaviour

The `technician_assignments` row and its `assignment_outbox` row are inserted
in one serializable Dispatch database transaction. `technician_assignments.job_id`
is unique, so repeated delivery cannot create a second active assignment. If a
consumer fails after US-06A commits but before US-06B commits, the duplicate
candidate evaluation is safely resumed by the idempotent assignment operation.

## JobAssigned publication

The outbox stores a complete `JobAssigned` envelope under a unique `eventId`.
`JobAssignedOutboxPublisher` reads pending entries, publishes to `job-assigned`
with Kafka `acks=all`, and only marks an entry published after broker
acknowledgement. A failure keeps the assignment and pending outbox record;
the publisher records the error and retries later. This guarantees one business
effect through durable producer retry plus consumer idempotency, not Kafka
exactly-once delivery.
