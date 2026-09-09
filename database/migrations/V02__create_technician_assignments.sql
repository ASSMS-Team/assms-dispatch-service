-- Dispatch retains this local assignment history even after a Technician is
-- deactivated. Job status is maintained by the assignment/status-sync flow;
-- it is deliberately not read from Job Service's database.
CREATE TABLE IF NOT EXISTS technician_assignments (
    id            CHAR(36)    NOT NULL,
    technician_id CHAR(36)    NOT NULL,
    job_id        CHAR(36)    NOT NULL,
    job_status    VARCHAR(20) NOT NULL,
    assigned_at   TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    released_at   TIMESTAMP   NULL,

    PRIMARY KEY (id),
    UNIQUE KEY uq_technician_assignments_job (job_id),
    KEY idx_technician_assignments_open (technician_id, released_at, job_status),
    CONSTRAINT fk_technician_assignments_technician
        FOREIGN KEY (technician_id) REFERENCES technicians(id)
);
