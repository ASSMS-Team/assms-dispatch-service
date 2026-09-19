# US-06A — eligible technician candidates

Dispatch consumes `JobCreated` from Kafka topic `job-created` as consumer group
`assms-dispatch-job-created`. It validates the envelope (`eventId`, event type,
version, producer, job identity, region and timestamp), then evaluates the
current Dispatch-owned technician data in `dispatchdb`.

## Matching rule

`JobCreated` contains a `serviceCategory`, not a required skill. The required
skill is therefore resolved from the deployment configuration:

```json
{
  "CandidateMatching": {
    "RequiredSkillByServiceCategory": {
      "REPAIR": "AC"
    }
  }
}
```

Set every supported Job Service category in the staging configuration after BA
confirms the category-to-skill rules. Dispatch does **not** infer a skill from
the free-text problem description. An event whose category has no configured
mapping is quarantined with its event and job identifiers and does not change
assignment state.

For a mapped event, the parameterized ADO.NET query returns only technicians
whose status is `ACTIVE`, normalized region equals the job region, and skill
equals the resolved required skill. It runs fresh for every new event, so a
later technician update or deactivation is used by later candidate evaluations.

## Durability and failure behaviour

Migration `V03__create_job_candidate_evaluations.sql` creates an evaluation
header and candidate rows. The unique `event_id` makes duplicate delivery
idempotent. Reservation, candidate query, no-match record and candidate rows
are one database transaction. A failure rolls the transaction back; the Kafka
offset is sought and retried. Malformed/unmappable events are committed after a
diagnosable quarantine log so they cannot block the partition forever.

No candidate produces a `NO_CANDIDATE` evaluation with
`requires_dispatcher_attention = true`; it creates no assignment. Assignment
selection and `JobAssigned` publishing belong to US-06B.
