-- US-09 AC2: a Technician's assignment list shows the priority, location and
-- problem summary needed for field work. Dispatch keeps the JobCreated facts it
-- already receives next to job_reference instead of reading Job Service's
-- database. Nullable because evaluations recorded before this migration were
-- never given these values.
ALTER TABLE job_candidate_evaluations
    ADD COLUMN service_category    VARCHAR(20)   NULL AFTER job_reference,
    ADD COLUMN priority            VARCHAR(10)   NULL AFTER service_category,
    ADD COLUMN problem_description VARCHAR(1000) NULL AFTER priority;
