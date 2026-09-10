-- US-06A: durable, Dispatch-owned candidate evaluation. This stores no Job
-- record and does not create an assignment; both remain owned by later stories.
CREATE TABLE job_candidate_evaluations (
    id BIGINT NOT NULL AUTO_INCREMENT,
    event_id CHAR(36) NOT NULL,
    job_id CHAR(36) NOT NULL,
    job_reference VARCHAR(50) NOT NULL,
    required_skill VARCHAR(50) NOT NULL,
    normalized_region VARCHAR(50) NOT NULL,
    evaluation_status VARCHAR(30) NOT NULL,
    candidate_count INT NOT NULL,
    requires_dispatcher_attention BOOLEAN NOT NULL DEFAULT FALSE,
    occurred_at DATETIME(6) NOT NULL,
    evaluated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    UNIQUE KEY uq_job_candidate_evaluations_event_id (event_id),
    KEY ix_job_candidate_evaluations_job_id (job_id),
    CONSTRAINT chk_job_candidate_evaluation_status
        CHECK (evaluation_status IN ('PROCESSING', 'CANDIDATES_FOUND', 'NO_CANDIDATE'))
);

CREATE TABLE job_candidate_evaluation_candidates (
    evaluation_id BIGINT NOT NULL,
    technician_id CHAR(36) NOT NULL,
    PRIMARY KEY (evaluation_id, technician_id),
    CONSTRAINT fk_candidate_evaluation_candidate_evaluation
        FOREIGN KEY (evaluation_id) REFERENCES job_candidate_evaluations(id),
    CONSTRAINT fk_candidate_evaluation_candidate_technician
        FOREIGN KEY (technician_id) REFERENCES technicians(id)
);
